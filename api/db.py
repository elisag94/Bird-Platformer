"""
Database engine and session factory.

Everything here comes from environment variables — nothing is baked into the
image. In Kubernetes those variables arrive from two different places on
purpose:

    ConfigMap  ->  DB_HOST, DB_PORT, DB_NAME     (not secret, useful to see)
    Secret     ->  DB_USER, DB_PASSWORD          (would be embarrassing on screen)

Note the asymmetry in how they are read. DB_USER and DB_PASSWORD use
os.environ[...], which raises KeyError and kills the process at import time if
the variable is missing. That is deliberate: a pod that dies immediately with a
clear error is far easier to debug than one that boots, looks healthy, and then
fails on the first request. Fail fast, fail loud.
"""

import os
from urllib.parse import quote_plus

from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker


def build_database_url() -> str:
    # Required. Missing -> KeyError -> container exits -> kubectl describe pod
    # shows you exactly which variable was not supplied.
    user = os.environ["DB_USER"]
    password = os.environ["DB_PASSWORD"]

    # Optional with sane defaults. "postgres" is the in-cluster Service name;
    # it only resolves inside the cluster.
    host = os.environ.get("DB_HOST", "postgres")
    port = os.environ.get("DB_PORT", "5432")
    name = os.environ.get("DB_NAME", "birdscores")

    # quote_plus because a generated password may contain @ / : # and friends,
    # any of which would corrupt the URL structure.
    return (
        f"postgresql+psycopg2://{quote_plus(user)}:{quote_plus(password)}"
        f"@{host}:{port}/{name}"
    )


# pool_pre_ping issues a cheap SELECT 1 before handing out a pooled connection.
# Without it, a Postgres pod restart leaves stale connections in the pool and
# the next few requests fail with confusing "server closed the connection"
# errors. This one flag prevents a whole class of flaky behaviour.
engine = create_engine(
    build_database_url(),
    pool_pre_ping=True,
    pool_size=5,
    max_overflow=5,
    echo=os.environ.get("SQL_ECHO", "").lower() == "true",
)

SessionLocal = sessionmaker(bind=engine, expire_on_commit=False)