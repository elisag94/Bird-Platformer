"""
The two tables.

Design notes worth being able to defend:

  * duration_ms is an INTEGER, never a float. Floats make ties and sorting
    subtly wrong, and a UI showing 42.31000000001 looks like a bug.

  * Every run is stored, not just each player's best. "Best per player" is a
    query. Discarding history is irreversible; deriving a ranking from history
    is one statement of SQL.

  * The composite index is the leaderboard query written down: filter column
    (level_id) first, sort column (duration_ms) second. That order is what lets
    Postgres satisfy both the WHERE and the ORDER BY from one index.
"""

from datetime import datetime

from sqlalchemy import (
    DateTime,
    ForeignKey,
    Index,
    Integer,
    String,
    func,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


class Base(DeclarativeBase):
    pass


class Player(Base):
    __tablename__ = "players"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)

    # UNIQUE is doing real work here: it is what makes "get or create by name"
    # safe when three replicas handle three submissions at the same instant.
    # The database, not the application, is the arbiter.
    name: Mapped[str] = mapped_column(String(32), nullable=False, unique=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=func.now(), nullable=False
    )

    scores: Mapped[list["Score"]] = relationship(
        back_populates="player", cascade="all, delete-orphan"
    )

    def __repr__(self) -> str:
        return f"<Player id={self.id} name={self.name!r}>"


class Score(Base):
    __tablename__ = "scores"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)

    player_id: Mapped[int] = mapped_column(
        ForeignKey("players.id", ondelete="CASCADE"), nullable=False
    )

    # A plain string rather than its own table. The Unity scene name is the
    # natural identifier and there is exactly one level today; a levels table
    # would be structure without content.
    level_id: Mapped[str] = mapped_column(String(64), nullable=False)

    duration_ms: Mapped[int] = mapped_column(Integer, nullable=False)
    deaths: Mapped[int] = mapped_column(Integer, nullable=False, default=0)

    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=func.now(), nullable=False
    )

    player: Mapped[Player] = relationship(back_populates="scores")

    __table_args__ = (
        Index("ix_scores_level_duration", "level_id", "duration_ms"),
    )

    def __repr__(self) -> str:
        return (
            f"<Score id={self.id} player_id={self.player_id} "
            f"level={self.level_id!r} ms={self.duration_ms}>"
        )