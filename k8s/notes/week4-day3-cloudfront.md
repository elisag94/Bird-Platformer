| Symptom | Cause | Fix |
|---|---|---|
| Old s3-website URL still served the game after CloudFront went live | OAC *added* CloudFront's key; yesterday's `"Principal": "*"` statement was still in the bucket policy | Delete the public statement, turn on Block Public Access, disable static website hosting |
| Old URL: 403 → then 404 | 403 = website exists, access denied; 404 = website hosting turned off entirely | Expected — both mean the public door is closed |
| Deploy script stopped at `(END)` | AWS CLI v2 opens the `less` pager by default | `export AWS_PAGER=""` — required before CI runs it |
| New cache policy had no effect | Policy created but `Distributions: None` — not attached to a behavior | Attach to the distribution's behavior |
| UAT `index.html` could be stale for 24 h | Recommended cache policy = 1 day; script sets no Cache-Control | UAT: `bird-uat-60s` policy; prod: invalidate `/*` on every deploy |

**Key facts**
- Behaviors: first match by **precedence order** wins (Ingress: longest path wins)
- Invalidation is **per distribution** — two envs, two invalidations
- `x-cache: Miss` → `Hit` (age N) → `Miss` again after invalidation
- WAF skipped: static site, no forms/auth — first thing I'd add with an API or login