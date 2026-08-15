"""
Bird Platformer leaderboard API.

Four endpoints, deliberately:

    GET  /healthz                 liveness  — is the process alive?
    GET  /readyz                  readiness — can it actually serve?
    POST /api/scores              submit a finished run
    GET  /api/scores/top          the leaderboard

The /api prefix is part of the route, not stripped by the Ingress. A request to
http://bird.local/api/scores arrives here still reading /api/scores, because no
rewrite-target annotation is set. The front desk forwards the envelope; it does
not retype it.

/healthz and /readyz sit OUTSIDE /api on purpose. Health probes never travel
through the Ingress or the Service — the kubelet dials the pod IP directly — so
they do not need to match any routing rule.
"""

import logging
import os
import re

from flask import Flask, jsonify, request
from sqlalchemy import select, text
from sqlalchemy.exc import IntegrityError, SQLAlchemyError

from db import SessionLocal, engine
from models import Player, Score

# --------------------------------------------------------------------------
# Configuration (ConfigMap in Kubernetes, plain env vars locally)
# --------------------------------------------------------------------------

LOG_LEVEL = os.environ.get("LOG_LEVEL", "INFO").upper()

# Bounds validation. The client holds the stopwatch, so these are the only
# thing standing between the leaderboard and someone curling a 1ms run. They
# are a stopgap, not a fix — the real answer is a server-issued run token so
# elapsed time comes from the server's own clock.
MIN_RUN_MS = int(os.environ.get("MIN_RUN_MS", "3000"))
MAX_RUN_MS = int(os.environ.get("MAX_RUN_MS", "600000"))

MAX_NAME_LENGTH = 32
MAX_LEVEL_LENGTH = 64
DEFAULT_LIMIT = 10
MAX_LIMIT = 50

# Letters, digits, space, underscore, hyphen. Everything else is rejected
# rather than sanitised — silently altering a player's name is worse than
# telling them it was invalid.
NAME_PATTERN = re.compile(r"^[\w \-]+$", re.UNICODE)

logging.basicConfig(
    level=LOG_LEVEL,
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)
log = logging.getLogger("leaderboard")

app = Flask(__name__)


class ValidationError(Exception):
    """Raised for bad client input. Becomes a 400."""


# --------------------------------------------------------------------------
# Validation helpers
# --------------------------------------------------------------------------


def clean_player_name(raw) -> str:
    if not isinstance(raw, str):
        raise ValidationError("player_name must be a string")
    name = raw.strip()
    if not name:
        raise ValidationError("player_name must not be empty")
    if len(name) > MAX_NAME_LENGTH:
        raise ValidationError(
            f"player_name must be {MAX_NAME_LENGTH} characters or fewer"
        )
    if not NAME_PATTERN.match(name):
        raise ValidationError(
            "player_name may contain only letters, digits, spaces, "
            "underscores and hyphens"
        )
    return name


def clean_level_id(raw) -> str:
    if not isinstance(raw, str):
        raise ValidationError("level_id must be a string")
    level = raw.strip()
    if not level:
        raise ValidationError("level_id must not be empty")
    if len(level) > MAX_LEVEL_LENGTH:
        raise ValidationError(
            f"level_id must be {MAX_LEVEL_LENGTH} characters or fewer"
        )
    return level


def clean_duration_ms(raw) -> int:
    # bool is a subclass of int in Python, so True would sneak through a naive
    # isinstance check. Excluding it explicitly is a small but real bug avoided.
    if isinstance(raw, bool) or not isinstance(raw, int):
        raise ValidationError("duration_ms must be an integer number of milliseconds")
    if raw < MIN_RUN_MS:
        raise ValidationError(f"duration_ms must be at least {MIN_RUN_MS}")
    if raw > MAX_RUN_MS:
        raise ValidationError(f"duration_ms must be at most {MAX_RUN_MS}")
    return raw


def clean_deaths(raw) -> int:
    if raw is None:
        return 0
    if isinstance(raw, bool) or not isinstance(raw, int):
        raise ValidationError("deaths must be an integer")
    if raw < 0 or raw > 9999:
        raise ValidationError("deaths must be between 0 and 9999")
    return raw


def clean_limit(raw) -> int:
    if raw is None:
        return DEFAULT_LIMIT
    try:
        value = int(raw)
    except (TypeError, ValueError):
        raise ValidationError("limit must be an integer")
    if value < 1 or value > MAX_LIMIT:
        raise ValidationError(f"limit must be between 1 and {MAX_LIMIT}")
    return value


# --------------------------------------------------------------------------
# SQL
# --------------------------------------------------------------------------

# DISTINCT ON is Postgres-specific and exactly right here: it keeps the first
# row per player_id under the given ORDER BY, which — because we sort by
# duration ascending — is that player's best run. The outer query then ranks
# those bests against each other.
TOP_SCORES_SQL = text(
    """
    WITH best AS (
        SELECT DISTINCT ON (s.player_id)
               s.player_id, s.duration_ms, s.deaths, s.created_at
        FROM scores s
        WHERE s.level_id = :level_id
        ORDER BY s.player_id, s.duration_ms ASC, s.created_at ASC
    )
    SELECT p.name, b.duration_ms, b.deaths, b.created_at
    FROM best b
    JOIN players p ON p.id = b.player_id
    ORDER BY b.duration_ms ASC, b.created_at ASC
    LIMIT :limit
    """
)

# Rank = how many players have a strictly better personal best, plus one.
# Ties therefore share a rank, which is the behaviour people expect from a
# leaderboard.
RANK_SQL = text(
    """
    WITH best AS (
        SELECT player_id, MIN(duration_ms) AS best_ms
        FROM scores
        WHERE level_id = :level_id
        GROUP BY player_id
    )
    SELECT COUNT(*) + 1
    FROM best
    WHERE best_ms < :duration_ms
    """
)

PREVIOUS_BEST_SQL = text(
    """
    SELECT MIN(duration_ms)
    FROM scores
    WHERE level_id = :level_id AND player_id = :player_id
    """
)


def get_or_create_player(session, name: str) -> Player:
    """
    Find a player by name, creating one if absent.

    The retry matters. With three replicas, two submissions from a new player
    can both see "no such player" and both try to INSERT. The UNIQUE constraint
    means one of them loses with an IntegrityError — that is the database doing
    its job, not a bug. Roll back and re-read; the row now exists.
    """
    player = session.scalar(select(Player).where(Player.name == name))
    if player is not None:
        return player

    player = Player(name=name)
    session.add(player)
    try:
        session.flush()
    except IntegrityError:
        session.rollback()
        player = session.scalar(select(Player).where(Player.name == name))
        if player is None:
            raise
    return player


# --------------------------------------------------------------------------
# Health endpoints
# --------------------------------------------------------------------------


@app.get("/healthz")
def healthz():
    """
    Liveness. Deliberately does NOT touch the database.

    If Postgres goes down, this process is still perfectly alive and restarting
    it will not help. Wiring a DB check in here would mean every replica fails
    liveness at the same moment and Kubernetes restarts the whole deployment —
    turning one outage into two.
    """
    return jsonify(status="ok"), 200


@app.get("/readyz")
def readyz():
    """
    Readiness. Answers a different question: can this pod actually serve a
    request right now? If not, take it off the Service's endpoint list —
    but do not restart it.
    """
    try:
        with engine.connect() as conn:
            conn.execute(text("SELECT 1"))
        return jsonify(status="ready", database="up"), 200
    except SQLAlchemyError as exc:
        log.warning("readiness check failed: %s", exc)
        return jsonify(status="not-ready", database="down"), 503


# --------------------------------------------------------------------------
# API endpoints
# --------------------------------------------------------------------------


@app.post("/api/scores")
def submit_score():
    payload = request.get_json(silent=True)
    if not isinstance(payload, dict):
        raise ValidationError("request body must be a JSON object")

    name = clean_player_name(payload.get("player_name"))
    level_id = clean_level_id(payload.get("level_id"))
    duration_ms = clean_duration_ms(payload.get("duration_ms"))
    deaths = clean_deaths(payload.get("deaths"))

    with SessionLocal() as session:
        player = get_or_create_player(session, name)

        previous_best = session.scalar(
            PREVIOUS_BEST_SQL, {"level_id": level_id, "player_id": player.id}
        )

        score = Score(
            player_id=player.id,
            level_id=level_id,
            duration_ms=duration_ms,
            deaths=deaths,
        )
        session.add(score)
        session.commit()

        best_ms = duration_ms if previous_best is None else min(previous_best, duration_ms)
        rank = session.scalar(
            RANK_SQL, {"level_id": level_id, "duration_ms": best_ms}
        )

        personal_best = previous_best is None or duration_ms < previous_best

        log.info(
            "score accepted player=%s level=%s ms=%d rank=%s pb=%s",
            name,
            level_id,
            duration_ms,
            rank,
            personal_best,
        )

        return (
            jsonify(
                id=score.id,
                player_name=player.name,
                level_id=level_id,
                duration_ms=duration_ms,
                deaths=deaths,
                rank=rank,
                personal_best=personal_best,
            ),
            201,
        )


@app.get("/api/scores/top")
def top_scores():
    level_id = clean_level_id(request.args.get("level_id", "Level01"))
    limit = clean_limit(request.args.get("limit"))

    with SessionLocal() as session:
        rows = session.execute(
            TOP_SCORES_SQL, {"level_id": level_id, "limit": limit}
        ).all()

    entries = [
        {
            "rank": index,
            "player_name": row.name,
            "duration_ms": row.duration_ms,
            "deaths": row.deaths,
            "achieved_at": row.created_at.isoformat(),
        }
        for index, row in enumerate(rows, start=1)
    ]

    return jsonify(level_id=level_id, count=len(entries), entries=entries), 200


# --------------------------------------------------------------------------
# Error handling
# --------------------------------------------------------------------------


@app.errorhandler(ValidationError)
def handle_validation_error(exc: ValidationError):
    return jsonify(error="bad_request", detail=str(exc)), 400


@app.errorhandler(SQLAlchemyError)
def handle_database_error(exc: SQLAlchemyError):
    # Log the detail, return a generic message. Database errors often contain
    # table names, column names and occasionally connection strings — none of
    # which belongs in a response to the internet.
    log.exception("database error")
    return jsonify(error="database_unavailable"), 503


@app.errorhandler(404)
def handle_not_found(_exc):
    return jsonify(error="not_found"), 404


# --------------------------------------------------------------------------
# Startup
# --------------------------------------------------------------------------
#
# No more schema creation here.
#
# create_all() used to run at import time. With two replicas each running two
# gunicorn workers, that meant four processes racing to create the same tables.
# Schema is now owned by init_db.py, run once by an initContainer before 
# this process starts.


if __name__ == "__main__":
    # Local convenience only. In the container, gunicorn imports `app` from
    # this module — see the Dockerfile CMD. Running locally, execute
    # `python init_db.py` once first.
    app.run(host="0.0.0.0", port=8080, debug=True)