# Bird Platformer

**This is a Kubernetes and CI/CD learning project.** The Unity game is not the point, in fact I used AI heavily to get the game playable. I'm not a game developer and I'm not trying to become one (too complicated for me!!). The Unity work
stopped once the game was substantial enough to be a realistic deployment
target.

I wanted to learn Kubernetes properly, and I didn't want to do it against a
generic "hello world" container. Since I had started leaning Unity earlier in the year, where
I wanted to build a small 2D platformer, I decided to leverage that to build a real-world deployment pipeline. The interesting work is everything downstream of it: the build
pipeline, the container, and the manifests.

## What actually runs, but still a WIP

```
Unity project ──► GitHub Actions ──► WebGL build ──► nginx container ──► Kubernetes
   (Assets/)        (tests, then          (web/game/)      (docker/)         (k8s/)
                     the build)
```

| Path | What it is |
|---|---|
| `.github/workflows/ci.yml` | Runs Unity EditMode + PlayMode tests, then the WebGL build. Caches the Unity `Library/` folder so runs don't recompile every asset from scratch, and renders test results onto the run summary page. |
| `docker/` | How the image is assembled; `nginx:alpine` serving the build, plus a `/healthz` endpoint that answers without touching a real page. |
| `k8s/` | How it's deployed; a 3-replica Deployment and a NodePort Service, running on minikube. See [`k8s/README.md`](k8s/README.md) for the full walkthrough. |
| `web/` | What gets served. `web/game/` is the build output and is gitignored, so it isn't present after a fresh clone. |

The three-way split between `docker/`, `k8s/`, and `web/` is deliberate:
assembly, deployment, and content stay separate, so nothing in `web/` can leak
into server configuration by accident.

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
- **Unity's build output path moves between GameCI versions**, so the workflow
  locates `index.html` and works back from it rather than hardcoding a path.
- **Platform naming drift**: Unity 6 calls the target "Web" in its UI, but the
  underlying `BuildTarget` enum is still `WebGL`, which is what GameCI expects.

## Current state and what's next

Working end to end: tests run, the WebGL build is produced and cached in CI and the Deployment and Service run on minikube with self-healing
verified.

Deliberately not done yet — this is an active learning project, and each of
these is a topic I'm working through rather than a box to tick:

- [ ] Liveness and readiness probes wired to the existing `/healthz` endpoint
- [ ] Resource requests and limits on the container
- [ ] Package the manifests as a Helm chart with per-environment values
- [ ] Ingress instead of NodePort, so the tunnel workaround goes away
- [ ] Push the image to a registry from CI and deploy on merge
- [ ] ConfigMaps for the nginx configuration rather than baking it into the image

## Running it locally

Full instructions are in [`k8s/README.md`](k8s/README.md). The short version:

```bash
minikube start
eval $(minikube docker-env)
docker build -f docker/Dockerfile -t bird-platformer:v1 .
kubectl apply -f k8s/
minikube service bird-platformer-service
```

Note that `web/game/` is gitignored, so a fresh clone needs a Unity Web build
(or the `web-build` artifact from a CI run) before the image will build.