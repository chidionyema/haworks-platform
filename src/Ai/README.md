# AI Service (`haworks-ai`)

Python microservice that adds AI capabilities to the Haworks e-commerce platform. It sits alongside the .NET services and connects via RabbitMQ + HTTP.

## What it does

### 1. Semantic Product Search (`POST /api/v1/ai/search`)

Traditional search finds "blue sneakers" when you type "blue sneakers". This finds "comfortable running shoes for rainy days" even if no product has those exact words. It works by converting product descriptions into mathematical vectors (embeddings) and finding the closest matches to the user's query.

**How it works:**
- On startup, fetches all products from `catalog-svc` via HTTP
- Converts each product's name + description + category into a 1024-dimension vector using Anthropic's Voyage-3 embedding model
- Stores vectors in PostgreSQL via pgvector extension
- When a user searches, converts their query into a vector and finds the nearest products by cosine similarity

### 2. AI Chat Assistant (`POST /api/v1/ai/chat/message`)

A conversational shopping assistant. Users ask questions like "what's a good gift under $50?" and get answers grounded in actual product data — not hallucinated products.

**How it works:**
- Takes user's message, searches the vector store for relevant products (RAG pattern)
- Sends the conversation history + relevant products as context to Claude
- Streams the response back as Server-Sent Events (SSE) for real-time typing effect
- Persists conversation history in PostgreSQL so users can continue later

### 3. Product Recommendations (`GET /api/v1/ai/recommendations/{user_id}`)

Suggests products based on what a user has previously purchased. Not collaborative filtering ("users who bought X also bought Y") — instead, it builds a preference vector per user and finds products closest to their taste.

**How it works:**
- Listens for `OrderCompletedEvent` from RabbitMQ (published by orders-svc when a checkout completes)
- Updates the user's preference vector (a running weighted average of purchased product embeddings)
- When recommendations are requested, finds products closest to the preference vector

### 4. Content Generation (`POST /api/v1/ai/content/generate`)

Generates marketing copy on demand: product descriptions, email templates, category blurbs. Useful for sellers listing new products or the platform sending campaigns.

**How it works:**
- Takes a content type + context (product details, audience, etc.)
- Uses Claude with type-specific prompts
- Returns generated text with token usage stats

## How it integrates with the platform

```
┌──────────────┐     HTTP      ┌──────────────┐     HTTP      ┌──────────────┐
│   Browser    │ ──────────── │   BFF Web    │ ──────────── │   AI Service │
│  (frontend)  │              │  (gateway)   │    /api/v1/ai │  (this svc)  │
└──────────────┘              └──────────────┘              └──────┬───────┘
                                                                   │
                              ┌──────────────┐     HTTP            │
                              │ Catalog Svc  │ ◄───────────────────┤ fetch products
                              └──────────────┘                     │
                                                                   │
                              ┌──────────────┐    RabbitMQ         │
                              │  Orders Svc  │ ─────────────────── ┤ OrderCompletedEvent
                              └──────────────┘                     │
                              ┌──────────────┐    RabbitMQ         │
                              │ Catalog Svc  │ ─────────────────── ┤ ProductCacheInvalidatedEvent
                              └──────────────┘                     │
                                                                   │
                              ┌──────────────┐                     │
                              │  PostgreSQL  │ ◄───────────────────┘ pgvector (ai schema)
                              └──────────────┘
```

**RabbitMQ integration:** The .NET services use MassTransit which wraps messages in a JSON envelope. This service deserializes that envelope format using `aio-pika`. It binds to existing MassTransit exchanges with `passive=True` (never creates exchanges — only reads from ones MassTransit created). See `app/messaging/masstransit.py`.

**Database:** Own PostgreSQL database with `ai` schema. Uses pgvector extension for similarity search. Tables: `product_embeddings`, `user_preferences`, `chat_sessions`, `inbox_state` (message dedup).

## Running locally

```bash
cd src/Ai

# Install dependencies (requires Python 3.12+)
pip install uv
uv pip install --system .

# Set required env vars
export AI_DATABASE_URL="postgresql+asyncpg://postgres:postgres@localhost:5432/ai"
export AI_RABBITMQ_URL="amqp://guest:guest@localhost:5672/"
export AI_ANTHROPIC_API_KEY="sk-ant-..."
export AI_CATALOG_BASE_URL="http://localhost:5010"  # catalog-svc port from Aspire

# Create the database and enable pgvector
psql -c "CREATE DATABASE ai;"
psql -d ai -c "CREATE EXTENSION IF NOT EXISTS vector;"

# Run migrations
python -m alembic upgrade head

# Start the service
uvicorn app.main:app --host 0.0.0.0 --port 8080 --reload
```

## Running tests

```bash
cd src/Ai

# Unit tests (no infrastructure needed, Anthropic API is mocked)
uv run pytest tests/unit -v

# Integration tests (needs Docker for Testcontainers PostgreSQL)
uv run pytest tests/integration -v
```

## Deploying to Fly.io

```bash
# Set secrets (one time)
fly secrets set \
  AI_DATABASE_URL="postgresql+asyncpg://user:pass@host:5432/ai" \
  AI_RABBITMQ_URL="amqp://user:pass@rabbitmq:5672/" \
  AI_ANTHROPIC_API_KEY="sk-ant-..." \
  AI_CATALOG_BASE_URL="http://haworks-catalog.internal:8080" \
  -a haworks-ai

# Deploy (runs alembic migrations via release_command, then starts uvicorn)
fly deploy -c src/Ai/fly.ai.toml
```

## Tech stack

| Component | Technology | Why |
|-----------|-----------|-----|
| Web framework | FastAPI | Async, streaming SSE support, auto-generated OpenAPI docs |
| LLM | Claude (Sonnet 4.5 / Opus 4.6) | Fast for chat, powerful for content generation |
| Embeddings | Voyage-3 via Anthropic | High quality, 1024 dimensions |
| Vector store | pgvector on PostgreSQL | Reuses existing Postgres infrastructure, no separate vector DB |
| LLM orchestration | LangChain | RAG chains, conversation memory, model routing |
| Message broker | aio-pika (RabbitMQ) | Consumes MassTransit events from .NET services |
| ORM | SQLAlchemy async | Async Postgres access, Alembic migrations |
| Logging | structlog | Structured JSON logs, matches platform conventions |

## Key files

| File | Purpose |
|------|---------|
| `app/main.py` | FastAPI app, lifespan (starts RabbitMQ workers + catalog sync) |
| `app/settings.py` | All config via `AI_*` env vars |
| `app/messaging/masstransit.py` | MassTransit envelope Pydantic models |
| `app/messaging/consumer_base.py` | Idempotent consumer base (INSERT ON CONFLICT dedup) |
| `app/services/rag_service.py` | pgvector search + embedding |
| `app/services/chat_service.py` | Streaming chat with RAG context |
| `app/services/catalog_sync_service.py` | Fetches catalog, embeds products, upserts to pgvector |
| `app/workers/rabbitmq_worker.py` | RabbitMQ connection lifecycle with reconnect loop |
| `alembic/versions/0001_create_ai_schema.py` | Schema: pgvector extension + all tables |
