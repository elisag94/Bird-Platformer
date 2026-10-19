| Symptom | Cause | Fix |
|---|---|---|
can-i says no after creating a Role	| A Role on its own grants nothing	| Bind it with a RoleBinding
A badge can act on bird-dev but not on bird-platformer | Role and RoleBinding are namespaced |	That is the intended boundary between environments
Forbidden ... cannot delete resource "pods" in API group ""	| The verb isn’t on the badge’s card | Read who / what / group / where from the error
bash: pod-name: No such file or directory | < is shell input redirection | Replace placeholders before running


## Break-things drill (19 Oct 2026, `bird-dev`)

Diagnosis order: `describe` → `logs --previous` → `get events`

| # | Symptom | Cause | Fix |
|---|---|---|---|
| 1 | `ErrImagePull` → `ImagePullBackOff`; old pod still Running | Tag `v99` doesn't exist. "may require docker login" is misleading | `rollout undo`. Check the tag before credentials |
| 2 | `CreateContainerConfigError`; `logs` returns BadRequest; Restart Count 0 | Env var references key `game-mode`, which isn't in the ConfigMap | `rollout undo`, or add the key. Container never started, so use `describe`, not `logs` |
| 3 | Pod Running, Service has an IP, no events, but curl fails | Selector `app=bird-platformr` matches no pod, so `Endpoints:` is blank | Fix the selector. Service broken → check Endpoints first |
| 4 | `CrashLoopBackOff`; `Last State: OOMKilled`, `Exit Code: 137`, Started = Finished | 6Mi limit is below nginx's ~7Mi need. Requests had to drop too (request ≤ limit) | `rollout undo`. OOM appears in `Last State`, not Events |
| 5 | Restart every ~30s; logs look healthy; shutdown via SIGQUIT, code 0 | Liveness probe hits `/does-not-exist` → `kube-probe` gets 404 ×3 | `rollout undo`. Grep logs for `kube-probe` and its status code |
| 6 | No error at all; change "missing" in dev | Default namespace was `bird-platformer`; command without `-n` hit prod | `kubens` + `kube-ps1` prompt; always `-n` on destructive commands |
| — | `describe pod` → NotFound for a pod I could see | Forgot `-n bird-dev` (break #6 by accident) | Same habit as #6 |