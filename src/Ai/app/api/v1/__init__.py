from fastapi import APIRouter

from app.api.v1 import chat, content, recommendations, search

router = APIRouter()
router.include_router(search.router)
router.include_router(chat.router)
router.include_router(recommendations.router)
router.include_router(content.router)
