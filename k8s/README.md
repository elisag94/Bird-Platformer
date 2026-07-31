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
├── Assets/
├── Packages/
├── ProjectSettings/
├── k8s/
│   ├── deployment.yaml   # Application deployment specification
│   ├── service.yaml      # NodePort service for internal routing
|   └── README.md  
├── web/
│   └── coming-soon/
│       └── index.html    # Placeholder site
├── .github/
│   └── workflows/
├── .gitignore
├── .dockerignore
└── Dockerfile
```

## Deployment Steps

1. **Clone the repository:**
   ```bash
   git clone https://github.com/elisag94/Bird-Platformer.git
   cd Bird-Platformer
   ```

2. **Build the Docker image (local):**
   Use a new tag for each change so Kubernetes detects the update:
   ```bash
   minikube start
   eval $(minikube docker-env)
   docker build -t bird-platformer:coming-soon .
   ```

3. **Apply the manifests:**
   Apply all files in the `k8s` directory to your cluster:
   ```bash
   kubectl apply -f k8s/
   ```

4. **Verify the deployment:**
   Check the status of the pods and services:
   ```bash
   kubectl get pods -l app=bird-platformer
   kubectl get svc bird-platformer-service
   ```

5. **View the App:**
   To actually view the app from your machine, use a separate terminal to open the NodePort service
   ```bash
   minikube service bird-platformer-service
   ```

## Cleanup

To remove all deployed resources from your cluster, run:
```bash
kubectl delete -f k8s/
```