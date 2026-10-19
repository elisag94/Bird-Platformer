# Week 3 Day 6 — Helm, three environments (19 Oct 2026)

**Mental model:** manifests = a form filled in by hand, in pen. **Chart** = the blank
form with `{{ }}` blanks. **Values file** = the answers. **Release** = one filled-in
copy running in the cluster (`bird-dev`, `bird-uat`, `bird-prod`).

Chart: `helm/bird-platformer/`. Values: `values.yaml` (dev-safe defaults) +
`values-uat.yaml` / `values-prod.yaml` (deltas only, layered on top).

## What differs per environment — and what deliberately doesn't

| Value | dev | uat | prod |
|---|---|---|---|
| HPA | off, 1 replica | on, min 2 | on, min 3 |
| Resources | 32Mi/128Mi | 32Mi/128Mi | 64Mi/256Mi |
| Leaderboard replicas / log level | 1 / DEBUG | 2 / INFO | 3 / WARN |
| Postgres PVC | 1Gi | 2Gi | 5Gi |
| Hostname | dev.bird.local | uat.bird.local | bird.local |
| Rollout strategy | maxSurge 0 (leave first) | maxSurge 1 (arrive first) | maxSurge 1 |
| `environment` (badge) | dev | uat | prod |

**Not parameterised:** probes, NetworkPolicies, ports, schema. Properties of the
*app*, not the environment. If it differed, UAT would stop testing prod.

**Secrets stay outside the chart:** `kubectl create secret` per namespace, a random
password each, none in Git. A leaked dev credential can't reach prod.

`helm template` diff dev vs prod was short: log level, storage, replicas
(game's *removed* — HPA owns it), resources, one HPA block, hostname.

## Symptom → cause → fix

| # | Symptom | Cause | Fix |
|---|---|---|---|
| 1 | `helm create helm/bird-platformer` → `stat .../helm: no such file` | `helm create` won't make parent dirs | `mkdir -p helm` first |
| 2 | Leaderboard still `replicas: 2` in render | One edit never landed in the file | Read the render, not the editor. `helm template \| grep` is the truth |
| 3 | `TLS handshake timeout`; `minikube status` hangs; `docker stats` empty | minikube container capped at **3.9 GiB** inside Docker's 7.7 GiB. `kubectl top` reported against the *host*, showing 41% while the container was full | `docker update --memory 6g --memory-swap 6g minikube`, delete the old `bird-platformer` namespace. Proper fix (`minikube config set memory`) needs a cluster delete — known gap |
| 4 | After restart, every pod `Error` | Stale status from before the stop. Calico (the road network) must come up first | Wait 2–3 min |
| 5 | v99 upgrade: Helm says `Upgrade complete`; pod `ImagePullBackOff`; prod still 200 | Helm checks the YAML was *accepted*, not that pods are healthy. Rolling update keeps old pods until new ones are Ready | `helm rollback bird-prod 1`. In CI: `--atomic` (Helm 3) / `--rollback-on-failure` (Helm 4) + `kubectl rollout status` |
| 6 | dev upgrade to v5: still serving v4, 404 on `/version.json` | `exceeded quota: limits.cpu used 900m + 200m > 1`. A surge pod doesn't fit dev's budget | Per-env `strategy`: dev `maxSurge: 0, maxUnavailable: 1` |
| 7 | (avoided) `helm upgrade` without `-f values-prod.yaml` | Helm doesn't remember previous answers — prod would silently become dev-shaped | Always pass the env's `-f` on upgrade. A pipeline makes this impossible to forget |
| 8 | `--set image.tag=v5` worked, but Git still said v4 | `--set` lives only in the cluster (`helm get values`) | Commit the tag. Day 7: pipeline records what each env runs |
| 9 | Rebuilt `bird-platformer:v5` after changing a COPY path — new digest, same tag | Mutable tag: "v5" now names two different sets of bytes | Never reuse a tag. Day 7: tag with commit SHA |

## Version vs. environment (the badge)

- **Version** is a fact about the box → baked in at build (`ARG APP_VERSION` →
  `version.json`).
- **Environment** is a fact about the shelf → mounted at runtime from a ConfigMap
  (`env.json`). Bake it in and you need one image per env — build-once breaks.
- Script injected by the Dockerfile, not hand-edited into `index.html`, because
  Unity's export overwrites `index.html`.
- `subPath` mount: slips one file into a folder instead of replacing the folder.
  Catch: subPath files never update in a running pod → `checksum/env` annotation
  forces a restart when the value changes.

Four version numbers: **image tag** (what's in the box), **appVersion** (a label
for it), **chart version** (the form's own version), **revision** (Helm's per-env
logbook counter).

## One fact, one home — nginx config

`docker/nginx/default.conf` and the inline ConfigMap copy were two homes for one
fact. Now: `helm/bird-platformer/files/nginx-default.conf` is the only copy. The
Dockerfile COPYs it; the chart inlines it with `.Files.Get`. `.Files.Get` can only
read inside the chart folder, which decided where the home lives.
`checksum/config` on the pod template → editing the config rolls the pods; without
it nginx never re-reads the file.

## Self-check answers

- **Release / where is state?** One installed copy of a chart. State is stored as
  Secrets in the release's namespace (`sh.helm.release.v1.<name>.v<N>`) — which is
  why releases survived the minikube restart.
- **`helm template` vs `install`?** Template renders locally, touches nothing.
- **Why no `namespace:` in templates?** A form with the address pre-printed can
  only be mailed to one place. (My manifests never had it — I always used `-n`.)
- **Rollback vs `kubectl rollout undo`?** Helm rolls back the whole release
  (ConfigMaps, Services, HPA), and adds a *new* revision ("Rollback to 1") rather
  than erasing history.
- **Three names, one IP — what decides?** `/etc/hosts` answers "which building"
  (all → 127.0.0.1). The ingress controller reads the `Host:` header for "which
  floor". Proven with `curl -H "Host: dev.bird.local" http://127.0.0.1/...`:
  dev's leaderboard had a score, prod's was empty — separate databases.
