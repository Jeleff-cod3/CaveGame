from urllib.parse import parse_qs

from channels.auth import AuthMiddlewareStack
from channels.db import database_sync_to_async
from django.contrib.auth.models import AnonymousUser
from rest_framework.authtoken.models import Token


@database_sync_to_async
def get_user_for_token(token_key: str):
    try:
        token = Token.objects.select_related("user").get(key=token_key)
    except Token.DoesNotExist:
        return AnonymousUser()
    return token.user


class TokenAuthMiddleware:
    """Authenticate Unity WebSockets with ?token=... or Authorization headers."""

    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        scope = dict(scope)
        existing_user = scope.get("user")

        if existing_user is not None and existing_user.is_authenticated:
            return await self.app(scope, receive, send)

        token_key = self._token_from_query(scope) or self._token_from_headers(scope)
        if token_key:
            scope["user"] = await get_user_for_token(token_key)

        return await self.app(scope, receive, send)

    def _token_from_query(self, scope) -> str | None:
        query_string = scope.get("query_string", b"").decode()
        query = parse_qs(query_string)
        values = query.get("token")
        return values[0] if values else None

    def _token_from_headers(self, scope) -> str | None:
        headers = dict(scope.get("headers", []))
        raw_header = headers.get(b"authorization")
        if raw_header is None:
            return None

        header = raw_header.decode()
        for prefix in ("Token ", "Bearer "):
            if header.startswith(prefix):
                return header.removeprefix(prefix).strip()
        return None


def TokenAuthMiddlewareStack(inner):
    return AuthMiddlewareStack(TokenAuthMiddleware(inner))
