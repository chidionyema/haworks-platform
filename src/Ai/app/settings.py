from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    model_config = {"env_prefix": "AI_", "env_file": ".env", "extra": "ignore"}

    database_url: str = "postgresql+asyncpg://postgres:postgres@localhost:5432/ai"
    rabbitmq_url: str = "amqp://guest:guest@localhost:5672/"
    anthropic_api_key: str = ""
    llm_model_fast: str = "claude-sonnet-4-5"
    llm_model_reasoning: str = "claude-opus-4-6"
    catalog_base_url: str = "http://haworks-catalog.internal:8080"
    embedding_model: str = "voyage-3"
    embedding_dimensions: int = 1024
    catalog_sync_on_startup: bool = True
    catalog_sync_interval_seconds: int = 3600
    otel_endpoint: str = ""
    service_name: str = "ai-svc"

    def validate_required(self) -> None:
        """Fail fast on startup if critical secrets are missing."""
        if not self.anthropic_api_key:
            raise RuntimeError(
                "AI_ANTHROPIC_API_KEY is required. "
                "Set via environment variable or fly secrets set AI_ANTHROPIC_API_KEY=..."
            )
        if "localhost" in self.database_url and self.service_name != "ai-svc-test":
            import structlog
            structlog.get_logger().warning(
                "database_url_is_localhost",
                hint="Set AI_DATABASE_URL for production",
            )
