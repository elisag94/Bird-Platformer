"""
Schema initialisation. Runs to completion and exits and it does not serve traffic.

This exists because calling create_all() at import time in app.py did not
survive contact with reality. Two replicas, each running two gunicorn workers,
meant 4 processes importing the module simultaneously and four calls to
create_all(). That call is check-then-act ("does the table exist? no? create
it"), and between the check and the create, a sibling won the race. The loser
died with UniqueViolation on players_id_seq.

Two independent guards now:

  1. This script runs in an initContainer, so schema work finishes before any
     app container starts. Ordering becomes explicit instead of emergent.

  2. A Postgres ADVISORY LOCK, because an initContainer runs in every pod and
     two pods would still race. An advisory lock is a mutex the database hands
     out on request — it locks nothing in particular, it just guarantees that
     only one holder of a given key exists at a time. Everyone else waits.
"""

import logging
import os
import sys
import time

from sqlalchemy import text
from sqlalchemy.exc import OperationalError

from db import engine
from models import Base

logging.basicConfig(
    level=os.environ.get("LOG_LEVEL", "INFO").upper(),
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)
log = logging.getLogger("init-db")

# Any fixed 64-bit integer. Every process that
# wants to run migrations must agree on the same number, and nothing else in
# the system should use it.
MIGRATION_LOCK_KEY = 815202601

MAX_ATTEMPTS = 30
RETRY_SECONDS = 2 


def wait_for_database() -> None:
    """
    Poll until Postgres accepts connections.

    Without this the initContainer dies with a stack trace whenever it starts
    fractionally before the database, Kubernetes restarts it, and it works on
    the second try. That is technically self-healing and genuinely horrible to
    read in `kubectl logs`. A polite wait loop turns a traceback into one clear
    line per attempt.
    """
    for attempt in range(1, MAX_ATTEMPTS + 1):
        try:
            with engine.connect() as conn:
                conn.execute(text("SELECT 1"))
            log.info("database reachable after %d attempt(s)", attempt)
            return
        except OperationalError as exc:
            log.info(
                "database not ready (attempt %d/%d): %s",
                attempt,
                MAX_ATTEMPTS,
                exc.__class__.__name__,
            )
            time.sleep(RETRY_SECONDS)

    log.error("database unreachable after %d attempts, giving up", MAX_ATTEMPTS)
    sys.exit(1)


def create_schema() -> None:
    # engine.begin() opens a transaction and commits on clean exit.
    # pg_advisory_xact_lock is the transaction-scoped variant: the lock is
    # released automatically when the transaction ends, including if this
    # process is killed. The session-scoped version (pg_advisory_lock) would
    # need an explicit unlock and can leak on a crash.
    with engine.begin() as conn:
        log.info("acquiring migration lock %d", MIGRATION_LOCK_KEY)
        conn.execute(
            text("SELECT pg_advisory_xact_lock(:key)"), {"key": MIGRATION_LOCK_KEY}
        )
        log.info("lock held; ensuring schema")

        # Bound to `conn`, not `engine`, so the DDL runs inside the same
        # transaction that holds the lock. Postgres DDL is transactional,
        # which is what makes this whole approach work.
        Base.metadata.create_all(conn)

    log.info("schema ready")


if __name__ == "__main__":
    wait_for_database()
    create_schema()