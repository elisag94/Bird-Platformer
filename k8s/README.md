# Bird Platformer — Kubernetes Deployment

Manifests and instructions for deploying Bird Platformer as a multi-tier
application: a static game tier, a leaderboard API, and a Postgres database,
all behind a single hostname.

## What's deployed

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
   [ nginx pods x3 ]         [ flask pods x2 ]
     ConfigMap: nginx conf     ConfigMap: DB host/port/limits
                               Secret:    DB user/password
                                   |
                                   |  host "postgres" — resolves only in-cluster
                                   v
                          [ postgres-service ] -> [ postgres pod ] -> [ PVC 1Gi ]

   NetworkPolicy: only pods labelled role=api may reach the database.
   The game pods are proven to have no route to it.
```

Everything runs in the `bird-platformer` namespace. Postgres has no Ingress
rule and no NodePort — it is reachable only from inside the cluster, by DNS
name.

## Prerequisites

* **kubectl** (v1.26+)
* **minikube** — and it must be started with a CNI that enforces NetworkPolicy
  (see below). Other clusters work, but the tunnel and addon steps differ.
* **Docker**
* A Unity Web build in `web/game/` — gitignored, so not present after a fresh
  clone. Build it from Unity (File → Build Profiles → Web) with the output
  folder set to `web/game/`, or download the `web-build` artifact from a CI run
  and unpack it there.

## Repository structure

```text
Bird-Platformer/
├── Assets/                     # Unity project source
├── api/                        # the leaderboard service
│   ├── app.py                  # Flask routes
│   ├── models.py               # SQLAlchemy Player, Score
│   ├── db.py                   # engine/session, reads env vars
│   ├── requirements.txt
│   └── Dockerfile
├── docker/                     # how the game image is assembled
│   ├── Dockerfile              # nginx image serving the Web build
│   └── nginx/default.conf      # server config, incl. /healthz
├── k8s/                        # how it's deployed
│   ├── namespace.yaml
│   ├── deployment.yaml                 # game: 3 replicas of nginx
│   ├── service.yaml                    # game: ClusterIP
│   ├── configmap.yaml                  # game: nginx conf, cache headers, /healthz
│   ├── leaderboard-deployment.yaml     # API: 2 replicas of Flask/gunicorn
│   ├── leaderboard-service.yaml        # API: ClusterIP, 80 → 8080
│   ├── leaderboard-configmap.yaml      # API: DB host/port/name, validation bounds
│   ├── postgres-deployment.yaml        # database, strategy: Recreate
│   ├── postgres-service.yaml           # database: ClusterIP, no external route
│   ├── postgres-pvc.yaml               # 1Gi, ReadWriteOnce
│   ├── ingress.yaml                    # / → game, /api → leaderboard
│   ├── networkpolicy-default-deny.yaml
│   ├── networkpolicy-allow-ingress-controller.yaml
│   ├── networkpolicy-allow-api-to-postgres.yaml
│   ├── notes/                          # symptom → cause → fix log
│   └── README.md
└── web/
    ├── coming-soon/index.html  # placeholder (superseded by the game)
    └── game/                   # Unity Web build output — gitignored
```

The manifests deliberately carry **no `namespace:` field**. They are applied
with `-n` instead, so the same YAML could stand up a second environment. The
trade-off is that a careless `kubectl apply` lands in `default` — hence setting
the context namespace in step 3.

## Deployment

### 1. Start a cluster that enforces NetworkPolicy

```bash
minikube start --cni=calico
kubectl get pods -n kube-system | grep -Ei 'calico'    # wait for Running
```

**This matters more than it looks.** minikube's default CNI stores
NetworkPolicy objects and enforces none of them. An unenforced policy is
indistinguishable from an enforced one until traffic you believed was blocked
gets through. Always check before trusting a policy.

### 2. Build both images inside minikube's Docker daemon

```bash
eval $(minikube docker-env)

docker build -f docker/Dockerfile -t bird-platformer:v1 .
docker build -f api/Dockerfile -t bird-leaderboard:v1 ./api

docker images | grep -E 'bird-platformer|bird-leaderboard'
```

Note the two different build contexts. The game's Dockerfile copies from
`web/game/` **and** `docker/nginx/`, so its context must be the repo root — the
trailing `.`. The API's Dockerfile only ever copies from `api/`, so a narrower
context is correct there. **Context should be the smallest directory containing
everything you `COPY`.**

`eval $(minikube docker-env)` only affects the terminal you ran it in. Undo it
with `eval $(minikube docker-env -u)`.

Optional smoke test of the game image outside Kubernetes:

```bash
docker run --rm -d -p 8081:80 --name smoke bird-platformer:v1
curl -s -o /dev/null -w '%{http_code}\n' localhost:8081/healthz   # 200
docker stop smoke
```

### 3. Create the namespace and point kubectl at it

```bash
kubectl apply -f k8s/namespace.yaml
kubectl config set-context --current --namespace=bird-platformer
```

The namespace must exist before anything else is applied, and
`kubectl apply -f k8s/` processes files alphabetically — `namespace.yaml` would
come far too late. Apply it on its own first.

Set the context back to `default` when you're finished for the day.

### 4. Create the database Secret

Not committed, by design:

```bash
kubectl create secret generic leaderboard-db \
  --from-literal=DB_USER=birduser \
  --from-literal=DB_PASSWORD="$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | cut -c1-24)"
```

This is the one step that breaks "redeploy from YAML alone", and that is a real
trade-off rather than an oversight. A Secret's contents are base64-**encoded**,
not encrypted — committing one to a public repo is exactly as bad as committing
plaintext, just less obvious to a casual reader. Sealed Secrets and the External
Secrets Operator exist to close this gap.

The same Secret feeds both tiers: Postgres reads it as `POSTGRES_USER` /
`POSTGRES_PASSWORD` via `secretKeyRef` renaming, and Flask reads it as
`DB_USER` / `DB_PASSWORD` via `envFrom`.

### 5. Enable the Ingress controller

An Ingress resource is only a set of rules; a controller has to be running to
act on them.

```bash
minikube addons enable ingress
kubectl get pods -n ingress-nginx      # wait for the controller to be Running
```

### 6. Add the local hostname

`/etc/hosts` is a private address book your machine checks before DNS.
`bird.local` resolves only on this laptop.

```bash
sudo sh -c 'echo "127.0.0.1  bird.local" >> /etc/hosts'
```

### 7. Apply everything

```bash
kubectl apply -f k8s/
kubectl get pods -w
```

### 8. Open the tunnel (macOS, Docker driver)

The minikube node runs on a Docker-internal network macOS can't route to.
**Leave this terminal open** — closing it kills the tunnel, which looks
identical to the deployment being broken.

```bash
minikube tunnel
```

### 9. Verify

```bash
kubectl get pods,svc,ingress,pvc
kubectl describe ingress bird-platformer-ingress     # check the backend column
```

```bash
open http://bird.local

curl -s -X POST http://bird.local/api/scores \
  -H 'Content-Type: application/json' \
  -d '{"player_name":"elisa","level_id":"Level01","duration_ms":42310,"deaths":2}'; echo

curl -s "http://bird.local/api/scores/top?level_id=Level01&limit=10"; echo
```

## The API

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/healthz` | Liveness. Returns 200 unconditionally, never touches the DB. |
| `GET` | `/readyz` | Readiness. Runs `SELECT 1`; 503 if Postgres is unreachable. |
| `POST` | `/api/scores` | Submit a finished run. |
| `GET` | `/api/scores/top?level_id=&limit=` | Leaderboard, fastest first. |

Liveness and readiness are separate on purpose. If Postgres hiccups the API is
*alive* — restarting it will not fix a database — but *not ready*, so traffic
should stop. Pointing liveness at a DB check makes every replica fail at once
and Kubernetes restarts the whole deployment, turning one outage into two.

`/healthz` sits outside the `/api` prefix even though the Ingress only routes
`/api` here, because **health probes bypass both the Service and the Ingress** —
the kubelet dials the pod IP directly.

There is no `rewrite-target` annotation, so Flask receives the full path
`/api/scores` and its routes are declared with the prefix included.

## Proving the interesting bits

**Persistence survives pod deletion:**

```bash
kubectl delete pod -l app=postgres
kubectl get pods -w                                   # new pod, new name, new IP
curl -s "http://bird.local/api/scores/top?level_id=Level01"    # scores still there
```

**Only the API can reach the database:**

```bash
kubectl run probe --rm -i --restart=Never --image=busybox:1.28 -- \
  sh -c 'nc -w 3 postgres 5432 </dev/null && echo REACHABLE || echo BLOCKED'
# → BLOCKED

kubectl run probe-api --rm -i --restart=Never --image=busybox:1.28 --labels=role=api -- \
  sh -c 'nc -w 3 postgres 5432 </dev/null && echo REACHABLE || echo BLOCKED'
# → REACHABLE
```

Same image, same namespace, same command — one label is the entire difference.

## Debugging a 503

In this order, every time. The first command usually ends it.

```bash
kubectl describe ingress <name>     # backend column shows an error? → wrong/missing Service
kubectl get endpoints <service>     # empty? → selector matches nothing, or readiness failing
kubectl describe pod <name>         # why is it not ready?
```

To separate routing problems from application problems, skip the Ingress
entirely:

```bash
kubectl port-forward svc/leaderboard-service 8080:80
curl -s localhost:8080/readyz
```

If port-forward works and `bird.local` doesn't, the app is fine and the problem
is routing.

Related, and worth recognising on sight:

* **404 with an HTML body / `Server: nginx`** — no Ingress rule matched; the
  request never reached Flask.
* **404 with a JSON body** — routing worked; Flask genuinely has no such route.
* **A hang followed by failure, rather than an instant connection refused** — a
  NetworkPolicy is dropping packets. There is nothing on the far end to send a
  refusal.

## Known shortcuts

Declared rather than hidden:

* **Schema is created by `create_all()` at app startup.** With 2 replicas × 2
  gunicorn workers, four processes race on first boot and the losers crash with
  `UniqueViolation`; it self-heals on retry. Fix in progress: an initContainer
  running a standalone init script guarded by a Postgres advisory lock. The
  production answer is Alembic migrations from a Job or a Helm pre-install hook.
* **Client-authoritative timing.** Unity measures the run and the server trusts
  it, bounded by `MIN_RUN_MS` / `MAX_RUN_MS`. A server-issued run token is the
  real fix.
* **Single Postgres pod, no backups** — a Deployment rather than a StatefulSet.
* **No TLS.** `bird.local` is HTTP only.

## Cleanup

```bash
kubectl delete -f k8s/
kubectl delete secret leaderboard-db
kubectl config set-context --current --namespace=default
```

Deleting the namespace removes everything in it, including the PVC and its data:

```bash
kubectl delete namespace bird-platformer
```