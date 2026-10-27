# Week 4 Day 2 — S3 static hosting

| Symptom | Cause | Fix |
|---|---|---|
| Unity: "Unable to parse ...framework.js.gz" | S3 guessed Content-Type from extension but set no `Content-Encoding` | Upload `.gz` files with `--content-encoding gzip` + correct type |
| `.data.gz` served as `binary/octet-stream` | S3's fallback type when it can't guess | Set `application/octet-stream` explicitly |
| Plan's fix still broken | Only covered `.wasm.gz` and `.data.gz` | Third rule for `.framework.js.gz` → `application/javascript` |
| Network tab empty | Devtools only records while open | Open devtools, then Cmd+Shift+R |
| AWS write commands print nothing | Silence = success | Verify with `get-bucket-website` / `get-bucket-policy-status` |

**Same bug as Week 3 Day 2, different place:** the cache/encoding *policy* is the durable decision; nginx config and S3 object metadata are two expressions of it.