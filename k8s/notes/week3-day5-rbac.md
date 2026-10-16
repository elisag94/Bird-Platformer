| Symptom | Cause | Fix |
|---|---|---|
can-i says no after creating a Role	| A Role on its own grants nothing	| Bind it with a RoleBinding
A badge can act on bird-dev but not on bird-platformer | Role and RoleBinding are namespaced |	That is the intended boundary between environments
Forbidden ... cannot delete resource "pods" in API group ""	| The verb isn’t on the badge’s card | Read who / what / group / where from the error
bash: pod-name: No such file or directory | < is shell input redirection | Replace placeholders before running