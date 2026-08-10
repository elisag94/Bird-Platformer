# Week 2, Day 2 — Secrets

**Date:** 2026-08-04
**Format:** hands-on first, docs second

---

## Mental model

A ConfigMap is a sticky note on the fridge. A Secret is the same note in an envelope
marked *private*. The envelope changes who bothers to look and how the cluster handles
it — it does **not** change the fact that anyone who opens it can read the note.

---

## What I ran

### Drill 1 — create and inspect

```bash
kubectl create secret generic user \
  --from-literal=username=user1 \
  --from-literal=password=1234

kubectl get secret user -o yaml
kubectl describe secret user
```

Mistakes made and corrected:

- First attempt made **two Secrets with one key each**. The Secret is the envelope;
  the keys are the notes inside. One envelope per *thing being accessed*, not per field.
- Second attempt used a comma: `--from-literal=username=user1,password=1234`.
  Everything after the first `=` is value, commas included. The flag **repeats** —
  that's the mechanism. Tell-tale sign: `DATA 1` instead of `DATA 2`, and a key
  whose byte count matched the whole comma-joined string.

**Key finding:** `describe` prints `password: 4 bytes` and refuses the value;
`get -o yaml` hands over `MTIzNA==` with no complaint. Same object, same permissions.
`describe` is being polite about shoulder-surfing, **not** enforcing anything.
"kubectl describe hides it" is a comfortable half-truth.

Also: byte counts leak password *length* even when the value is hidden.

### Drill 2 — decode it

```bash
kubectl get secret user -o jsonpath='{.data.password}' | base64 -d
```

One command, no special credentials, plaintext out. **Encoding is not encryption.**

No trailing newline in the output — that's correct behaviour, and it sets up a trap:
`--from-literal` stores exactly what you type, but `--from-file` stores the file's
**trailing newline too**. A password becomes `1234\n`, `describe` looks perfectly
normal, and the database rejects the login. Check with:

```bash
kubectl get secret <name> -o jsonpath='{.data.password}' | base64 -d | xxd | tail -1
```

A trailing `0a` is the smoking gun.

### Drill 3 — inject two ways

**Env vars** — `envFrom.secretRef` pulls every key in, named after the keys.
`env[].valueFrom.secretKeyRef` pulls one key and lets you rename it (needed when the
app expects `POSTGRES_PASSWORD` but the key is `password`).

Watch the nesting: `name` appears at two levels meaning different things — the outer
is the env var name, the inner is the Secret's name.

**Volume** — mirrors the ConfigMap mount already in `deployment.yaml`, with two changes:
`configMap:` → `secret:`, and the field inside changes from `name:` to **`secretName:`**.
That inconsistency is real and it catches everyone.

One file per key, named after the key, containing **plaintext** — the kubelet decodes
on the way in, so the app just reads a file.

`ls -la` on the mount shows `..data` and a timestamped directory, with the "files" as
symlinks. That's the atomic-update mechanism: kubelet writes a new directory and flips
one symlink, so a reader never catches half-updated credentials.

**The important contrast:**

| Injection method | Picks up a changed Secret? |
|---|---|
| Env vars | **No** — snapshot at container start, needs a restart |
| Mounted volume | **Yes** — updates live, no restart (except `subPath` mounts) |

Config that rotates belongs in files. And `subPath` — which the nginx `default.conf`
mount uses — is precisely the style that *doesn't* receive updates.

Other notes: mounted files default to world-readable `0644`; use `defaultMode: 0400`
for real credentials. Env vars leak more than files do — crash handlers, debug
endpoints, `ps e`, inherited child processes.

### Drill 4 — the ServiceAccount token

Every pod is issued a staff badge at the door whether or not it has any reason to open
a locked room. The game pods have worn one since Week 1.

```bash
kubectl exec -it <pod> -- sh
ls -la /var/run/secrets/kubernetes.io/serviceaccount/

TOKEN=$(cat /var/run/secrets/kubernetes.io/serviceaccount/token)
wget -q -O- --no-check-certificate \
  --header="Authorization: Bearer $TOKEN" \
  https://kubernetes.default.svc/api/v1/namespaces/default/pods
```

Result: **HTTP 403 Forbidden** — and that's the whole lesson.

- **401** = "I don't know who you are" — credential missing, malformed, or expired.
- **403** = "I know exactly who you are, and you may not do that."

The badge scanned fine. The API server authenticated it as
`system:serviceaccount:default:default`, checked RBAC, found no matching rule.
**Authentication succeeded; authorization failed.**

Nothing in `deployment.yaml` mounted this — the ServiceAccount admission controller
mutates the pod spec on the way in. The gap between "what I wrote" and "what the
cluster stored" applies to every object submitted.

Modern tokens are **bound**: ~1h expiry, kubelet-rotated, audience-scoped, and tied to
the specific pod (its name and UID are *inside* the JWT). The old behaviour was a
non-expiring token in an auto-created Secret — steal once, use forever.

Useful tool discovered: `kubectl auth can-i list pods --as=system:serviceaccount:default:default`

**Action taken:** nginx serves static files and never calls the API, so the badge is
pure attack surface. Added `automountServiceAccountToken: false` to the pod spec in
`k8s/deployment.yaml`, applied, `rollout restart`, confirmed the mount is gone.

### Drill 5 — break it on purpose

**Accidental finding, worth keeping:** a volume declared under `spec.volumes` with no
matching `volumeMounts` in the container is **inert**. The bad key was never noticed
and the pod ran happily. The filing cabinet was delivered to the lobby and nobody
carried it into the office. "I added the volume and nothing happened" — check the mount.

Also: config failures happen *before* the container starts, so `kubectl logs` is empty
and useless. The explanation lives in `kubectl describe pod` Events and
`kubectl get events`.

---

## Symptom → cause → fix log

| Symptom | Cause | Fix |
|---|---|---|
| `DATA 1` when two keys expected; byte count matches whole string | Comma used instead of repeating `--from-literal` | Repeat the flag |
| Secret value visible via `get -o yaml` but not `describe` | `describe` redacts by convention, not by permission | Don't rely on it as a control |
| DB rejects a password that looks correct | `--from-file` captured a trailing newline | `base64 -d \| xxd`, look for trailing `0a` |
| Rotated Secret not taking effect | Injected as env vars — snapshot at start | Mount as volume, or `rollout restart` |
| Mounted Secret not updating despite volume mount | `subPath` mount | Mount the directory, not the single file |
| Pod Running despite bad key in volume | Volume declared but never mounted | Add `volumeMounts` to the container |
| HTTP 403 from API server with a valid token | Authenticated but not authorized (RBAC) | Not a credential problem — check roles |
| Pod won't start, `kubectl logs` empty | Config failure precedes container start | Read `describe pod` Events |

---

## Self-check answers

**Q: If Secrets are only base64-encoded in etcd, what makes them safer than ConfigMaps?**

Base64 makes them no safer *at all*. What helps is that being a **separate resource
type** unlocks separate handling:

- RBAC is per-resource-type — grant `get`/`list` on configmaps without granting secrets
- The kubelet only ships a Secret to nodes actually running a pod that needs it, and
  keeps it in tmpfs (memory) rather than node disk
- Encryption at rest via `EncryptionConfiguration` — the only thing that genuinely
  changes the etcd story, and it's **off by default**
- Redaction by convention in tooling, logs, and events

**Q: Who can read a Secret in a default cluster?**

Anyone with `get`/`list` on secrets in that namespace, cluster-admins, and anyone who
can read etcd or a cluster backup.

**The bigger hole:** anyone who can **create a pod** in a namespace can read every
Secret in that namespace — just mount it and `cat` the file. No `get secrets`
permission required. *Pod-create is effectively secret-read.* This is a large part of
why namespace boundaries matter.

**Q: Why is committing a Secret manifest to git a real problem if it's "encoded"?**

`base64 -d` — one command. But the deeper reason is **permanence**: git history is
immutable in practice. Deleting the file later doesn't remove it from clones, forks,
CI caches, or laptops. Public repos are scraped by bots within seconds.

The only real remedy is **rotation** — once committed, treat it as compromised forever.
Sealed Secrets and SOPS exist so manifests *can* be committed safely: encrypted in git,
decrypted only in-cluster.

---

## Open items

- [ ] **Task 8** — encryption at rest + external secret stores (vocabulary only, no setup):
  - `EncryptionConfiguration`, off by default, does **not** retroactively encrypt
    existing Secrets
  - External stores: Vault, AWS Secrets Manager, GCP Secret Manager, Azure Key Vault
  - Two connecting patterns: External Secrets Operator (syncs *into* a k8s Secret) vs
    Secrets Store CSI Driver (mounts directly, creates no Secret object)
  - GitOps-safe: Sealed Secrets, SOPS
- [ ] **Drill 5, Case B** — Secret doesn't exist at all (`secretName: nope`); record
  STATUS and Event reason
- [ ] **Drill 5, Case C** — same as B but with `optional: true`; predict before applying
- [ ] **Stretch** — find the Secret type for private registry creds and the pod spec
  field that references it (not under `volumes` or `env`). Needed once CI pushes images
  to a private registry.
- [ ] Redo Q1 in my own words without looking

## Where this lands next

- **Day 7 (leaderboard)** — Postgres credentials in a Secret, `secretKeyRef` renaming
  keys to `POSTGRES_USER` / `POSTGRES_PASSWORD`, and `stringData` for hand-written
  manifests
- **Day 5 / Week 3 (Ingress)** — TLS cert private key as a `kubernetes.io/tls` Secret
- **Week 3 (RBAC)** — the 403 from Drill 4 is the starting point
- **CI** — `kubernetes.io/dockerconfigjson` + `imagePullSecrets` once images go to a
  private registry
