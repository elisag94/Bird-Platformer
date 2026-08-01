# Bird Platformer App Kubernetes Deployment

This repository contains the Kubernetes manifests and instructions to deploy the Bird Platformer.

## Prerequisites

Before deploying, ensure you have the following installed and configured:
* **kubectl** (v1.26+)
* **Access to a K8s cluster [minikube was used as the local setup]** (Minikube, Kind, or cloud provider)
* **Docker** (if building images locally)

## Repository Structure

```text
Bird-Platformer/
├── Assets/                     # Unity project source
├── Packages/
├── ProjectSettings/
├── docker/                     # how the container is assembled
│   ├── Dockerfile              # nginx image serving the Web build
│   └── nginx/
│       └── default.conf        # server config, incl. /healthz for probes
├── k8s/                        # how it's deployed
│   ├── deployment.yaml         # Application deployment specification
│   ├── service.yaml            # NodePort service for internal routing
│   └── README.md
├── web/                        # what gets served
│   ├── coming-soon/
│   │   └── index.html          # Placeholder site (superseded by the game)
│   └── game/                   # Unity Web build output — gitignored
├── .github/
│   └── workflows/ci.yml        # tests → Web build → image build & push
├── .gitignore
└── .dockerignore
```

The three-way split is deliberate: `docker/` is how the image is assembled,
`k8s/` is how it's deployed, and `web/` is what actually gets served. Nothing
in `web/` is server configuration, so nothing there can leak into the served
site by accident.

## Deployment Steps

1. **Clone the repository:**
   ```bash
   git clone https://github.com/elisag94/Bird-Platformer.git
   cd Bird-Platformer
   ```

2. **Produce the Unity Web build:**
   The image serves `web/game/`, which is gitignored and not present after a
   fresh clone. Build it from Unity (File → Build Profiles → Web) with the
   output folder set to `web/game/`.

3. **Build the Docker image (local):**
   Use a new tag for each change so Kubernetes detects the update:
   ```bash
   minikube start
   eval $(minikube docker-env)
   docker build -f docker/Dockerfile -t bird-platformer:v1 .
   ```
   Note the trailing `.` — the build context stays at the repo root even
   though the Dockerfile lives in `docker/`, because the image needs
   `web/game/` from the root.

   Sanity-check it outside Kubernetes first:
   ```bash
   docker run --rm -p 8080:80 bird-platformer:v1
   # http://localhost:8080          → the game
   # http://localhost:8080/healthz  → "ok"
   ```

4. **Apply the manifests:**
   Apply all files in the `k8s` directory to your cluster:
   ```bash
   kubectl apply -f k8s/
   ```

5. **Verify the deployment:**
   Check the status of the pods and services:
   ```bash
   kubectl get pods -l app=bird-platformer
   kubectl get svc bird-platformer-service
   ```

6. **View the App:**
   To actually view the app from your machine, use a separate terminal to open the NodePort service
   ```bash
   minikube service bird-platformer-service
   ```

## Cleanup

To remove all deployed resources from your cluster, run:
```bash
kubectl delete -f k8s/
```