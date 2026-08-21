# Week 3 Day 2 — Resource requests, limits, quotas, and bytes on the wire

**Date:** 18 August 2026
**Cluster:** minikube, namespace `bird-platformer` (+ new `bird-dev`)
**Status:** ✅ complete — all three parts

---

## Mental model

**Request = the seat you reserve. Limit = the point the bouncer throws you out.**

You book a table for four. The *request* is the booking — it's what the scheduler uses
to decide whether you fit in the room tonight. It doesn't stop you pulling up a fifth
chair if the next table is empty. The *limit* is the fire marshal's cap: exceed it and
you're removed.

| Resource | Exceed the limit | Why |
|---|---|---|
| **Memory** | **OOMKilled** — instantly, no warning | You can't half-have a byte. Allocated or not. |
| **CPU** | **Throttled** — slowed, stays alive | Time is divisible. Fewer slices per second. |

**Memory is a cliff. CPU is a speed bump.** Say this out loud in an interview; it
answers three follow-ups at once.

### QoS classes

| Class | When | Evicted under node pressure |
|---|---|---|
| `Guaranteed` | requests == limits, every container | last |
| `Burstable` | requests set, limits higher | middle ← **game tier is here now** |
| `BestEffort` | nothing set | **first** |

QoS governs *eviction priority when the node runs short*. It does **not** protect you
from your own limit — a `Guaranteed` pod with a 6Mi limit still gets OOMKilled at 6Mi.

---

## Part 1 — Measure, then set

### Why nginx memory barely moves

Predicted the pod's memory would climb by ~48 MB while shipping the `.wasm`. It doesn't.

nginx uses the kernel's `sendfile()` — *"kernel, take that file on disk and pour it
straight down that socket."* The bytes go disk → NIC without passing through nginx's
own memory. **nginx isn't carrying the plate; it's pointing at a conveyor belt.** It
needs about as much strength for 48 MB as for 2 KB.

The number you *do* see move is **page cache** — the kernel keeps recently-read file
contents in RAM, and in a container that cache is charged to the cgroup. It is
**reclaimable** (dropped instantly under pressure) but it **does** count against the
memory limit. The kernel evicts cache before it OOM-kills.

> **Size requests off the steady-state floor, not the cache-inflated peak.** Reserving
> 53Mi books a seat for a passenger who doesn't exist and makes pods harder to schedule.

### Measured — `kubectl top pods`, after 4 hard refreshes

```
bird-platformer-game-764dd859b-dccsw   1m   53Mi
bird-platformer-game-764dd859b-jfg7x   1m   26Mi
bird-platformer-game-764dd859b-jtmsw   1m    7Mi
leaderboard-656dbffc55-2p4mk           2m  100Mi
leaderboard-656dbffc55-mwkfl           2m  102Mi
postgres-64d864747b-4ldd6              3m   51Mi

node minikube: 320m CPU (4%), 2595Mi memory (32%)
```

**Three identical pods, 7.5× memory spread.** Same image, same config — different
histories. Service round-robin distributed requests unevenly, and page cache
accumulated on whichever pod served the most bytes. *Identical pods are not
interchangeable observations.*

### Sizing decision — game tier

```yaml
        resources:
          requests: { memory: "32Mi", cpu: "50m" }
          limits:   { memory: "128Mi", cpu: "200m" }
```

| Number | Justification |
|---|---|
| `requests.memory: 32Mi` | 7Mi measured floor + headroom. Not 53Mi — that's reclaimable cache. |
| `limits.memory: 128Mi` | Room for cache to breathe; a hard stop if nginx genuinely leaks. |
| `requests.cpu: 50m` | Measured 1m idle. A schedulable floor, not a load prediction. |
| `limits.cpu: 200m` | CPU throttles rather than kills — worst case it gets slow, not dead. |

Result: `kubectl get pod -o jsonpath='{...status.qosClass}'` → **`Burstable`** ✅

**Honest note on the leaderboard tier:** its `requests: 50m/128Mi` was set on 14 Aug
*without* measuring, as a panic fix for the probe-induced crashloop. Measured today at
~100Mi — well-sized by luck. **"Guessed, later verified correct" is a weaker claim than
"measured."** Say the accurate one.

---

## Part 2 — ResourceQuota and LimitRange

**ResourceQuota** = the floor's total power budget. The whole floor may draw 2 kW; once
it's used, the next appliance doesn't get plugged in.

**LimitRange** = the house rule that any appliance without a stated wattage is *assumed*
to be 100 W — so nobody sneaks an unmetered space heater onto the floor.

They are a pair. **The quota only works because the LimitRange guarantees nothing is
unmetered.** Without it, a container declaring no limits is unaccountable and the quota
rejects it outright.

Files: `k8s/env/dev/resourcequota.yaml`, `k8s/env/dev/limitrange.yaml`

```
requests.cpu: 500m   requests.memory: 512Mi
limits.cpu:   1      limits.memory:   1Gi     pods: 6
```

### The drill

`kubectl -n bird-dev create deployment fat --image=nginx --replicas=5` → all 5 ran.
The LimitRange silently stamped `200m/128Mi` on each. Quota then read:

```
limits.cpu       1      1      ← exactly at the ceiling
pods             5      6
```

Scale to 6 → `DESIRED 6, AVAILABLE 5`, forever, silently.

**The binding constraint was `limits.cpu`, not `pods`.** 5 × 200m = 1000m = the cap.
Two ceilings; the one that bit wasn't the obvious one. **Read every row of
`describe resourcequota`, not the row you expected.**

---

## Symptom → cause → fix

| # | Symptom | Cause | Fix / lesson |
|---|---|---|---|
| 1 | `kubectl apply` rejected: *"requests 32Mi must be less than or equal to memory limit of 4Mi"*. No ReplicaSet, no pod, nothing to describe. | Tried to lower `limits` without lowering `requests`. | A request can never exceed its limit — you can't reserve a table for four in a room that seats two. Rejected by the **API server**, before any object exists. |
| 2 | Pod stuck `CreateContainerError`. `Last State` **empty**. `describe \| grep "Last State"` returned nothing at all. | `limits.memory: 4Mi` is below the container runtime's floor for building a cgroup. The runtime refused the config. | **The container was never created, so there is no death to describe.** Config failure, not runtime failure — no exit code exists to read. Same signature as Drill 5 Case B (`Init:CreateContainerConfigError`, 14 Aug). |
| 3 | `CrashLoopBackOff`, `RESTARTS` climbing. `Reason: OOMKilled`, `Exit Code: 137`, `Started` and `Finished` on the **same second**. | `limits.memory: 6Mi`, just under nginx's real 7Mi need. | **137 = 128 + 9 = killed by SIGKILL.** Unambiguous, unlike the 17 Aug `Exit Code 0 / Completed` case. nginx never got as far as binding port 80. |
| 4 | Rollout never completed during #2 and #3 — the three healthy old pods stayed up and the site never went down. | `maxUnavailable` defaults to 25%. | **A Deployment refuses to tear down working pods for replacements that won't go Ready.** A bad config that crashes on boot cannot take down a running service. This is the practical argument for readiness probes gating rollouts. |
| 5 | Deployment reads `5/6` indefinitely. `kubectl get pods` shows 5 pods, none pending, none failing. Nothing to describe. | ResourceQuota rejected pod creation at admission. | **Errors live on the object that *tried* to create, not the one that doesn't exist.** `kubectl describe replicaset -l app=fat` → `FailedCreate ... exceeded quota`. The ReplicaSet retried a dozen times in 13s and backed off — it never gives up, same as CrashLoopBackOff. |
| 6 | `watch` not found on macOS. | coreutils habit that doesn't transfer to a Mac. | `while true; do clear; kubectl top pods -n bird-platformer; sleep 2; done`, or `brew install watch`. Worth knowing before screen-sharing on someone else's machine. |

---

## Part 3 — Compression (bytes on the wire)

Same discipline as the memory numbers: measure, act, measure again.

**Unity:** Player Settings → Publishing → Compression Format **Gzip**, Decompression
Fallback **off** (on, Unity ships a JS decompressor and the exercise is pointless).

**The analogy:** gzip is vacuum-packing. The file arrives flat and sealed.

- `Content-Encoding: gzip` = the label saying *"vacuum-packed, unwrap me first"*
- `Content-Type: application/wasm` = *"and what's inside is a game"*

Without the first, the browser hands you a sealed bag. Without the second, it doesn't
know what to do once opened. **Two headers, two entirely different jobs.**

### nginx footgun — `add_header` does not inherit

Regex locations (`~`) take priority over prefix locations (`/Build/`), and **nginx does
not merge `add_header` across blocks — the innermost block containing *any* `add_header`
wins and every parent directive is dropped.** That is why `Cache-Control` is now
repeated four times in one config file. Real interview anecdote.

```nginx
location ~ \.wasm\.gz$ {
    add_header Content-Encoding gzip;
    add_header Cache-Control "public, max-age=31536000, immutable";
    default_type application/wasm;
}
location ~ \.js\.gz$   { ... default_type application/javascript; }
location ~ \.data\.gz$ { ... default_type application/octet-stream; }
```

Rebuilt as `bird-platformer:v4` — **new tag required**, because rebuilding under the same
tag leaves the Deployment spec byte-identical and `apply` reports `unchanged`.
Kubernetes watches the spec, not the image contents.

### Result

```
Before: 48,224,945 bytes transferred   (14 Aug 2026)
After:  17,600,000 bytes transferred   (18 Aug 2026)   ≈ 2.7× — 30.6 MB saved per new player
```

| File | Transferred |
|---|---|
| `aad016808...wasm.gz` | 12,054 kB |
| `4a38ec35b...data.gz` | 5,417 kB |
| `0632f05a8...framework.js.gz` | 85 kB |

**Proof the headers landed:** devtools' **Type** column reads `wasm` and `script`.
Without `Content-Type` those rows would read `gzip`/`other` and the game would not run.

**Unreconciled number, flagged not hidden:** devtools reports "61.4 MB resources"
(uncompressed) against a 48.2 MB pre-compression *transferred* baseline. Either the build
grew since 14 Aug or the two figures measure different things. Like-for-like comparison
is transferred → transferred. **"I noticed the numbers didn't reconcile" beats a clean
number I can't defend.**

---

## Forward links

- **Day 4 (HPA):** `bird-dev`'s quota will silently cap autoscaling — the HPA asks for
  more pods, the quota refuses, nothing errors. Keep the namespace.
- **Day 6 (Helm):** `resources` becomes a values-file key. Probes do **not**.
- **Week 4 Day 2 (S3):** the same `Content-Encoding`/MIME decision, re-expressed as S3
  object metadata with no nginx anywhere. *The cache/encoding policy is the durable
  decision; the config file is one platform's expression of it.*

## Self-check

1. Does the scheduler use request or limit? *(Request.)*
2. Node's requests fully booked but actual usage is 10% — what happens? *(Nothing new
   schedules. Requests are reservations, not measurements.)*
3. Which QoS class are the game pods in, and what does that buy them?
4. Why did the quota rejection produce no failing pod to inspect?
5. Why does `Exit Code 137` end a debugging session that `Exit Code 0` doesn't?