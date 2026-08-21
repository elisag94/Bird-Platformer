# Plan revision — Environments (dev / UAT / prod) across Weeks 3 and 4
 
**Revised:** 17 August 2026 — Week 3 Day 1 complete (probes on both tiers, Postgres
outage drill, liveness-404 crashloop). Week 2's `init_db`/initContainer warm-up, Name
Files As Hashes, and leaderboard probe/resource tuning were closed 14 Aug.
**Authoritative for Weeks 3–4.** The main plan points here rather than repeating it.
**Replaces:** WEEK 3 Days 1–7 and WEEK 4 Days 1–7 in `kubernetes-devops-interview-prep-plan.md`
**Added time cost:** roughly +2 h across Week 3, +2 h across Week 4. Nothing is removed; two things move.
 
---
 
## PART 0 — The design decision, fleshed out
 
### The analogy to hold on to
 
Environments are a **theatre**.
 
- **dev** is the rehearsal room. Bare walls, half the cast, nobody watching. You stop and start whenever you like.
- **UAT** is the dress rehearsal on the real stage, with an invited audience. Same script, same set, same lighting cues. The whole point is that nothing differs from opening night except who is in the seats.
- **prod** is opening night.
The rule that falls out of the analogy, and the one thing to say in an interview:
 
> **You do not rewrite the play between the dress rehearsal and opening night. If you do, the dress rehearsal proved nothing.**
 
That is *build once, deploy many*. One artifact — one image, one WebGL build, tagged with one commit SHA — moves untouched through all three. What changes between environments is **who's watching, how many seats, and what it costs to get it wrong**. Never the script.
 
### The three questions an "environment" actually answers
 
| Question | Mechanism | Where it lives in the plan |
|---|---|---|
| **1. Configuration** — same artifact, different behaviour | Helm values files (K8s), `.tfvars` (AWS) | Week 3 Day 6 |
| **2. Promotion** — how a change moves up, and who approves | Pipeline stages + GitHub Environments | Week 3 Day 7 → Week 4 Day 6 |
| **3. Isolation** — what stops dev breaking prod | Namespaces (weak) → separate cloud resources → separate accounts (strong) | Week 4 Days 1, 4, 5 |
 
Most people conflate all three and end up with "environment" meaning "a folder of copy-pasted YAML." Keeping them separate is the whole insight.
 
### The isolation ladder — memorise this, it's a direct interview answer
 
From weakest to strongest:
 
```
  4. Separate AWS accounts     ← hard billing, quota and blast-radius boundary. The real answer.
  3. Separate clusters         ← real compute isolation, shared cloud account
  2. Separate namespaces       ← ORGANISATIONAL only. Shared control plane, nodes, CNI.
  1. Name prefixes             ← no isolation at all. bird-dev-web next to bird-prod-web.
```
 
**You will be on rung 2 in Week 3 and a hybrid of 2 and 3 in Week 4** (dev on minikube, UAT and prod as separate AWS resource sets in one account). Rung 4 is what a real company does, using AWS Organizations, and you should be able to say *why*: a compromised or runaway workload in one account cannot spend, delete, or reach anything in another, and service quotas are per-account so dev cannot exhaust prod's limits.
 
Why you're not doing rung 4 here: three AWS accounts is a day of Organizations setup that teaches you org admin, not infrastructure. Single account with strict IAM boundaries and naming discipline teaches the same *reasoning* and lets you finish the week.
 
**The honest line for interviews:**
> "Locally I ran dev, UAT and prod as three namespaces — which is a rehearsal rig, not isolation, because they share a control plane and a node. On AWS I split them into separate buckets, distributions and VPCs with per-environment IAM roles. If it were real money I'd use separate accounts under an Organization, because that's the only boundary that's also a billing and quota boundary."
 
### What differs between environments — and what must not
 
**Must differ:**
 
| Setting | dev | UAT | prod |
|---|---|---|---|
| Replica count | 1 | 2 | 3 |
| Resource requests/limits | tiny | realistic | realistic |
| HPA enabled | no | yes | yes |
| Log level | `DEBUG` | `INFO` | `WARN` |
| Hostname | `dev.bird.local` | UAT CloudFront URL | prod CloudFront URL |
| CDN cache TTL | n/a | short (60 s) — you want to see changes | long (1 year on hashed assets) |
| Database | ephemeral, small PVC | small PVC | PVC + a backup story |
| Alerts | fire silently | fire to a log | would page someone |
| Who can deploy | you, freely | pipeline, automatically | pipeline, **after approval** |
 
**Must NOT differ — this is the part that matters:**
 
- **The image.** Same SHA tag in all three. If prod builds its own image from a `prod` branch, UAT tested a different binary and you have learned nothing.
- **The chart / templates.** One set of templates, three values files. If prod has a hand-edited manifest, your "environments" are three unrelated deployments wearing matching t-shirts.
- **The deploy mechanism.** The command that ships to prod must be the command you've already run a hundred times against dev.
- **Health check endpoints and probe configuration.** If prod's probes differ from UAT's, UAT is not testing prod's failure behaviour.
**And the rule about secrets:** a values file describes *shape and scale*, never *credentials*. `values-prod.yaml` goes in Git. The prod database password does not. It comes from a Kubernetes `Secret` created out-of-band (Week 3) or AWS Secrets Manager (Week 4+). If you can't commit a file safely, it's the wrong kind of file.
 
### The promotion pipeline you're building toward
 
```
   git push  (feature branch)
       │
       ▼
   ┌─────────────────────────────────────────┐
   │  BUILD ONCE                             │
   │  Unity tests → WebGL export →           │
   │  docker build -t bird:<sha>             │
   │  push to ghcr.io                        │   ← the artifact. Never rebuilt again.
   └────────────────┬────────────────────────┘
                    │
       ┌────────────┴────────────┐
       │  merge to main          │
       ▼                         │
   ┌────────┐                    │
   │  dev   │  automatic         │   "does it start?"
   │ (local)│  no gate           │
   └───┬────┘                    │
       │                         │
       ▼                         │
   ┌────────┐                    │
   │  UAT   │  automatic on main │   "does it behave like prod?"
   │ (AWS)  │  no gate           │
   └───┬────┘                    │
       │                         │
       │  git tag v1.2.0   ──────┘
       │  + manual approval
       ▼
   ┌────────┐
   │  prod  │  gated
   │ (AWS)  │
   └────────┘
```
 
**Promotion is not a rebuild. Promotion is pointing a different environment at the same tag.** In Helm that is literally `--set image.tag=<sha>` against a different values file. That sentence is the interview answer.
 
**The anti-pattern to name aloud:** environment branches (`dev`, `uat`, `prod` branches that each build their own image). It's common, it's tempting, and it guarantees the thing you tested is not the thing you shipped.
 
### Cost check for the AWS side
 
| Resource | Per env | Two envs | Notes |
|---|---|---|---|
| S3 bucket | free to create | free | storage of ~50 MB is pennies |
| CloudFront distribution | free to create | free | free tier: 1 TB out + 10 M requests/month |
| VPC, subnets, IGW, route tables, SGs | free | free | only NAT Gateway costs, and you never create one |
| EC2 `t3.micro` (Day 5 only) | free tier 750 h/mo | ~pennies for a few hours | terminate same day |
| Public IPv4 | $0.005/hr each | ~$0.02 for an afternoon | terminate same day |
 
**Net effect of adding a second AWS environment: effectively zero.** The only new cost is your patience — a CloudFront distribution takes ~15 minutes to deploy, and now you create two.
 
### Knock-on effects elsewhere in the plan
 
- **Week 5 Day 4** currently says "stand up a second environment — staging." That day now becomes *"prove the codebase is a template by standing up a **third** environment from the same code with a new `.tfvars` file, then destroy it."* Same lesson, stronger evidence, less work — because the multi-env structure already exists.
- **Week 5 Day 6 (portfolio)** gains a strong README line: *"one chart and one Terraform codebase, three environments, promotion gated on approval."*
- **Status table**: add row 17 — *Multi-environment (dev/UAT/prod) with gated promotion* — W3 D6–D7, W4 D6.
---
 
# WEEK 3 — Operate it, see it, ship it (revised)
 
**This is the last Kubernetes week.** Weeks 1–2 built the system; this week makes it behave like something a team could run, and hands you the three artefacts recruiters look at: a Grafana dashboard, a Helm chart, and a green pipeline with a promotion gate.
 
**Framing:** you've built the house. Now install the smoke alarms and the thermostat, then learn to build the same house three times from one set of blueprints.
 
**What's moved in this revision:**
- The compression story moves **Day 7 → Day 2** (Day 7 was overloaded, and compression is a measure-then-act exercise that pairs naturally with sizing memory limits).
- `init_db.py` + initContainer (the outstanding Week 2 item) is now **Day 1's warm-up**, because probes are meaningless while pods still crash on boot.
---
 
### Day 1 — Health checks (probes), both tiers
 
**STATUS: ✅ COMPLETE — 17 August 2026.** Game tier probes added and verified; the
Postgres-outage drill and the deliberate liveness-404 crashloop both run and captured.
Write-up in `k8s/notes/week3-day1-probes.md`.
 
**Closed the Week 2 leftover:**
 
```bash
# 1. api/init_db.py exists; remove the init_db() call from app.py's import path
# 2. add initContainers: to k8s/leaderboard-deployment.yaml, same image, same envFrom
# 3. rebuild as bird-leaderboard:v2 — BUMP THE TAG IN BOTH PLACES
kubectl -n bird-platformer get pods -w      # Init:0/1 → PodInitializing → Running, RESTARTS 0
kubectl -n bird-platformer scale deployment leaderboard --replicas=4
```
 
Done first because a probe cannot tell you anything useful about a pod that is still losing a startup race. Verified: `Init:0/1` → `PodInitializing` → `Running`, RESTARTS 0, and clean at 4 replicas.
 
**Mental model — three different questions, three different probes:**
 
| Probe | The question | What Kubernetes does if the answer is no |
|---|---|---|
| **Readiness** | "Are you ready for customers?" | Takes you off the guest list. Service stops sending traffic. **No restart.** |
| **Liveness** | "Are you still alive?" | Kills and restarts the container. |
| **Startup** | "Have you finished waking up?" | Holds the other two off until you're up, or gives up. |

**How long before a probe actually acts:**

```
time to act ≈ initialDelaySeconds + (failureThreshold − 1) × periodSeconds
```

The first check fires *at* `initialDelaySeconds`, not one period after it. Game tier
liveness (`delay=10, period=10, failure=3`) → killed at t=30s, confirmed against
container timestamps on 17 Aug. Readiness (`delay=2, period=5, failure=3`) → 12s.

**`failureThreshold` is a count, not a duration.** It only becomes a time when
multiplied by `periodSeconds`. Detection latency is the thing an interviewer is
probing for with "why did it take 90 seconds to notice?"

**The asymmetry that matters — `successThreshold` is 1 and cannot be changed for
readiness:**

| | to be marked failed | to be marked healthy |
|---|---|---|
| threshold | 3 consecutive failures | **1** success |

Slow to condemn, quick to forgive. A false failure is expensive — you pull a healthy
pod out of service, and if it hits every replica you've caused the outage yourself.
A false success is cheap — one request may land early, and the next check catches it.
Observed live: Postgres took ~25s to take the API unready, and the API came back
`1/1` on the very next watch line after Postgres hit ready.
The classic outage: someone points **liveness** at a slow endpoint. Under load it gets slower, liveness times out, Kubernetes restarts the pod, the remaining pods take more load, they get slower too. A cascading restart loop caused by the health check itself. Very common interview question — have the story ready.

**How a probe actually travels — and what it bypasses**

The kubelet is a process on the node. It dials the **pod's IP directly** on the
`containerPort`. It does not use the Service, the Ingress, or cluster DNS.

Three consequences, each of which came up on 17 Aug:

1. **Probe port = `containerPort`, never the Service port.** Editing a Service's
   `port:` cannot break a probe. The leaderboard Service does `80 → 8080` and the
   probe correctly says `8080`.
2. **The probe path needs no Ingress rule.** `/healthz` works even though the Ingress
   only routes `/api`.
3. **NetworkPolicy does not apply.** NetworkPolicy governs pod-to-pod traffic; the
   kubelet is not a pod. A default-deny policy cannot break your health checks.

`kubectl describe pod` prints `http-get http://:80/healthz` with a **blank host** —
that blank is the pod IP being filled in at runtime. The mental model is printed
right there in the output.

**Pass/fail rule:** HTTP 200–399 passes, 400+ fails. A 404 is a failure — which is
what the deliberate break exploits.

**You no longer need to invent this story — 14 Aug 2026, from an angle neither of us predicted.** A 48 MB wasm `docker build` inside minikube starved the node. Healthy pods answered probes in over 1 s (the default `timeoutSeconds`), liveness failed 3×, and the kubelet restarted containers that were never broken — just slow. Restarting added startup work to a node already struggling, which made it worse.
 
The logs pointed the wrong way, which is the best part:
 
```
[CRITICAL] WORKER TIMEOUT (pid:7)
[ERROR] Worker (pid:7) was sent SIGKILL! Perhaps out of memory?
```
 
It was **not** out of memory. Gunicorn prints that guess for any SIGKILL, including its own after a worker timeout. The authoritative evidence was in `describe pod`:
 
```
Last State:  Terminated
Reason:      Completed      ← a graceful shutdown, not a crash
Exit Code:   0
```
 
**`Exit Code 0` / `Completed` = killed by something else. `OOMKilled` / `137` = actually out of memory.** Read exit codes over log messages. `QoS Class: BestEffort` was the other half — with no requests set, the kubelet had no idea what the pod needed and squeezed it first.
 
Fix: `timeoutSeconds` 3 (readiness) / 5 (liveness), a slacker liveness period, and resource requests → `Burstable`. **An over-aggressive liveness probe converts a slowdown into an outage.**

**`Endpoints` is deprecated in v1.33+; use `EndpointSlice`.** The old object crammed
every pod IP for a Service into one record — fine for 3 pods, miserable for 3000,
since one pod's readiness flipping rewrote the whole object and pushed it to every
node. EndpointSlice pages the list into chunks of ~100.

```bash
kubectl get endpointslice -n bird-platformer \
  -l kubernetes.io/service-name=leaderboard-service -o yaml | grep -E "addresses:|ready:"
```

An unready pod's address **stays in the list, flagged `ready: false`** rather than
being removed. **kube-proxy only writes routing rules for entries marked ready** —
that flag is the actual mechanism by which "unready" becomes "receives no traffic."
This is the cleanest possible evidence for the Postgres drill.

**Do — the game tier:**
 
```yaml
# k8s/deployment.yaml, container spec:
        readinessProbe:
          httpGet: { path: /healthz, port: 80 }    # from Day 1's ConfigMap
          initialDelaySeconds: 2
          periodSeconds: 5
        livenessProbe:
          httpGet: { path: /healthz, port: 80 }
          initialDelaySeconds: 10
          periodSeconds: 10
          failureThreshold: 3
```
 
**Do — the leaderboard tier, where the split finally earns its keep:**
 
```yaml
# k8s/leaderboard-deployment.yaml — ✅ APPLIED 14 Aug 2026
# NOTE: port 8080, not 8000. gunicorn binds 8080; the Service does 80 -> 8080.
        readinessProbe:
          httpGet: { path: /readyz, port: 8080 }   # runs SELECT 1 — 503 if Postgres is down
          initialDelaySeconds: 3
          periodSeconds: 10
          timeoutSeconds: 3
          failureThreshold: 3
        livenessProbe:
          httpGet: { path: /healthz, port: 8080 }  # NEVER touches the DB
          initialDelaySeconds: 15
          periodSeconds: 20
          timeoutSeconds: 5
          failureThreshold: 3
 
# timeoutSeconds defaults to 1, which is far too tight — see the incident below.
# Liveness is deliberately SLACKER than readiness. Failing readiness costs a pod
# its traffic and is instantly reversible; failing liveness costs it its life.
```
 
```bash
kubectl apply -f k8s/
kubectl -n bird-platformer get pods -w        # watch READY go 0/1 → 1/1
 
# prove the split: kill Postgres and watch what happens to the API
kubectl -n bird-platformer scale deployment postgres --replicas=0
kubectl -n bird-platformer get pods -w        # API: READY 0/1, RESTARTS still 0 ← the whole lesson
kubectl -n bird-platformer scale deployment postgres --replicas=1
 
# now break it on purpose: point livenessProbe at /does-not-exist, apply
kubectl -n bird-platformer get pods -w        # RESTARTS climbs → CrashLoopBackOff
kubectl -n bird-platformer describe pod <name> | tail -20
```
 
That Postgres experiment is the money shot: **the API went unready without restarting**. Restarting it would not have fixed a database outage. Screenshot it.
 
**Environment note (new):** probe configuration is one of the things that must **not** differ between environments. If prod is more forgiving than UAT, UAT is not rehearsing prod's failure behaviour. Probes go in the chart's templates, not in the values files. Note that decision now, because on Day 6 you will be tempted to parameterise everything you can see.
 
**Visualize:** keep `kubectl get pods -w` in a split terminal for the whole exercise. RESTARTS ticking up in real time is the clearest picture of a bad probe.
 
**Self-check:**
1. Which probe failing takes a pod out of a Service without restarting it?
2. Why is pointing liveness at `/readyz` genuinely dangerous *in this stack*?
   → Both replicas share one Postgres. A DB blip fails liveness on **every replica
   simultaneously** — correlated failure destroys the independence replicas exist to
   provide. All restart, hit cold start, fail again, and enter exponential backoff
   (10→20→40→80→160→300s). Postgres recovers in 40s; the API is still in a 160s
   pause. A short DB blip becomes a long API outage caused by the health check.
   **General rule: liveness may only ask about things inside the container. If a
   restart can't fix it, liveness must not check it.**
3. Startup probe vs. a large `initialDelaySeconds`?
   → `initialDelaySeconds: 120` is a permanent tax: liveness waits two minutes on
   *every* restart forever, so a month-three deadlock goes unnoticed for two minutes.
   A startup probe holds liveness and readiness off *only until first success*, then
   retires and hands over to a tight liveness probe. **Tolerant during boot, strict
   during operation** — `initialDelaySeconds` can only give you one setting for both.
4. `READY 1/1` with `RESTARTS 7` — first hypothesis?
   → **Suspect the probe, not the app.** Readiness passing means the app is answering
   correctly right now. Candidates in order: liveness path wrong; `timeoutSeconds`
   too tight (the 14 Aug incident); liveness checking a flapping dependency.
5. `CrashLoopBackOff` — what is it actually telling you?
   → Not "Kubernetes gave up." It never gives up. It's the kubelet saying it is
   **deliberately pausing** before the next restart, with exponentially growing
   delays. You are looking at the pause, not the failure.
 
 **Incident — `Connection refused` from inside the nginx pod (17 Aug).**
`wget http://localhost/healthz` inside a game pod returned *connection refused*,
while the game served fine externally. `localhost` resolves to **two** addresses:
`127.0.0.1` (IPv4) and `::1` (IPv6). nginx's `listen 80;` binds IPv4 only —
`netstat -tln` showed `0.0.0.0:80` and no `:::80`. BusyBox wget tried IPv6 first.
Fix: dial `127.0.0.1` explicitly. Never an issue for the probe itself, since the
kubelet uses the pod's IPv4 address.

**Read the failure mode, not just the failure:**

| Result | Meaning |
|---|---|
| `Connection refused` | You reached the machine; nothing was listening. **An answer.** |
| `Connection timed out` | Nobody replied at all. **Silence** — usually a firewall or NetworkPolicy dropping packets. |

Confirmed the second one the same day: a bare `busybox` pod timed out against a game
pod, because it carried none of the labels the allow-policies match on, so
default-deny discarded it silently.

**Incident — liveness 404 crashloop, and why the exit code was 0 (17 Aug).**
Pointed `livenessProbe` at `/does-not-exist`. Result: `READY 1/1` alongside a
climbing `RESTARTS` — the pod was serving real traffic correctly while being killed
every 30 seconds, because readiness still pointed at `/healthz` and was passing.
The two probes are independent judges who never consult each other.

Exit code was **0 / `Completed`**, not the expected 137. The kubelet sends `SIGTERM`
first and only escalates to `SIGKILL` after `terminationGracePeriodSeconds` (default
30s). nginx handles SIGTERM properly and exits cleanly, so it never reaches SIGKILL.
Only a container that ignores SIGTERM or shuts down slowly shows 137.

**This sharpens the 14 Aug note.** `Exit Code 0` proves it was not an OOM, but it does
**not** identify who killed the container — a liveness kill and an ordinary graceful
shutdown look identical. The culprit is in the events:

```
Warning  Unhealthy  Liveness probe failed: HTTP probe failed with statuscode: 404
Normal   Killing    Container failed liveness probe, will be restarted
```

**Exit code tells you the manner of death; events tell you who pulled the trigger.**
Events expire after ~1 hour (`--event-ttl`) and the kubelet deduplicates repeats, so
they are for "what just happened," not "what happened yesterday."

---
 
### Day 2 — Resource requests, limits, quotas, and bytes on the wire
 
**Mental model:**
- **Request** = the seat you reserve. The scheduler uses it to decide which node you fit on. A *planning* number.
- **Limit** = the point the bouncer throws you out. Exceed memory → **OOMKilled**, instantly, no warning. Exceed CPU → **throttled**, slowed, not killed.
Memory is a cliff; CPU is a speed bump. That asymmetry is worth saying out loud in an interview.
 
**Status, 14 Aug 2026:** the **leaderboard tier already has requests and limits**
(`requests: 50m / 128Mi`, `limits: 500m / 256Mi`), added out of order as the fix for
the probe-induced crashloop on Day 1. Its QoS class is now `Burstable`, confirmed with:
 
```bash
kubectl get pod -l app=leaderboard -o jsonpath='{.items[0].status.qosClass}'
```
 
**The game tier is still `BestEffort`** — three nginx pods with no ceiling. That is the
work below. Those values were also set without measuring, so re-measure and correct
them rather than assuming they were right.
 
**Part 1 — measure, then set (~45 min):**
 
```bash
minikube addons enable metrics-server
kubectl top pods -n bird-platformer      # MEASURE FIRST. Never guess.
kubectl top nodes
```
 
Load the game a few times *while* `kubectl top pods` refreshes. nginx serving multi-MB WebGL assets has a completely different memory profile from a hello-world container.
 
**Why `BestEffort` bit for real:** with no requests set, the kubelet has no idea what a
pod needs, so it is the first thing squeezed when the node runs short — which is exactly
what happened when a `docker build` starved the node on 14 Aug. Requests are not only a
scheduling hint; they are what stops your workload being the sacrifice.
 
```yaml
        resources:
          requests: { memory: "32Mi", cpu: "50m" }
          limits:   { memory: "128Mi", cpu: "200m" }
```
 
```bash
# force an OOMKill: set limits.memory to 4Mi and apply
kubectl -n bird-platformer get pods                 # OOMKilled → CrashLoopBackOff
kubectl -n bird-platformer describe pod <name> | grep -A3 "Last State"
```
 
Exit code **137** = 128 + 9 = killed by SIGKILL. Recognising 137 on sight is a small, real signal of experience.
 
**Part 2 — ResourceQuota and LimitRange: what makes "dev" a real place (~45 min, NEW):**
 
*Analogy:* a **ResourceQuota** is the floor's total power budget — the whole floor may draw 2 kW, and once it's used, the next appliance simply doesn't get plugged in. A **LimitRange** is the house rule that says *any* appliance without a stated wattage is assumed to be 100 W, so nobody can sneak an unmetered space heater onto the floor.
 
This is what stops "dev" from being a sticker on the same infrastructure. A dev namespace with a real ceiling behaves differently from prod — which is both the point and the danger (see Day 4).
 
```bash
kubectl create namespace bird-dev
```
 
```yaml
# k8s/env/dev/resourcequota.yaml
apiVersion: v1
kind: ResourceQuota
metadata:
  name: dev-quota
  namespace: bird-dev
spec:
  hard:
    requests.cpu: "500m"
    requests.memory: 512Mi
    limits.cpu: "1"
    limits.memory: 1Gi
    pods: "6"
```
 
```yaml
# k8s/env/dev/limitrange.yaml
apiVersion: v1
kind: LimitRange
metadata:
  name: dev-defaults
  namespace: bird-dev
spec:
  limits:
  - type: Container
    default:        { memory: 128Mi, cpu: 200m }   # applied if you set no limit
    defaultRequest: { memory: 32Mi,  cpu: 50m }    # applied if you set no request
```
 
```bash
kubectl apply -f k8s/env/dev/
kubectl -n bird-dev describe resourcequota dev-quota
 
# now break it on purpose — try to deploy more than the floor's power budget
kubectl -n bird-dev create deployment fat --image=nginx --replicas=5
kubectl -n bird-dev get deployment fat        # DESIRED 5, AVAILABLE fewer
kubectl -n bird-dev describe replicaset -l app=fat | tail -20
```
 
**Read that error carefully.** The failure is `exceeded quota`, and — crucially — it comes from the **ReplicaSet**, not the pod, because the pods were never created at all. `kubectl get pods` shows nothing. A resource that doesn't exist has no events. Add this to `k8s/notes/` in symptom → cause → fix format; "deployment says 5, I see 2 pods, and there are no failing pods to describe" is a genuinely confusing first encounter.
 
**Part 3 — the compression story, moved here from Day 7 (~60 min):**
 
Same discipline as the memory numbers: measure, act, measure again.
 
- Re-enable Unity WebGL compression (Brotli or gzip) in Player Settings.
- ~~**Also turn on Name Files As Hashes** while you're in there~~ — ✅ **done 14 Aug 2026**, along with dropping `/TemplateData/` to `max-age=300` (hashing covers `/Build/` only). The `immutable` header is now honest.
- Add matching `Content-Encoding` headers in `k8s/configmap.yaml`. Without them the browser refuses the files outright.
- Measure transfer size before and after in devtools → Network.
```
Before: 48,224,945 bytes (re-measured 14 Aug 2026 after enabling Name Files As Hashes; the 48,112,154 figure predates it)
After:  ______________ bytes      ← fill this in; it goes in the README
```
 
*If the Unity rebuild fights you, defer this to Day 6 — you're rebuilding the image there anyway. Don't let it eat the day.*
 
**Visualize:** a two-column table in your notes — measured usage from `kubectl top` next to the numbers you set, and measured bytes before/after compression. That table answers "how did you size that?", which is the follow-up to everything above.
 
**Self-check:** does the scheduler use request or limit? What happens when a node's *requests* are fully booked but actual usage is 10%? What's a QoS class and which one do your pods land in? Why did the quota rejection produce no failing pod to inspect?
 
---
 
### Day 3 — Prometheus + Grafana (with a namespace variable)
 
**Biggest visual payoff of the week.** It also closes the observability gap job postings keep naming (they say Datadog; the concepts are identical, and Prometheus is the free one you can actually run).
 
**Mental model:**
- **Logs** are a diary — what happened, in order, in words.
- **Metrics** are a bathroom scale — one number, measured repeatedly, so you can see the *trend*.
- **Prometheus** is the scale plus the notebook: every 15 seconds it walks the cluster asking each pod "what are your numbers?" and writes them down. That walking-around is **scraping** — note the direction: Prometheus *pulls*, the pod does not push.
- **Grafana** draws the graph. It stores nothing; it asks Prometheus.
- An **alert rule** is a sticky note on the notebook: "if this number does that, wake someone."
```
   [ your pods ] --exposes /metrics--> [ Prometheus ] --queried by--> [ Grafana ]
                    every 15s              |                            (graphs)
                                           v
                                    [ Alertmanager ] --> "pod restarted 3x"
```
 
```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo update
helm install monitoring prometheus-community/kube-prometheus-stack \
  -n monitoring --create-namespace
 
kubectl get pods -n monitoring -w
kubectl port-forward -n monitoring svc/monitoring-grafana 3000:80
# http://localhost:3000 — admin / prom-operator
```
 
**Do:**
- Find *your* pods in the built-in dashboards. Filter by namespace `bird-platformer`.
- Compare real memory use against the Day 2 limits. Adjust if you guessed badly.
- Build one dashboard of your own: memory, CPU, restart count for the game deployment.
- Write **one** alert rule: fire if a pod restarts more than twice in five minutes.
- Break a liveness probe on purpose and watch it fire.
**Environment work (NEW, ~30 min) — one dashboard, three environments:**
 
Do **not** build a dashboard per environment. Build one with a **variable**.
 
```
Grafana → Dashboard settings → Variables → New variable
  Name:  namespace
  Type:  Query
  Query: label_values(kube_pod_info, namespace)
  Regex: /^bird-.*/          ← only your namespaces appear in the dropdown
```
 
Then change every panel query from a hardcoded namespace to the variable:
 
```promql
sum(container_memory_working_set_bytes{namespace="$namespace", pod=~"bird-platformer-game.*"}) by (pod)
```
 
Now one dashboard serves dev, UAT and prod, and a dropdown switches between them. **This is the same principle as a Helm values file, three days early** — one template, the varying bit pulled out into a parameter. Notice that, because on Day 6 it will make Helm click faster.
 
**Alerting differs per environment, and that's legitimate.** Not every environment deserves to wake someone. In a real setup the *rule* is identical and the **routing** differs — Alertmanager matches on the `namespace` or an `environment` label and sends prod to a pager, UAT to a chat channel, dev nowhere. Read the Alertmanager routing tree section; you don't need to wire a pager, you need to be able to say that sentence.
 
**Visualize:** screenshot your dashboard with the restart spike visible. Best single image for the repo README — it says "I don't just deploy things, I watch them."
 
**Self-check:** what does *scraping* mean and who initiates it? Why is a metric cheaper to store than a log line? Gauge vs counter? Why is one dashboard with a variable better than three dashboards, and what's the equivalent argument for Helm?
 
---
 
### Day 4 — Horizontal Pod Autoscaler
 
**Mental model:** the HPA is a thermostat. You set a target (CPU at 50%), it adds or removes pods to hold that number. Like a thermostat, it only works if there's a thermometer — `metrics-server`, which is why this comes after Day 2.
 
Crucially: **the HPA reads the *request*, not the limit.** "50% CPU" means 50% of what you *requested*. No request → no denominator → the HPA does nothing. That link between Day 2 and today is exactly what interviews probe.
 
```bash
kubectl -n bird-platformer autoscale deployment bird-platformer-game \
  --cpu-percent=50 --min=2 --max=10
kubectl -n bird-platformer get hpa -w
 
# load, in a second terminal
kubectl -n bird-platformer run load --rm -it --image=busybox:1.28 -- \
  sh -c "while true; do wget -q -O- http://bird-platformer-service; done"
 
kubectl -n bird-platformer get pods -w     # replicas climb
# stop the load, wait ~5 min, watch them scale back down slowly, on purpose
```
 
**Environment lesson (NEW) — the trap in "prod but smaller":**
 
Run the same load test against `bird-dev`, which has Day 2's quota capping it at 6 pods and 500m CPU.
 
```bash
kubectl -n bird-dev describe hpa            # or watch it simply stop climbing
kubectl -n bird-dev describe resourcequota dev-quota
```
 
The HPA wants more pods. The quota refuses. **Nothing errors loudly — the system just quietly stops scaling.**
 
This is the single most important environment lesson of the week:
 
> **An environment that is "prod but smaller" cannot test the behaviours that only appear at prod's size.** Autoscaling, connection-pool exhaustion, disk filling, rate limits — all invisible in a scaled-down environment, all waiting for you in prod.
 
So the decision becomes explicit rather than accidental: **HPA is `enabled: false` in dev's values file.** Not because autoscaling doesn't work there, but because a capped environment gives you a *false negative* — it looks fine and proves nothing. Write down which environment you actually trust to test which properties. That table is a genuinely senior artifact.
 
**Visualize:** three panes — `kubectl get hpa -w`, `kubectl get pods -w`, and your Grafana dashboard with the namespace dropdown. Watch load rise on the graph and pod count follow. Record 20 seconds of screen capture; it's the most convincing thing you can put in a portfolio.
 
**Self-check:** why is scale-down much slower than scale-up? What happens to the HPA if you remove CPU requests? Why can't the HPA save you from a slow database? Which properties can dev honestly test, and which can only UAT test?
 
---
 
### Day 5 — Break-things drill + RBAC, per environment
 
**Deliberate practice day.** Break six things on purpose and diagnose each from symptoms alone, no peeking at what you changed. Closest thing to a real interview troubleshooting round.
 
| # | Break | Symptom you should learn to recognise |
|---|---|---|
| 1 | Bad image tag | `ImagePullBackOff` / `ErrImagePull` |
| 2 | Missing ConfigMap key | Pod stuck `CreateContainerConfigError` |
| 3 | Service selector matching nothing | Service exists, `Endpoints: <none>`, connection refused |
| 4 | Memory limit far too low | `OOMKilled`, exit 137, CrashLoop |
| 5 | Liveness pointed at a 404 | Restart loop, healthy-looking logs |
| 6 | **Right command, wrong namespace** (NEW) | *Nothing happens.* No error. The change lands somewhere you aren't looking. |
 
The three commands, in this order, every time:
 
```bash
kubectl describe pod <name>        # events at the bottom — read these FIRST
kubectl logs <name> --previous     # --previous is the whole trick for crashed containers
kubectl get events --sort-by=.metadata.creationTimestamp
```
 
**Break #6 deserves its own paragraph, because it's the one multi-environment work introduces.**
 
```bash
kubectl config set-context --current --namespace=bird-dev
# ... now do some work, get distracted, come back an hour later ...
kubectl delete deployment bird-platformer-game     # which environment did that just hit?
```
 
There is no error message. The command succeeded — somewhere. **This is the most common self-inflicted production incident in the industry**, and the mitigations are habits, not YAML:
 
```bash
kubectl config current-context                     # the "where am I?" reflex
kubectl config view --minify | grep namespace
 
# make the state visible instead of remembered
brew install kubectx                               # provides kubectx and kubens
kubens                                             # lists namespaces, highlights current
kubens bird-dev                                    # switch
 
# best habit of all: be explicit. -n on every destructive command, always.
kubectl delete deployment bird-platformer-game -n bird-dev
```
 
Then add a prompt indicator (`kube-ps1`, or Starship's Kubernetes module) so your terminal *shows* which cluster and namespace you're pointed at. In interviews: **"I don't rely on remembering which context I'm in; I make it visible and I pass `-n` explicitly on anything destructive."**
 
**RBAC, now per environment (~60 min):**
 
*Mental model:* a **Role** is a job description ("may read pods on this floor"). A **RoleBinding** hands that job description to a specific person. A **ServiceAccount** is the badge a *pod* wears, since pods aren't people. Role/RoleBinding are namespaced; ClusterRole/ClusterRoleBinding are building-wide.
 
The environment application: **your everyday identity should be able to do less in prod than in dev.** That's not bureaucracy — it's the same reasoning as break #6. If the destructive command *cannot* work against prod, the habit failing doesn't cost you anything.
 
```bash
kubectl create serviceaccount deployer -n bird-dev
kubectl create serviceaccount deployer -n bird-prod
 
# dev: full control over workloads
kubectl create role dev-full -n bird-dev \
  --verb=* --resource=deployments,pods,services,configmaps
kubectl create rolebinding dev-full-binding -n bird-dev \
  --role=dev-full --serviceaccount=bird-dev:deployer
 
# prod: read-only
kubectl create role prod-readonly -n bird-prod \
  --verb=get,list,watch --resource=deployments,pods,services
kubectl create rolebinding prod-readonly-binding -n bird-prod \
  --role=prod-readonly --serviceaccount=bird-prod:deployer
 
# prove the boundary — this is the fun command
kubectl auth can-i delete deployments -n bird-dev \
  --as=system:serviceaccount:bird-dev:deployer          # yes
kubectl auth can-i delete deployments -n bird-prod \
  --as=system:serviceaccount:bird-prod:deployer         # no
kubectl auth can-i --list -n bird-prod \
  --as=system:serviceaccount:bird-prod:deployer         # the whole picture
```
 
**Remember this shape.** Week 4 Day 1 does the identical exercise in IAM — a role scoped to one environment's resources, then proving it's denied against the other. Same idea, different vocabulary. Noticing that Kubernetes RBAC and AWS IAM are the same pattern is worth more than memorising either one.
 
Tie-back: `automountServiceAccountToken: false` from Week 2 Day 2 exists precisely because every pod otherwise wears a badge it probably doesn't need.
 
---
 
### Day 6 — Helm, and the three environments *(the centrepiece — budget 3.5–4 h)*
 
**Mental model:** your manifests are a form filled in by hand, in pen. Want a second copy with one field changed? Rewrite the whole thing. A **chart** is the blank form with `{{ }}` where the variable bits go; a **values file** is the list of what to write in the blanks. One template, many deployments.
 
This is the clearest "I understand why raw YAML doesn't scale" signal you can put in a repo — and it's the day dev/UAT/prod stops being an idea and becomes three running copies.

**Task — collapse the nginx config duplication (~30 min, NEW).**

Found 17 Aug: `docker/nginx/default.conf` and the inline copy in `k8s/configmap.yaml`
are two homes for one fact, and had **already drifted** — the image's copy was missing
the `/Build/` immutable and `/TemplateData/` max-age blocks added on 14 Aug. It never
bit, only because the Deployment mounts the ConfigMap *over* `/etc/nginx/conf.d`, so
the ConfigMap always wins in-cluster. Dead code that looks authoritative is the
dangerous kind. Synced by hand on 17 Aug; this is the structural fix.

> **One fact, one home.** If a fact is written in two places, it isn't duplicated —
> one of them is already wrong and you don't know which.

Don't pick a winner; make the second copy impossible. Helm generates the ConfigMap
from the real file:

```yaml
# templates/configmap.yaml
data:
  default.conf: |
{{ .Files.Get "config/nginx-default.conf" | indent 4 }}
```

One file on disk. The Dockerfile `COPY`s it, Helm inlines it. Nothing to forget.

**Gotcha to test the same day: editing a ConfigMap does not restart your pods.** The
mounted file updates after a kubelet sync (up to ~60s) but nginx never re-reads it,
so the change silently doesn't apply. Fix with a checksum annotation on the pod
template:

```yaml
      annotations:
        checksum/config: {{ include (print $.Template.BasePath "/configmap.yaml") . | sha256sum }}
```

Config changes → annotation changes → pod template changes → rolling restart.
"I changed the ConfigMap and nothing happened" is a standard first-Helm confusion —
cause it on purpose, then fix it.

**Also fixed for free by templating:** the `leaderboard` (Deployment) vs
`leaderboard-service` (Service) naming inconsistency. Note that renaming a Service by
hand is *not* cosmetic — **a Service's name is its DNS name**, so it's an API contract
that `ingress.yaml` and any client resolve by. Renaming a Deployment is cosmetic.
Deliberately deferred to the chart rather than done by hand.

**Keep the comments.** The explanatory comments in the current manifests (especially
the `/Build/` immutable one — *"a cache header is a claim about your build process,
not a performance dial"*) are a portfolio asset. Templates get denser; move the
reasoning into `k8s/notes/` or the chart README rather than losing it.

**Step 1 — scaffold and gut it (~30 min):**
 
```bash
helm create bird-platformer          # read the generated scaffold, then delete most of it
rm -rf bird-platformer/templates/*   # keep Chart.yaml, values.yaml, templates/_helpers.tpl
```
 
**Step 2 — move your manifests in (~60 min):**
 
Your fifteen files map like this:
 
| Current file | Becomes | Notes |
|---|---|---|
| `namespace.yaml` | **deleted** | Helm's `-n <ns> --create-namespace` owns this now |
| `deployment.yaml` | `templates/game-deployment.yaml` | |
| `service.yaml` | `templates/game-service.yaml` | |
| `configmap.yaml` | `templates/game-configmap.yaml` | |
| `ingress.yaml` | `templates/ingress.yaml` | host becomes a value |
| `leaderboard-deployment.yaml` | `templates/leaderboard-deployment.yaml` | |
| `leaderboard-service.yaml` | `templates/leaderboard-service.yaml` | |
| `leaderboard-configmap.yaml` | `templates/leaderboard-configmap.yaml` | |
| `postgres-deployment.yaml` | `templates/postgres-deployment.yaml` | |
| `postgres-service.yaml` | `templates/postgres-service.yaml` | |
| `postgres-pvc.yaml` | `templates/postgres-pvc.yaml` | size becomes a value |
| `networkpolicy-*.yaml` (×3) | `templates/networkpolicy-*.yaml` | unchanged — same in every env |
| the Secret | **stays out of the chart** | created imperatively per namespace, see below |
 
**The single most important edit: delete every hardcoded `namespace:` line from `metadata:`.**
 
A manifest that names its own namespace cannot be installed anywhere else. That one line is the entire reason your current `k8s/` folder can only ever produce one environment. Let `helm -n` decide.
 
**Step 3 — parameterise exactly these, and resist doing more:**
 
```yaml
# values.yaml  (defaults = dev-safe, so a mistake fails small)
image:
  repository: bird-platformer
  tag: latest
  pullPolicy: IfNotPresent
replicaCount: 1
ingress:
  host: dev.bird.local
resources:
  requests: { memory: 32Mi, cpu: 50m }
  limits:   { memory: 128Mi, cpu: 200m }
hpa:
  enabled: false
  minReplicas: 2
  maxReplicas: 10
  targetCPUUtilizationPercentage: 50
leaderboard:
  logLevel: DEBUG
  replicaCount: 1
postgres:
  storageSize: 1Gi
```
 
```yaml
# values-uat.yaml — only the deltas
replicaCount: 2
ingress:  { host: uat.bird.local }
hpa:      { enabled: true }
leaderboard: { logLevel: INFO, replicaCount: 2 }
postgres: { storageSize: 2Gi }
```
 
```yaml
# values-prod.yaml — only the deltas
replicaCount: 3
ingress:  { host: bird.local }
hpa:      { enabled: true, minReplicas: 3, maxReplicas: 10 }
resources:
  requests: { memory: 64Mi, cpu: 100m }
  limits:   { memory: 256Mi, cpu: 500m }
leaderboard: { logLevel: WARN, replicaCount: 3 }
postgres: { storageSize: 5Gi }
```
 
Notice what's **not** in any values file: probe configuration, NetworkPolicies, the schema, the endpoints. Those are properties of the *application*, not the environment. If a value differs per environment, ask whether it's genuinely environmental or whether you're about to make UAT stop testing prod.
 
**Step 4 — the conditional, your one bit of template logic:**
 
```yaml
# templates/hpa.yaml
{{- if .Values.hpa.enabled }}
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: {{ .Release.Name }}-game
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: {{ .Release.Name }}-game
  minReplicas: {{ .Values.hpa.minReplicas }}
  maxReplicas: {{ .Values.hpa.maxReplicas }}
  metrics:
  - type: Resource
    resource:
      name: cpu
      target: { type: Utilization, averageUtilization: {{ .Values.hpa.targetCPUUtilizationPercentage }} }
{{- end }}
```
 
**Trap you will hit:** with `hpa.enabled: true`, both the HPA and the Deployment's `replicas` field want to own the replica count. They fight. The standard fix is to omit `replicas` from the Deployment template when the HPA is enabled — `{{- if not .Values.hpa.enabled }}replicas: {{ .Values.replicaCount }}{{- end }}`. Log it in `k8s/notes/`; it's a real interview question about who owns desired state.
 
**Step 5 — secrets, deliberately outside the chart:**
 
```bash
for ns in bird-dev bird-uat bird-prod; do
  kubectl create namespace $ns --dry-run=client -o yaml | kubectl apply -f -
  kubectl create secret generic leaderboard-db -n $ns \
    --from-literal=DB_USER=birduser \
    --from-literal=DB_PASSWORD="$(openssl rand -base64 24)"
done
```
 
**Three environments, three different passwords, none of them in Git.** If a dev credential leaks, prod is untouched — that's a real isolation benefit you get on rung 2 of the ladder. Say so in interviews; it shows you know what namespaces *do* buy you, not just what they don't.
 
**Step 6 — render before you install. Always:**
 
```bash
helm template bird-dev ./bird-platformer -f values.yaml > /tmp/dev.yaml
helm template bird-prod ./bird-platformer -f values-prod.yaml > /tmp/prod.yaml
diff /tmp/dev.yaml /tmp/prod.yaml
```
 
**That diff is the deliverable of the day.** It should be short — replica counts, a hostname, resource numbers, and the presence of an HPA. If it's long, you've parameterised something you shouldn't have. If it's empty, you've parameterised nothing. Screenshot it for the README.
 
**Step 7 — install all three:**
 
```bash
helm install bird-dev  ./bird-platformer -n bird-dev  -f values.yaml
helm install bird-uat  ./bird-platformer -n bird-uat  -f values-uat.yaml
helm install bird-prod ./bird-platformer -n bird-prod -f values-prod.yaml
 
helm list -A
kubectl get pods -A -l app=bird-platformer
```
 
**Step 8 — three hostnames, one door (the networking bit, explained slowly):**
 
```bash
minikube ip                                  # e.g. 192.168.49.2
sudo sh -c 'echo "192.168.49.2  dev.bird.local uat.bird.local" >> /etc/hosts'
# bird.local is already there from Week 2 Day 4
```
 
All three names point at **the same IP address**. Nothing about the network changed.
 
Here's the picture. Think of an office block with a single street entrance and one receptionist. Three companies rent floors in it. Every visitor walks through the same door — but the envelope in their hand is addressed to a company name, and the receptionist reads that name and walks the visitor to the right floor.
 
```
   dev.bird.local  ┐
   uat.bird.local  ├──▶  192.168.49.2  ──▶  [ ingress-nginx controller ]
   bird.local      ┘      ONE address            reads the Host: header
                                                        │
                        ┌───────────────────────────────┼───────────────────────────────┐
                        ▼                               ▼                               ▼
              Host: dev.bird.local            Host: uat.bird.local            Host: bird.local
                 namespace bird-dev              namespace bird-uat             namespace bird-prod
                 1 replica, DEBUG                2 replicas, INFO               3 replicas, WARN
```
 
The name in the envelope is the HTTP `Host:` header — a line of text the browser sends with every request, saying which site it *thinks* it's talking to. `/etc/hosts` only handles the first half of the trip ("what number is that address?"). The receptionist handles the second half ("which floor?"). Two completely separate steps that feel like one thing.
 
```bash
# prove the header is doing the work — same IP, different answer, DNS bypassed entirely
curl -H "Host: dev.bird.local"  http://192.168.49.2/
curl -H "Host: bird.local"      http://192.168.49.2/
```
 
If those return different things, you've just demonstrated **name-based virtual hosting**, and you understand something most people using it don't. If they return the *same* thing, your Ingress hosts aren't set from the values files — check `helm get manifest bird-uat -n bird-uat | grep host`.
 
**Step 9 — upgrade and roll back:**
 
```bash
helm upgrade bird-prod ./bird-platformer -n bird-prod -f values-prod.yaml --set image.tag=v3
helm history bird-prod -n bird-prod
helm rollback bird-prod 1 -n bird-prod        # compare to kubectl rollout undo
```
 
**Visualize:** the office-block diagram above, drawn from memory with your own hostnames. Then `helm template` diff'd against your original hand-written YAML — seeing the same file come out the other side of the template is what makes Helm click.
 
**Self-check:** what is a *release* and where does Helm store its state? What does `helm template` do that `helm install` doesn't? Why must the namespace come out of the templates? Which values did you deliberately *not* parameterise, and what would break if you had? Three names, one IP — what does the routing decision actually read?
 
---
 
### Day 7 — CI/CD end to end, with a promotion gate
 
**The pipeline:**
 
```
  git push
     │
     ▼
  [ Unity tests ]                      already in ci.yml
     │
     ▼
  [ WebGL export ]                     game-ci/unity-builder, targetPlatform: WebGL
     │
     ▼
  [ docker build -t bird:${{ github.sha }} ]        ← THE artifact. Built exactly once.
     │
     ▼
  [ push to ghcr.io/elisag94/bird-platformer:<sha> ]
     │
     ├──▶ [ deploy dev ]   automatic, no gate
     │
     ├──▶ [ deploy uat ]   automatic on merge to main
     │
     └──▶ [ deploy prod ]  requires approval  ────┐
                                                   │
                          GitHub Environments ─────┘
                          protection rule: required reviewer = you
```
 
**Do — the build (~90 min):**
- Extend `.github/workflows/ci.yml` with a WebGL build job (keep the pin at `6000.5.6f1`).
- Tag the image with `${{ github.sha }}` — **never `latest`**. `latest` is why "it works on my machine" survives into production; a SHA tag means every running pod traces to exactly one commit.
- Push to GitHub Container Registry (free for public repos).
**Do — the promotion (~60 min, NEW):**
 
GitHub **Environments** are the free, built-in mechanism for exactly this, and they're the piece that turns three deployments into a *pipeline*.
 
```
Repo → Settings → Environments → New environment
  Name: dev      no protection rules
  Name: uat      no protection rules
  Name: prod     ☑ Required reviewers → yourself
                 ☑ Deployment branches → main only
```
 
```yaml
# .github/workflows/ci.yml  (deploy jobs)
  deploy-uat:
    needs: build
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    environment: uat                      # ← binds the job to the environment
    steps:
      - run: |
          helm upgrade --install bird-uat ./bird-platformer \
            -n bird-uat -f values-uat.yaml \
            --set image.tag=${{ github.sha }}
 
  deploy-prod:
    needs: deploy-uat                     # ← prod can only follow a successful UAT
    runs-on: ubuntu-latest
    environment: prod                     # ← the job PAUSES here awaiting approval
    steps:
      - run: |
          helm upgrade --install bird-prod ./bird-platformer \
            -n bird-prod -f values-prod.yaml \
            --set image.tag=${{ github.sha }}
```
 
Push something and **watch the prod job sit there waiting for you**, with an Approve button. That pause is the entire concept made visible — screenshot it, it's the clearest possible evidence you understand gated deployment.
 
Look at the two deploy steps side by side. They differ in exactly three ways: release name, namespace, values file. **Same chart, same image tag, same command.** That's the whole discipline in ten lines of YAML.
 
**Be honest about the local cluster.** GitHub Actions cannot reach minikube on your laptop. Document the deploy step as the command you run locally, and note what would change with a cloud cluster — the workflow structure is identical, only the `kubeconfig` differs. Interviewers respect the accurate version far more than a faked one, and Week 4 makes the AWS half genuinely automated anyway.
 
**Deployment strategies — read + one small experiment (~30 min):**
- Rolling update (already done), blue-green, canary, and the trade-offs.
- Cheap canary: two Deployments with different image tags behind one Service; adjust replica counts to shift the ratio. 1 of 10 pods on the new build ≈ 10% canary. Crude, but it demonstrates the idea without a service mesh.
- Note the relationship: **environments separate in space, canaries separate in time.** UAT asks "does this work?" before anyone sees it; a canary asks "does this work?" while a few people already do. They're complements, not alternatives — and that's a good answer to "why do you need UAT if you have canaries?"
**Week 3 checkpoint — say this out loud:**
> "I deploy it, I watch it, I autoscale it, I package it as a chart, I run three environments from one template, and a push to main ships it to UAT and waits for my approval before prod."
 
**Kubernetes ends here.**
 
---
 
# WEEK 4 — Host it for real (AWS), in two environments
 
**Goal:** by Friday, Bird Platformer lives at **two** public URLs — a UAT one and a prod one — and a `git push` updates UAT automatically while prod waits for your approval. This is the week that closes the gap actually blocking your applications.
 
**Everything is free or costs pennies, including the second environment.** Two buckets and two CloudFront distributions sit comfortably inside the free tier. Read the account setup section in the original plan before creating the account (Free Plan, budget alert, teardown discipline, no NAT Gateway) — none of that changes.
 
**The environment model for this week:**
 
```
   dev   →  local minikube        (Week 3; stays local, costs nothing, fast loop)
   uat   →  AWS, auto-deployed    bird-platformer-uat  bucket + distribution + VPC
   prod  →  AWS, gated            bird-platformer-prod bucket + distribution + VPC
```
 
**Naming convention, decided now and never varied:** `bird-platformer-<env>-<resource>`. Boring, greppable, and — critically — it means an IAM policy can allow or deny an entire environment with one wildcard. Naming discipline *is* a security control; that's not a stretch, it's Day 1's exercise.
 
**Tagging convention, on every single resource:**
 
```
Project     = bird-platformer
Environment = uat | prod
ManagedBy   = manual        (becomes "terraform" in Week 5)
```
 
Tags are how you answer "what did UAT cost last month?" on Day 7 and "what's still running that shouldn't be?" on every teardown.
 
---
 
### Day 1 — Account, IAM, and per-environment boundaries
 
**Mental model:** IAM is the building's **security desk**.
- A **user** is a person with a permanent badge.
- A **role** is a *uniform hanging on a hook*. Anyone authorised can put it on temporarily; while wearing it they get exactly that uniform's access, and only for a while. Roles are how AWS prefers you work — nobody carries a permanent key, and a stolen key is worthless an hour later.
- A **policy** is the printed list of what a badge or uniform opens.
- The **root user** is the master key to the entire building. It lives in a drawer.
```bash
# after creating the account on the FREE PLAN and turning on MFA for root:
aws configure                    # region: ca-central-1 (Montreal)
aws sts get-caller-identity      # "who does AWS think I am?" — your first debugging tool
```
 
**Do — the baseline:**
- Account on the **Free Plan**. MFA on root immediately, then stop using root.
- IAM user for yourself. **$10 budget alert** (it's also a credit-earning onboarding activity, so it pays for itself).
**Do — the environment boundary (NEW, ~60 min). This is Week 3 Day 5's RBAC exercise in a different vocabulary:**
 
Create two roles, each scoped to exactly one environment's resources.
 
```json
// bird-platformer-uat-deployer — permissions policy
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:PutObject", "s3:GetObject", "s3:DeleteObject", "s3:ListBucket"],
      "Resource": [
        "arn:aws:s3:::bird-platformer-uat",
        "arn:aws:s3:::bird-platformer-uat/*"
      ]
    }
  ]
}
```
 
The prod role is the same file with `uat` swapped for `prod`. **That is the entire mechanism** — and it only works because you were disciplined about naming. A bucket called `my-test-bucket-2` cannot be covered by a wildcard.
 
```bash
# assume the uniform and watch your identity change
aws sts assume-role --role-arn <uat-role-arn> --role-session-name uat-test
# export the returned temporary credentials, then:
aws sts get-caller-identity                     # different answer — you're wearing the uniform
 
# now prove the boundary — the point of the whole exercise
aws s3 ls s3://bird-platformer-uat/             # works
aws s3 ls s3://bird-platformer-prod/            # AccessDenied
```
 
**That `AccessDenied` is the deliverable.** It is the exact same shape as `kubectl auth can-i delete deployments -n bird-prod` returning `no`. Two completely different systems, one idea: *identity plus scope equals permission*. Recognising that is worth more than memorising either syntax.
 
**Read (don't build) — the multi-account conversation, ~20 min:**
 
The production-grade answer to "how do you isolate environments on AWS" is **one account per environment under AWS Organizations**, with Service Control Policies at the org level. Know why:
 
| Boundary | Single account, IAM-scoped (what you're doing) | Separate accounts (what companies do) |
|---|---|---|
| Blast radius | A wildcard typo in a policy crosses environments | Nothing crosses without explicit cross-account trust |
| Service quotas | Shared — dev can exhaust prod's limits | Per-account |
| Billing | Cost allocation tags, best effort | Exact, per-account, unarguable |
| Cost to set up | Zero | An afternoon of Organizations work |
 
Say the honest version in interviews: *"I used per-environment IAM roles in one account because it teaches the same scoping discipline. At a company I'd expect separate accounts under an Organization, because that's the only boundary that's also a quota and billing boundary."*
 
**Visualize:** draw the security desk. Badge (user) → what it opens (policy). Uniform on a hook (role) → who may wear it (trust policy) → what it opens (permissions policy). Now draw **two** hooks, one labelled uat and one prod, with non-overlapping key rings. Two boxes and six arrows. That drawing answers the most common AWS interview question there is.
 
**Self-check:** user vs role in one sentence? Why is `get-caller-identity` the first thing you run on "access denied"? Why are temporary credentials safer than an access key in a config file? What does a naming convention have to do with security?
 
---
 
### Day 2 — S3: two environments go live (badly)

**There is no nginx in UAT or prod.** The config file cannot be a single source of
truth across environments no matter how well you factor it — S3 and CloudFront have
no concept of an nginx `location` block. What survives is the **policy**, not the file:

| Policy decision | dev (nginx) | UAT / prod (AWS) |
|---|---|---|
| `/Build/` immutable, 1 year | `add_header Cache-Control` | S3 object metadata at upload |
| `/TemplateData/` max-age=300 | `add_header Cache-Control` | S3 object metadata |
| `index.html` no-cache | `add_header Cache-Control` | S3 metadata + CloudFront behaviour |
| `/healthz` | nginx `return 200` | CloudFront/Route 53 health check |

Three identical decisions, three different mechanisms. This is *why* the `.wasm`
failure below is the same MIME/`Content-Encoding` bug from Week 3 Day 2 with no nginx
in sight — the decision persisted, its expression didn't.

**Interview line:** *"The cache policy is the durable decision; the config file is one
platform's expression of it. I document the policy and re-express it per platform."*

**Mental model:** S3 is a **warehouse**. Cheap, enormous, holds anything, properly indexed — but there is only one of it, and it might be a very long drive from your visitor. Buckets are not folders; they're a flat keyspace where `Build/game.wasm` is just a key that happens to contain a slash.
 
```bash
aws s3 mb s3://bird-platformer-uat  --region ca-central-1
aws s3 mb s3://bird-platformer-prod --region ca-central-1
 
# tag them immediately — Day 7's cost report depends on this
aws s3api put-bucket-tagging --bucket bird-platformer-uat --tagging \
  'TagSet=[{Key=Project,Value=bird-platformer},{Key=Environment,Value=uat},{Key=ManagedBy,Value=manual}]'
aws s3api put-bucket-tagging --bucket bird-platformer-prod --tagging \
  'TagSet=[{Key=Project,Value=bird-platformer},{Key=Environment,Value=prod},{Key=ManagedBy,Value=manual}]'
 
aws s3 sync web/game/ s3://bird-platformer-uat/
aws s3 ls s3://bird-platformer-uat/ --recursive --human-readable --summarize
```
 
**Build the `--dryrun` habit now, before prod exists in earnest:**
 
```bash
aws s3 sync web/game/ s3://bird-platformer-prod/ --delete --dryrun    # read the output
aws s3 sync web/game/ s3://bird-platformer-prod/ --delete             # then, and only then
```
 
`--delete` removes anything in the bucket that isn't in your local folder. Pointed at the wrong bucket, that is Week 3 Day 5's break #6 with a much worse ending. `--dryrun` is the S3 equivalent of `terraform plan`, and forming the habit here means you already have it in Week 5.
 
Enable static website hosting on UAT and open the URL. Two things will happen:
 
1. It will be **slow** — you're the only visitor and you're driving to the warehouse yourself.
2. The `.wasm` may **not load at all.**
**Stop before you look up the fix.** That second failure is the *exact same MIME type and `Content-Encoding` problem you already solved in the nginx ConfigMap* in Week 3 Day 2 — the browser refusing a file whose headers lie about what it is. Same bug, completely different place, no nginx anywhere in sight. Recognising it yourself is worth more than fixing it fast.
 
The fix is object metadata rather than server config:
 
```bash
aws s3 cp web/game/Build/ s3://bird-platformer-uat/Build/ --recursive \
  --exclude "*" --include "*.wasm.gz" \
  --content-type application/wasm --content-encoding gzip
 
aws s3 cp web/game/Build/ s3://bird-platformer-uat/Build/ --recursive \
  --exclude "*" --include "*.data.gz" \
  --content-type application/octet-stream --content-encoding gzip
```
 
**Environment lesson (NEW):** you just typed those commands once for UAT. Do **not** type them again for prod. Put them in a shell script that takes the environment as an argument:
 
```bash
# scripts/deploy-static.sh
set -euo pipefail
ENV="${1:?usage: deploy-static.sh <uat|prod>}"
BUCKET="bird-platformer-${ENV}"
aws s3 sync web/game/ "s3://${BUCKET}/" --delete
aws s3 cp web/game/Build/ "s3://${BUCKET}/Build/" --recursive \
  --exclude "*" --include "*.wasm.gz" \
  --content-type application/wasm --content-encoding gzip
# ... etc
```
 
```bash
./scripts/deploy-static.sh uat
./scripts/deploy-static.sh prod
```
 
**The moment you have two environments, every manual command is a bug waiting to happen** — because the second time you type it, you will type it slightly differently, and then UAT and prod are no longer comparable. This script is a rough draft of Day 6's pipeline and of Week 5's Terraform. That progression — command → script → pipeline → declarative code — is the whole automation story, and you're walking up it deliberately.
 
**Visualize:** devtools → Network, screenshot the `.wasm` response headers before and after, side by side with the nginx equivalents from Week 3 Day 2. That's a README section showing you understand a *concept* rather than a *command*.
 
**Self-check:** why is serving production traffic straight from S3 a bad idea even though it works? What does "static website hosting" actually turn on? Why does `--delete` deserve more caution than the rest of the command?
 
---
 
### Day 3 — CloudFront: two environments go live (properly)
 
**Mental model:** if S3 is the warehouse, CloudFront is a chain of **corner shops worldwide**, each keeping a copy of your popular items so nobody drives to the warehouse. A shop without your item fetches it once, keeps it, and serves everyone else locally.
 
```
  player in Berlin ──▶ CloudFront edge (Frankfurt) ──cache HIT──▶ instant, never leaves Germany
                                  │
                              cache MISS (first visitor only)
                                  ▼
                        S3 bucket (ca-central-1)  ◀── locked: reachable ONLY via CloudFront
```
 
**Do — both distributions (~90 min, most of it waiting):**
- Create a distribution per bucket. Deployment takes ~15 minutes each; **start both, then go do something else.** Don't sit watching.
- Lock both buckets with **Origin Access Control** so they're only reachable *through* CloudFront. Then try the direct S3 URLs again and confirm they're denied.
- Load the game through each CloudFront URL. Compare load time against yesterday's S3 URL.
**Environment difference that isn't just a number (NEW) — cache TTLs:**
 
| | UAT | prod |
|---|---|---|
| Default TTL | **60 seconds** | 86400 (1 day) |
| `/Build/*` TTL | 60 seconds | 31536000 (1 year) — safe now that filenames are hashed |
| Why | You deploy constantly and need to *see* it | Nobody should re-download 48 MB twice |
 
This is the first environment setting that changes *behaviour* rather than *scale*, and it introduces a genuine risk worth naming: **UAT with short TTLs is not testing prod's caching behaviour.** A stale-asset bug will appear only in prod. The mitigation is that the fix is the same everywhere — hashed filenames, which you enabled in Week 3 Day 2 — and that prod deploys always invalidate. Say this in an interview and you sound like someone who has been burned, which is the point.
 
**Trap to expect:** you will change a file, re-upload, reload, and still see the old version. Nothing is broken — the corner shop still has yesterday's stock. Look up **cache invalidation** before assuming you broke the deployment:
 
```bash
aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
```
 
Note it down: **an invalidation is per-distribution.** Two environments means two invalidations, and forgetting the prod one means the deploy "succeeded" and nobody sees it. Straight into `k8s/notes/`.
 
**Extend the script:**
 
```bash
# scripts/deploy-static.sh, continued
DIST_ID=$(aws cloudfront list-distributions \
  --query "DistributionList.Items[?Comment=='bird-platformer-${ENV}'].Id | [0]" --output text)
aws cloudfront create-invalidation --distribution-id "$DIST_ID" --paths "/*"
```
 
**This is the moment the project becomes shareable.** Send the prod URL to someone and watch them play it. Protect that feeling — it's what keeps the last two weeks from feeling like homework.
 
**Interview note worth having ready:** for a static WebGL build, S3 + CloudFront **is** the correct production answer — cheaper, faster, and far less to operate than Kubernetes. You have the Kubernetes version because the market asks for it. Being able to say *"here's what I built on Kubernetes, here's what I'd actually ship, and here's why they differ"* demonstrates **judgment**, which is what separates a senior candidate from someone who followed a tutorial. Rehearse that out loud today, while the comparison is fresh.
 
---
 
### Day 4 — VPC and networking, slowly, with two buildings
 
**Read this model twice. It's the one that pays off in interviews.**
 
A **VPC** is a private office building you rent inside a huge city (an AWS region). Nothing gets in or out except through doors you install yourself.
 
```
 REGION (the city)
 ┌──────────────── VPC 10.0.0.0/16 — the UAT building ──────────────┐
 │                                                                   │
 │   ☰ Internet Gateway  ◀── the street door                         │
 │          │                                                        │
 │   📋 Route table: "for anywhere on the street (0.0.0.0/0),        │
 │          │          go through that door"                         │
 │          ▼                                                        │
 │   ┌─── public subnet 10.0.1.0/24 — ground floor ──────┐           │
 │   │   EC2 #1     🚪 SG: allow 22 from my IP only      │           │
 │   └──────────────────┬─────────────────────────────────┘          │
 │                      │  ping works — inside the building          │
 │   ┌─── private subnet 10.0.2.0/24 — upper floor ──────┐           │
 │   │   EC2 #2     no route to the street → SSH fails   │           │
 │   └──────────────────────────────────────────────────┘           │
 └───────────────────────────────────────────────────────────────────┘
 
 ┌──────────────── VPC 10.1.0.0/16 — the PROD building ─────────────┐
 │   different building, different street door, NO connection       │
 │   to the UAT building unless you deliberately build a bridge     │
 └───────────────────────────────────────────────────────────────────┘
```
 
- **Subnets** are floors.
- A subnet is "public" **only because its route table points at the street door.** Take the sign down and it's just another floor. There is no checkbox called "public" — this trips nearly everyone, and interviewers know it.
- A **Security Group** is a bouncer on each individual room door checking a guest list. **Stateful**: if the bouncer let you in, you're allowed back out unchecked.
- A **NACL** is a guard at the floor's stairwell — **stateless**, checks you both directions. Know the distinction; you rarely touch NACLs.
- A **NAT Gateway** is a mailroom letting upstairs staff send letters out while nobody outside can send letters directly in. You will not create one. Know the name and the $32/month.
**The environment lesson (NEW) — CIDR planning, and why the numbers aren't arbitrary:**
 
| Env | VPC CIDR | Public subnet | Private subnet |
|---|---|---|---|
| uat | `10.0.0.0/16` | `10.0.1.0/24` | `10.0.2.0/24` |
| prod | `10.1.0.0/16` | `10.1.1.0/24` | `10.1.2.0/24` |
 
You *could* give both buildings the same internal numbering — nothing stops you, and each works fine alone. The problem shows up the day you want to connect them (VPC peering, a VPN, a Transit Gateway). **If two buildings both have a "Room 101," a letter addressed to Room 101 has no unambiguous destination.** Overlapping CIDR ranges make peering impossible, and the fix is rebuilding a live network — which nobody wants to do.
 
So: **allocate non-overlapping ranges before you need them, even if you never connect the environments.** That's the whole lesson, it costs nothing today, and "we planned non-overlapping CIDRs per environment" is a sentence that marks you out immediately in a networking conversation.
 
**Do:** build the **UAT** VPC by hand in the console — VPC, two subnets, internet gateway, two route tables, tagged `Environment=uat`. Doing it by hand exactly once is what makes Week 5's Terraform version make sense instead of being magic incantations.
 
**Do not build the prod VPC by hand.** Design it on paper with the table above, then leave it. Building it twice by clicking teaches you nothing the first time didn't, and Week 5 Day 3 will create it from code in twenty seconds — which is a far more satisfying demonstration of why IaC exists. Write down how many clicks the UAT one took; you'll want that number.
 
**Visualize:** redraw the diagram from memory with your actual CIDR blocks, both buildings, and check against the console.
 
---
 
### Day 5 — Prove it, break it, prove the isolation, terminate it
 
```bash
# public subnet instance — should work
ssh -i key.pem ec2-user@<public-ip>
 
# private subnet instance — WILL FAIL.
# Work out why from yesterday's model BEFORE looking it up.
ssh -i key.pem ec2-user@<private-ip>
 
# from inside the public instance, reach the private one:
ping <private-ip>        # works — the building is connected internally
```
 
**Do:**
- Launch a `t3.micro` in each UAT subnet.
- Prove the asymmetry: reachable from the street vs reachable only from inside.
- **Break the working one on purpose:** remove the `0.0.0.0/0` route from the public subnet's route table and watch SSH die. Put it back. Fastest possible demonstration that "public" is a property of the *route table*, not the subnet.
- Break it a second way: remove port 22 from the security group. Same symptom, different cause — which is exactly why the debugging *order* matters.
**The environment isolation proof (NEW, ~20 min) — and it closes a loop you opened in Week 2:**
 
If you have credits and patience, launch one `t3.micro` in a hand-made prod VPC (or just reason it through with the console open — the answer is unambiguous). Then from the UAT instance:
 
```bash
ping <prod-instance-private-ip>          # times out — no route exists at all
```
 
Compare that with the Kubernetes side. In `bird-dev`, a pod could reach `bird-prod` by DNS with no configuration whatsoever until you wrote a NetworkPolicy, because **namespaces are an organisational boundary, not a network one.** Two VPCs are the opposite: they are isolated by default and connecting them takes deliberate work (peering, and non-overlapping CIDRs — which is why yesterday mattered).
 
> **Kubernetes namespaces: open by default, isolated by policy.**
> **AWS VPCs: isolated by default, connected by policy.**
 
That contrast is one of the best two-sentence answers you can carry into an interview about environment isolation, and you will have *demonstrated* both halves rather than read them.
 
- **Terminate every instance the same day.** Set a phone alarm.
**Self-check — memorise this order, it's a standard interview question.** If you can't reach an instance:
1. **Security group** — is the port open from your IP?
2. **Route table** — does the subnet have a path to the internet gateway?
3. **Internet gateway** — is one attached to the VPC at all?
4. **Subnet / public IP** — does the instance even have a public address?
And the fifth, which only exists once you have environments: **are you looking at the right VPC?**
 
---
 
### Day 6 — Make it ship itself, to two environments, and watch it
 
**Part 1 — the promotion pipeline (the practical outcome of the whole week):**
 
```
  git push main
     │
     ▼
  GitHub Actions
     ├─ Unity tests                        already in ci.yml
     ├─ Unity WebGL export                 added Week 3 Day 7
     └─ upload web/game/ as a workflow artifact     ← BUILT ONCE
                 │
       ┌─────────┴──────────┐
       ▼                    ▼
  ┌─────────────┐    ┌─────────────────────────┐
  │ deploy uat  │    │ deploy prod             │
  │ automatic   │───▶│ environment: prod       │
  │             │    │ ⏸ WAITS FOR APPROVAL    │
  │ s3 sync     │    │ s3 sync                 │
  │ invalidate  │    │ invalidate              │
  └─────────────┘    └─────────────────────────┘
```
 
**The critical detail: download the *same* workflow artifact in both deploy jobs.** Do not run the Unity build twice. A second Unity build produces a different set of bytes — different timestamps at minimum, and possibly a different IL2CPP output — and then prod is serving something UAT never tested. This is *build once, deploy many* made literal, and it's the difference between a pipeline and three scripts that happen to run in order.
 
```yaml
  deploy-uat:
    needs: build
    if: github.ref == 'refs/heads/main'
    environment: uat
    steps:
      - uses: actions/download-artifact@v4
        with: { name: webgl-build, path: web/game }
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: ${{ secrets.AWS_UAT_ROLE_ARN }}
          aws-region: ca-central-1
      - run: ./scripts/deploy-static.sh uat
 
  deploy-prod:
    needs: deploy-uat
    environment: prod            # ← pauses for approval, same as Week 3 Day 7
    steps:
      - uses: actions/download-artifact@v4
        with: { name: webgl-build, path: web/game }     # THE SAME ARTIFACT
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: ${{ secrets.AWS_PROD_ROLE_ARN }}
          aws-region: ca-central-1
      - run: ./scripts/deploy-static.sh prod
```
 
**Note the two different role ARNs.** The UAT job literally *cannot* write to the prod bucket — you proved that on Day 1. If the pipeline has a bug that sends a UAT deploy to the wrong place, IAM refuses it. **Defence in depth: the naming convention, the role scoping, and the pipeline structure each independently prevent the same mistake.**
 
**Do — credentials, and the answer that marks you out:**
- Get it working today with AWS access keys as GitHub Actions secrets.
- Then read one section on **OIDC federation** — the mechanism that lets GitHub Actions assume an IAM *role* instead of holding long-lived keys. GitHub presents a short-lived token, AWS trusts it because of a trust policy that names your repo, and there is **no secret stored anywhere to leak**. With environments this gets better still: the trust policy can be scoped to `repo:elisag94/Bird-Platformer:environment:prod`, so a workflow running in the UAT environment cannot assume the prod role *at all*, regardless of what the YAML says.
- You don't have to implement it. You do need to be able to say: *"We'd use OIDC rather than static keys, with the trust policy scoped per environment, so there's no secret to leak and a UAT job can't assume the prod role."*
**Prove it end to end:** change one line of text in the game, push, watch UAT update by itself, then approve and watch prod follow. Screenshot the approval prompt.
 
**Part 2 — CloudWatch, lightly (~30 min):**
- Find the CloudFront metrics per distribution: requests, bytes downloaded, **cache hit ratio**.
- **Cache hit ratio is the number to screenshot.** Clearest evidence CloudFront is doing its job, and it pairs beautifully with the Grafana screenshots from Week 3 — same instinct, different platform.
- **Compare UAT's hit ratio to prod's.** UAT's will be dramatically worse. That is not a bug — it's the 60-second TTL you chose on Day 3, visible as a number. An environment difference you deliberately introduced, showing up in a metric, is a small but real taste of what capacity planning feels like.
- Set one alarm on prod (4xx rate above a threshold). Same pattern as the Prometheus alert; notice it's the same idea with different vocabulary. **Only prod gets the alarm** — same reasoning as Week 3 Day 3's routing.
---
 
### Day 7 — Teardown, per-environment cost, write it down
 
- Terminate every instance. Walk the console for orphans — an orphan is a bug in your process, not a nuisance. Check **both** VPCs.
- **Keep** both S3 buckets + both CloudFront distributions: they're your live URLs, they cost essentially nothing at your traffic, and Week 5 rebuilds them from Terraform.
- **Per-environment cost (NEW):** activate your cost allocation tags (Billing → Cost allocation tags → activate `Environment` and `Project`), then in Cost Explorer group by the `Environment` tag.
```bash
aws ce get-cost-and-usage \
  --time-period Start=2026-09-01,End=2026-09-30 \
  --granularity MONTHLY --metrics UnblendedCost \
  --group-by Type=TAG,Key=Environment
```
 
The numbers will be tiny. **That's not the point** — the point is that you can answer "what does UAT cost us?" with a command instead of a guess, and that you tagged things on the way in rather than trying to reconstruct ownership later. Untagged resources are unattributable forever; that's the actual lesson, and "have you worked with cloud cost?" is a real interview question with a real answer now.
 
- Log every AWS problem into the symptom → cause → fix file. You will forget the details within a fortnight and they are interview gold.
**Week 4 checkpoint — say all three out loud:**
1. Trace a request from a player's browser to a file in S3 and name every hop.
2. Explain why you'd choose S3 + CloudFront over the Kubernetes version, and why you built both.
3. Explain how a change gets from your laptop to the prod URL, what stops it going straight there, and what would stop it even if the pipeline had a bug.
---
 
## Quick reference — what to build, in order
 
| Week 3 | Artifact produced |
|---|---|
| D1 ✅ | Probes both tiers; "unready, RESTARTS unchanged" screenshot; liveness-404 crashloop with `Ready: True` beside climbing `RESTARTS` |
| D2 | Sizing table, quota rejection note, compression before/after numbers |
| D3 | Grafana dashboard with a `$namespace` variable + one alert rule |
| D4 | HPA + the "quota silently caps scaling" note |
| D5 | Six-entry break-things log, per-env RBAC, `kubectl auth can-i` proof |
| D6 | **Helm chart + three values files + three running environments + the render diff** |
| D7 | **Pipeline with a paused prod job awaiting approval** |
 
| Week 4 | Artifact produced |
|---|---|
| D1 | Two IAM roles + the cross-environment `AccessDenied` |
| D2 | Two buckets, tagged, and `scripts/deploy-static.sh` |
| D3 | Two live CloudFront URLs with different TTL policies |
| D4 | UAT VPC by hand + the CIDR allocation table |
| D5 | Route-table/SG break log + the cross-VPC isolation proof |
| D6 | **UAT auto-deploys, prod waits for you, both from one artifact** |
| D7 | Per-environment cost report + teardown with zero orphans |