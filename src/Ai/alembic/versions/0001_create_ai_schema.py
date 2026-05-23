"""Create ai schema and tables.

Revision ID: 0001
Revises:
Create Date: 2026-05-22 00:00:00.000000
"""
from __future__ import annotations

from alembic import op
import sqlalchemy as sa
from pgvector.sqlalchemy import Vector

revision: str = "0001"
down_revision: str | None = None
branch_labels: tuple[str, ...] | None = None
depends_on: str | None = None


def upgrade() -> None:
    op.execute("CREATE SCHEMA IF NOT EXISTS ai")
    op.execute("CREATE EXTENSION IF NOT EXISTS vector")

    op.create_table(
        "product_embeddings",
        sa.Column("id", sa.Uuid(), server_default=sa.text("gen_random_uuid()"), nullable=False),
        sa.Column("product_id", sa.Uuid(), nullable=False),
        sa.Column("name", sa.String(500), nullable=False),
        sa.Column("description", sa.Text(), nullable=False, server_default=""),
        sa.Column("unit_price", sa.Numeric(18, 2), nullable=False, server_default="0"),
        sa.Column("category_id", sa.Uuid(), nullable=True),
        sa.Column("category_name", sa.String(200), nullable=False, server_default=""),
        sa.Column("is_listed", sa.Boolean(), nullable=False, server_default=sa.text("true")),
        sa.Column("is_in_stock", sa.Boolean(), nullable=False, server_default=sa.text("true")),
        sa.Column("source_version", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("embedding", Vector(1024), nullable=True),
        sa.Column("embedded_at", sa.DateTime(timezone=True), nullable=True),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("product_id", name="uq_product_embeddings_product_id"),
        schema="ai",
    )

    op.create_table(
        "user_preferences",
        sa.Column("id", sa.Uuid(), server_default=sa.text("gen_random_uuid()"), nullable=False),
        sa.Column("customer_id", sa.String(200), nullable=False),
        sa.Column("preference_vector", Vector(1024), nullable=True),
        sa.Column("order_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("total_spent", sa.Numeric(18, 2), nullable=False, server_default="0"),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.text("now()")),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("customer_id", name="uq_user_preferences_customer_id"),
        schema="ai",
    )

    op.create_table(
        "chat_sessions",
        sa.Column("id", sa.Uuid(), server_default=sa.text("gen_random_uuid()"), nullable=False),
        sa.Column("session_id", sa.String(200), nullable=False),
        sa.Column("user_id", sa.String(200), nullable=True),
        sa.Column("messages", sa.dialects.postgresql.JSONB(), nullable=False, server_default="[]"),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.text("now()")),
        sa.Column("updated_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.text("now()")),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("session_id", name="uq_chat_sessions_session_id"),
        schema="ai",
    )

    op.create_table(
        "inbox_state",
        sa.Column("message_id", sa.Uuid(), nullable=False),
        sa.Column("processed_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.text("now()")),
        sa.PrimaryKeyConstraint("message_id"),
        schema="ai",
    )

    op.create_index(
        "ix_product_embeddings_embedding",
        "product_embeddings",
        ["embedding"],
        schema="ai",
        postgresql_using="ivfflat",
        postgresql_with={"lists": 100},
        postgresql_ops={"embedding": "vector_cosine_ops"},
    )


def downgrade() -> None:
    op.drop_index("ix_product_embeddings_embedding", table_name="product_embeddings", schema="ai")
    op.drop_table("inbox_state", schema="ai")
    op.drop_table("chat_sessions", schema="ai")
    op.drop_table("user_preferences", schema="ai")
    op.drop_table("product_embeddings", schema="ai")
    op.execute("DROP SCHEMA IF EXISTS ai CASCADE")
