# Week 2, Day 6 — The leaderboard: multi-tier build

**Date:** 2026-08-12
**Format:** hands-on first, docs second

The day the project stopped being "a static site on Kubernetes" and became a real
architecture: two services behind one hostname, a database with persistent storage,
config split between a ConfigMap and a Secret, and enforced network segmentation.

---

## What got built

```
   [ player's browser ]  ← the game runs HERE, outside the cluster
        |  GET  http://bird.local/            → game files
        |  POST http://bird.local/api/scores  → the score
        v
   +-- Ingress: the front desk ------------------+
   |   /     -> bird-platformer-service          |
   |   /api  -> leaderboard-service              |
   +---------------------------------------------+
        |                          |
        v                          v
   [ nginx pods x3 ]         [ flask pods x2 ]
                                   |  host "postgres" — internal name only
                                   v
                          [ postgres-service ] -> [ postgres pod ] -> [ PVC ]
```

**The framing that made the networking click:** the game does not run in the cluster.
Unity Web compiles to files that are downloaded and executed in the player's browser.
The cluster only hands over those files. So the browser is a customer *on the street*
who now needs to reach two different rooms — which is why the API needs a route through
the Ingress, and why Postgres deliberately has no street door at all.

---

## Design decisions worth defending

| Decision | Why |
|---|---|
| **No authentication** | Passwords, hashing, sessions and token storage in a Web build is a day of work that teaches nothing about Kubernetes. A display name is enough. Deliberately scoped out, not half-built. |
| **Client holds the stopwatch** | Unity measures and sends `duration_ms`. Trivially cheatable — anyone can `curl` a 1 ms run. Mitigated by server-side bounds validation (`MIN_RUN_MS` / `MAX_RUN_MS`); the real fix is a server-issued run token so elapsed time comes from the server's clock. **Know the weakness, name the fix.** |
| **Ranking is a query, not a column** | Every run is stored; best-per-player is derived with `DISTINCT ON`. Throwing away history is irreversible. |
| **Milliseconds as `INT`** | Floats make ties and sorting subtly wrong and look like bugs in a UI. |
| **gunicorn, not `flask run`** | The dev server is single-threaded and prints a warning telling you not to ship it. |
| **Postgres as a Deployment, not a StatefulSet** | Fine for one replica with no backups. Name the difference in an interview rather than pretending it wasn't a choice. |

### Config split

```
ConfigMap (leaderboard-config)      Secret (leaderboard-db)
  DB_HOST=postgres                    DB_USER
  DB_PORT=5432                        DB_PASSWORD
  DB_NAME=birdscores
  MIN_RUN_MS / MAX_RUN_MS
  LOG_LEVEL
```

Rule of thumb: **if it appearing in a screenshot would be embarrassing, it is a Secret.**
`DB_HOST=postgres` is useless to anyone not already inside the cluster.

Two injection styles used side by side, on purpose:

- **`envFrom`** on the Flask pods — pulls every key in under its own name. Terse, and
  works because `db.py` was written to expect exactly those names.
- **`secretKeyRef` / `configMapKeyRef`** on the Postgres pod — pulls single keys and
  *renames* them. The official Postgres image wants `POSTGRES_USER`; the app wants
  `DB_USER`. Same Secret, two spellings, no chance of them drifting apart.

---

## Symptom → cause → fix log

| Symptom | Cause | Fix |
|---|---|---|
| Leaderboard pods `Error` → `Running 1`. Logs: `UniqueViolation: duplicate key ... players_id_seq` | `create_all()` at module import, run by 2 replicas × 2 gunicorn workers = **4 concurrent check-then-act** table creations. Losers crash. Self-healed on retry, which hid it. | **Open.** Designed fix: move schema creation to `init_db.py` run by an initContainer, guarded by `pg_advisory_xact_lock`. |
| `503` from nginx on `/api`. `describe ingress` backend column: `echo:80 (<error: services "echo" not found>)` | `k8s/ingress.yaml` was never updated from the Day 4 path-routing exercise. The `echo` Service was created in `default` and doesn't exist in this namespace. | Rewrite the Ingress with the real backend. **Read `describe ingress` first, every time.** |
| `COPY ... not found` on a Dockerfile whose paths are obviously correct | Built from inside `docker/`, so the build **context** was `docker/`. `COPY` resolves inside the context, never relative to the Dockerfile. | `docker build -f docker/Dockerfile -t bird-platformer:v2.1 .` from the repo root. |
| NetworkPolicies applied cleanly, `kubectl get networkpolicy` looked right, **nothing was blocked** | minikube's default CNI does not enforce NetworkPolicy. The API server stores the object regardless — enforcement is the CNI's job. | `minikube delete && minikube start --cni=calico`. **Check before writing any policy.** |
| Every path returns 503 after applying `default-deny-ingress`. Pods `Running`, `Ready`, endpoints populated. | The ingress-nginx controller lives in another namespace and became just another blocked source. | Allow policy with `namespaceSelector` on `kubernetes.io/metadata.name: ingress-nginx`. |
| (Anticipated) allow policy applied but leaderboard still unreachable | NetworkPolicy `ports` are the **container's** port, not the Service's. Policy is enforced at the pod, *after* the Service's 80 → 8080 translation. | List 8080 for Flask, 80 for nginx. |

### Two traps avoided by design rather than by suffering

Recording these honestly — they were built in from the start, not debugged:

- **`PGDATA` points at a subdirectory** of the volume mount. A freshly provisioned
  volume often contains `lost+found`, and Postgres refuses to initialise into a
  non-empty directory.
- **Postgres uses `strategy: Recreate`.** With a `ReadWriteOnce` PVC, a rolling update
  would start the new pod before terminating the old one, the new pod could not mount
  the volume, and it would sit `Pending` indefinitely.

---

## The lesson that transfers furthest

**The pod was healthy and the site was down.**

After `default-deny-ingress`, every pod was `Running`, `Ready`, and listed in its
Service's endpoints — while every request 503'd. Readiness probes kept passing because
the **kubelet dials the pod IP directly**, a path the policy never touched.

Two different meanings of "reachable", and only one of them was broken. "The app is
healthy" answers exactly one question out of the several between a browser and a
process.

### The 503 debugging ladder

Used twice today, stopped at step 1 both times:

```
1. kubectl describe ingress <name>   → backend column shows an error?
                                        → Service missing or in the wrong namespace
2. kubectl get endpoints <service>   → empty?
                                        → selector matches nothing, or readiness failing
3. kubectl describe pod <name>       → why is it not ready?
```

And the routing-vs-application split, which halves the search space in one command:

- **404 with an HTML body / `Server: nginx`** → no Ingress rule matched. Never reached Flask.
- **404 with a JSON body** → routing worked; Flask genuinely has no such route.
- **`kubectl logs deploy/leaderboard`** → if the request isn't in gunicorn's access log,
  it never arrived. Which side of the wall the evidence is on tells you which side the
  bug is on.

`kubectl port-forward svc/leaderboard-service 8080:80` does the same job from the other
end: it skips the Ingress entirely, so if port-forward works and `bird.local` doesn't,
the application is fine and the problem is routing.

---

## Persistence, proven

```bash
kubectl delete pod -l app=postgres
# new pod, new name, new IP, new container filesystem
curl -s "http://bird.local/api/scores/top?level_id=Level01&limit=10"
```

Scores survived. The container's writable layer died with the container; the data lives
on a PersistentVolume that outlived the pod entirely.

---

## NetworkPolicy — the model

All three policies are one system, not alternatives:

| Kept | Result |
|---|---|
| All three | Locked floor, two specific doors open. |
| `default-deny` only | Everything 503s. |
| The two allows only | **Nothing enforced** — identical to writing none. |

**Policies are purely additive and there is no deny rule type.** You cannot write "allow
everything except the database." The only vocabulary is: stop governing this pod's
ingress, or govern it and enumerate every permitted source. Deleting the deny doesn't
tighten anything — it removes the wall and leaves two doors standing in a field.

The proof, and the best 30-second demo in the repo:

```bash
kubectl run probe --rm -i --restart=Never --image=busybox:1.28 -- \
  sh -c 'nc -w 3 postgres 5432 </dev/null && echo REACHABLE || echo BLOCKED'
# → BLOCKED

kubectl run probe-api --rm -i --restart=Never --image=busybox:1.28 --labels=role=api -- \
  sh -c 'nc -w 3 postgres 5432 </dev/null && echo REACHABLE || echo BLOCKED'
# → REACHABLE
```

Same image, same namespace, same command. **One label is the entire difference between
having database access and not.**

Note the failure mode: it *hangs* for three seconds and then fails, rather than refusing
instantly. A dropped packet gives silence — there is nothing on the far end to send a
refusal. A timeout where a connection-refused was expected is a strong hint that
something is filtering rather than something being down.

---

## Self-check answers

**Q: Do namespaces provide network isolation?**
No. They are an *organisational* boundary. Pods in one namespace can reach pods in
another by default — different floors, same open-plan air. NetworkPolicies add the
locks, and only if the CNI enforces them.

**Q: Why does `/healthz` sit outside the `/api` prefix when the Ingress only routes `/api` here?**
Health probes never travel through the Ingress or the Service. The kubelet dials the pod
IP directly, so probe paths don't need to match any routing rule.

**Q: Why are liveness and readiness separate endpoints?**
They answer different questions. If Postgres hiccups the API is *alive* — restarting it
will not fix a database — but *not ready*, so traffic should stop. Pointing liveness at
a DB check makes every replica fail simultaneously and Kubernetes restarts the entire
deployment: one outage becomes two.

**Q: What does the Ingress see, with no rewrite annotation?**
The full path. `POST http://bird.local/api/scores` arrives at Flask still reading
`/api/scores` — the front desk forwards the envelope, it doesn't retype it. Routes are
declared with the prefix included. Adding `rewrite-target` would make Flask see
`/scores` and 404 everything.

**Q: Why is there no CORS problem?**
The game and the API share one origin (`bird.local`), so the browser never asks the
question. This stops being free in Week 4 when the game moves to CloudFront and the API
does not.

---

## Open items

- [ ] **`create_all` race** — implement `init_db.py` + initContainer + advisory lock.
      Production answer is Alembic from a Job or a Helm pre-install hook; `create_all()`
      cannot express "add a column to an existing table."
- [ ] **Egress policy drill** — block all egress from a pod and watch DNS itself break.
      Symptom is "host not found", not "connection refused", because everything depends
      on CoreDNS on port 53 to `kube-system`. Looks like a completely different bug.
- [ ] **Server-authoritative timing** — `POST /api/runs/start` returning a run token.
- [ ] Carried from Day 2: encryption at rest + external secret stores (vocabulary only);
      Drill 5 Cases B and C.
- [ ] Unity side: `RunTimer.cs`, name capture via `PlayerPrefs`, `LeaderboardClient.cs`
      using `UnityWebRequest` (Web builds cannot use `System.Net.Http` or raw sockets).

## Manual steps that are not yet in YAML

Each has a real-world counterpart worth naming:

| Manual step | What replaces it in production |
|---|---|
| `minikube addons enable ingress` | Helm-installed ingress controller |
| `/etc/hosts` entry for `bird.local` | Real DNS |
| `docker build` for both images | CI pushing to a registry |
| `kubectl create secret generic leaderboard-db` | External Secrets Operator / Sealed Secrets |
