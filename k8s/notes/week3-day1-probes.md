## Symptom → cause → fix log

| Symptom | Cause |
|---|---|
|wget localhost refused inside nginx pod | listen 80 binds IPv4 only; BusyBox tried ::1
|busybox pod → game pod timed out | default-deny NetworkPolicy; unlabelled pod dropped silently
|leaderboard 0/1, RESTARTS unchanged, Postgres down	| readiness checks the DB, liveness doesn't — by design
|game READY 1/1, RESTARTS climbing, Exit Code 0 | liveness 404; nginx handled SIGTERM cleanly so no 137
