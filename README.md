# Bird Platformer

**This is a Kubernetes, CI/CD, and cloud infrastructure learning project.** The Unity game is not the point, in fact I used AI heavily to get the game playable. I'm not a game developer and I'm not trying to become one (too complicated for me!!). The Unity work
stopped once the game was substantial enough to be a realistic deployment
target.

I wanted to learn Kubernetes properly, and I didn't want to do it against a
generic "hello world" container. Since I had started learning Unity earlier in the year, where
I wanted to build a small 2D platformer, I decided to leverage that to build a real-world deployment pipeline. The interesting work is everything downstream of it: the build
pipeline, the container, and the manifests.

## What actually runs, but still a WIP

```
Unity project ──► GitHub Actions ──► Web build ──► nginx container ──► Kubernetes
   (Assets/)        (tests, then        (web/game/)      (docker/)         (k8s/)
                     the build)

                                                    Flask + Postgres ──► Kubernetes
                                                          (api/)            (k8s/)
```

It started as a static site on a Deployment. It's now a multi-tier application:
a game tier and a leaderboard API behind one hostname, with a database on
persistent storage and network policies that stop the game pods from reaching it.

```
   [ player's browser ]  ← the game runs HERE, outside the cluster
        |  GET  http://bird.local/            → game files
        |  POST http://bird.local/api/scores  → the score
        v
   +-- Ingress (nginx) --------------------------+
   |   /     -> bird-platformer-service          |
   |   /api  -> leaderboard-service              |
   +---------------------------------------------+
        |                          |
        v                          v
   [ nginx pods x3 ]         [ flask pods x2 ] ──► [ postgres ] ──► [ PVC ]
```

| Path | What it is |
|---|---|
| `.github/workflows/ci.yml` | Runs Unity EditMode + PlayMode tests, then the Web build. Caches the Unity `Library/` folder so runs don't recompile every asset from scratch, and renders test results onto the run summary page. |
| `api/` | The leaderboard service. Flask + SQLAlchemy + gunicorn, backed by Postgres. Separate `/healthz` and `/readyz` endpoints, config from a ConfigMap, credentials from a Secret. |
| `docker/` | How the game image is assembled; `nginx:alpine` serving the build, plus a `/healthz` endpoint that answers without touching a real page. |
| `k8s/` | How it's deployed; namespace, two Deployments, Postgres with a PVC, ConfigMaps, a Secret, an Ingress with path routing, and enforced NetworkPolicies. See [`k8s/README.md`](k8s/README.md) for the full walkthrough. |
| `k8s/notes/` | A running symptom → cause → fix log. Probably the most useful thing in the repo. |
| `web/` | What gets served. `web/game/` is the build output and is gitignored, so it isn't present after a fresh clone. |

The split between `api/`, `docker/`, `k8s/`, and `web/` is deliberate: the
service, assembly, deployment, and content stay separate, so nothing in `web/`
can leak into server configuration by accident.

## Some things this forced me to learn the hard way

- **Docker build context is not the Dockerfile's directory.** `docker/Dockerfile`
  copies from `docker/nginx/...` and `web/game/` because the context stays at the
  repo root. Getting this wrong produces a confusing "file not found" that has
  nothing to do with the file being missing.
- **`minikube image load` silently fails** against Docker Desktop's containerd
  image store. The fix is `eval $(minikube docker-env)` and building directly
  inside minikube's daemon.
- **NodePort on macOS with the Docker driver needs `minikube service`** to open a
  tunnel — and that tunnel dies when you close the terminal, which looks
  identical to the deployment being broken.
- **Mounting a ConfigMap at a directory path replaces the whole directory.**
  Moving the nginx config out of the image and into a ConfigMap is a two-line
  change that fails in a confusing way if you mount it over `/etc/nginx/conf.d`
  without thinking about what else lives there.
- **A NetworkPolicy is a memo, not a wall.** minikube's default CNI stores the
  objects and enforces none of them, so an unenforced policy looks exactly like
  an enforced one until traffic you thought was blocked gets through. Checking
  the CNI is now the first thing I do.
- **A pod can be perfectly healthy while the site is down.** After applying a
  default-deny policy every pod stayed `Running`, `Ready`, and listed in its
  Service's endpoints — because readiness probes come from the kubelet, which
  dials the pod IP directly and never touches the Ingress. Two different
  meanings of "reachable", and only one of them was broken.
- **`create_all()` at import time races itself.** Two replicas × two gunicorn
  workers meant four processes trying to create the same tables at once, and the
  losers crashed with a unique-constraint violation on a sequence. It self-healed
  on retry, which is exactly why it was easy to miss.
- **An Ingress can only forward to Services in its own namespace.** Moving the
  game into a namespace meant moving the Ingress with it, and the symptom of not
  doing so is a 503 with `<error: services "..." not found>` in the backend
  column of `kubectl describe ingress`.
- **Unity's build output path moves between GameCI versions**, so the workflow
  locates `index.html` and works back from it rather than hardcoding a path.
- **Platform naming drift**: Unity 6 calls the target "Web" in its UI, but the
  underlying `BuildTarget` enum is still `WebGL`, which is what GameCI expects.

## Current state and what's next

Working end to end: tests run and the Web build is produced and cached in CI;
the game runs on a 3-replica Deployment with its nginx configuration in a
ConfigMap; a Flask leaderboard API and a Postgres database run alongside it,
reachable through a single Ingress at `bird.local`; database credentials come
from a Secret and the rest of the configuration from a ConfigMap; scores survive
pod deletion on a PersistentVolume; and NetworkPolicies enforced by Calico mean
only pods labelled `role=api` can reach the database — verified, not assumed.

This is an active learning project. Everything below is a topic I'm working
through rather than a box to tick, and nothing here is claimed as done until it
is.

**Known shortcuts, declared rather than hidden**

- [ ] Schema creation runs at app startup and races across replicas. Moving to an
      initContainer with a Postgres advisory lock; the production answer is
      Alembic migrations from a Job.
- [ ] Run timing is client-authoritative and therefore trivially cheatable,
      bounded only by server-side sanity checks. The real fix is a server-issued
      run token.
- [ ] Postgres is a single pod with no backups — a Deployment, not a StatefulSet.
- [ ] Creating the database Secret is still a manual `kubectl create secret`,
      which is the one thing standing between this and a pure
      "redeploy from YAML alone".

**Finishing the Kubernetes track**

- [ ] Liveness and readiness probes on the game tier (the API already has them)
- [ ] Resource requests and limits on every container
- [ ] Wire the Unity client to the API: run timer, player name, `UnityWebRequest`
- [ ] Prometheus and Grafana, with an alert on restart count
- [ ] Horizontal Pod Autoscaler
- [ ] Package the manifests as a Helm chart with per-environment values
- [ ] Push images to a registry from CI and deploy on merge

**Extending to Terraform and AWS**

The scope has deliberately grown. A deployment story that only ever runs on a
laptop is an incomplete one — minikube hides the parts that matter most in
production, and some things simply don't exist there. Finding out what
actually happens when a cloud provider is on the other end is the point.

- [ ] `terraform/` as a sibling to `docker/` and `k8s/`, same one-folder-one-concern split
- [ ] Build the VPC by hand once — subnets, routing, security groups — then replace it with Terraform
- [ ] Serve the Web build from S3 behind CloudFront as a second deployment path
- [ ] Manage it all through Terraform: variables, outputs, remote state, and a clean `destroy`

For a static Web build, S3 and CloudFront is the better production answer:
cheaper, faster, and far less to operate than a Kubernetes cluster. The
Kubernetes path exists here because learning it was the original goal, and
because the orchestration concepts are what transfer to real workloads. Building
both is deliberate — being able to explain why they differ, and what each costs,
is more useful than having only one of them.

## Running it locally

Full instructions are in [`k8s/README.md`](k8s/README.md). The short version:

```bash
minikube start --cni=calico          # default CNI does NOT enforce NetworkPolicy
eval $(minikube docker-env)
docker build -f docker/Dockerfile -t bird-platformer:v2.2 .
docker build -f api/Dockerfile -t bird-leaderboard:v1 ./api

kubectl apply -f k8s/namespace.yaml
kubectl config set-context --current --namespace=bird-platformer

kubectl create secret generic leaderboard-db \
  --from-literal=DB_USER=birduser \
  --from-literal=DB_PASSWORD="$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | cut -c1-24)"

minikube addons enable ingress
sudo sh -c 'echo "127.0.0.1  bird.local" >> /etc/hosts'
kubectl apply -f k8s/
minikube tunnel # new terminal

open http://bird.local
curl -s "http://bird.local/api/scores/top?level_id=Level01&limit=10"
```

Note that `web/game/` is gitignored, so a fresh clone needs a Unity Web build
(or the `web-build` artifact from a CI run) before the image will build. You can grab this artifact from CI and drop the contents into `web/game/` at the root of the cloned repository.