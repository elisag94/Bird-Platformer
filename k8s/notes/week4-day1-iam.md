# Week 4 Day 1 — AWS account, IAM, per-environment roles

| Symptom | Cause | Fix |
|---|---|---|
| `eks list-clusters` → explicit deny in a service control policy | New-experience account is locked to home region us-east-2 (Canada → Ohio); RegionFloor SCP denies other regions | `aws configure set region us-east-2` |
| IAM user review page: "Permissions summary: No resources" | Skipped "Attach policies directly" | Back → attach AdministratorAccess |
| No "Spend limit" on Billing page | Spend limits are Paid-plan only | Not needed on Free plan; $1 AWS Budgets email instead |
| uat role → prod bucket: `AccessDenied` ... no identity-based policy allows | Expected: role scoped to `bird-platformer-uat*` only | Working as designed — this is the deliverable |

**Two kinds of "no":** explicit deny (an SCP — the landlord) beats every allow; implicit deny = nothing said yes.
**First command on any AccessDenied:** `aws sts get-caller-identity`.