import os

from channels.routing import ProtocolTypeRouter, URLRouter
from channels.security.websocket import AllowedHostsOriginValidator
from django.core.asgi import get_asgi_application

os.environ.setdefault("DJANGO_SETTINGS_MODULE", "config.settings")

django_asgi_app = get_asgi_application()

from realtime.auth import TokenAuthMiddlewareStack  # noqa: E402
from realtime.routing import websocket_urlpatterns  # noqa: E402

websocket_application = TokenAuthMiddlewareStack(URLRouter(websocket_urlpatterns))
if os.environ.get("ENABLE_WS_ORIGIN_VALIDATION", "false").lower() in {"1", "true", "yes"}:
    websocket_application = AllowedHostsOriginValidator(websocket_application)

application = ProtocolTypeRouter(
    {
        "http": django_asgi_app,
        "websocket": websocket_application,
    }
)
