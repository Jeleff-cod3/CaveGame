from pathlib import Path
from urllib.parse import urlparse
import json
import os


BASE_DIR = Path(__file__).resolve().parent.parent

SECRET_KEY = os.environ.get("DJANGO_SECRET_KEY", "dev-only-change-me")
DEBUG = os.environ.get("DJANGO_DEBUG", "true").lower() in {"1", "true", "yes"}

allowed_hosts = os.environ.get("DJANGO_ALLOWED_HOSTS", "localhost,127.0.0.1,*")
ALLOWED_HOSTS = [host.strip() for host in allowed_hosts.split(",") if host.strip()]


INSTALLED_APPS = [
    "daphne",
    "django.contrib.admin",
    "django.contrib.auth",
    "django.contrib.contenttypes",
    "django.contrib.sessions",
    "django.contrib.messages",
    "django.contrib.staticfiles",
    "rest_framework",
    "rest_framework.authtoken",
    "channels",
    "accounts",
    "lobbies",
    "realtime",
]

MIDDLEWARE = [
    "django.middleware.security.SecurityMiddleware",
    "django.contrib.sessions.middleware.SessionMiddleware",
    "django.middleware.common.CommonMiddleware",
    "django.middleware.csrf.CsrfViewMiddleware",
    "django.contrib.auth.middleware.AuthenticationMiddleware",
    "django.contrib.messages.middleware.MessageMiddleware",
    "django.middleware.clickjacking.XFrameOptionsMiddleware",
]

ROOT_URLCONF = "config.urls"

TEMPLATES = [
    {
        "BACKEND": "django.template.backends.django.DjangoTemplates",
        "DIRS": [],
        "APP_DIRS": True,
        "OPTIONS": {
            "context_processors": [
                "django.template.context_processors.request",
                "django.contrib.auth.context_processors.auth",
                "django.contrib.messages.context_processors.messages",
            ],
        },
    },
]

WSGI_APPLICATION = "config.wsgi.application"
ASGI_APPLICATION = "config.asgi.application"


def database_from_url(url: str) -> dict[str, object]:
    parsed = urlparse(url)
    engine = {
        "postgres": "django.db.backends.postgresql",
        "postgresql": "django.db.backends.postgresql",
    }.get(parsed.scheme)

    if engine is None:
        raise ValueError(f"Unsupported DATABASE_URL scheme: {parsed.scheme}")

    return {
        "ENGINE": engine,
        "NAME": parsed.path.lstrip("/"),
        "USER": parsed.username or "",
        "PASSWORD": parsed.password or "",
        "HOST": parsed.hostname or "",
        "PORT": str(parsed.port or ""),
    }


def json_object_from_env(var_name: str) -> dict[str, object]:
    raw = os.environ.get(var_name, "").strip()
    if not raw:
        return {}

    try:
        parsed = json.loads(raw)
    except json.JSONDecodeError:
        return {}

    return parsed if isinstance(parsed, dict) else {}


def env_bool(var_name: str, default: bool) -> bool:
    raw = os.environ.get(var_name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


def env_int(var_name: str, default: int) -> int:
    raw = os.environ.get(var_name)
    if raw is None:
        return default
    try:
        return int(raw)
    except ValueError:
        return default


if os.environ.get("DATABASE_URL"):
    DATABASES = {"default": database_from_url(os.environ["DATABASE_URL"])}
elif os.environ.get("POSTGRES_DB"):
    DATABASES = {
        "default": {
            "ENGINE": "django.db.backends.postgresql",
            "NAME": os.environ["POSTGRES_DB"],
            "USER": os.environ.get("POSTGRES_USER", "postgres"),
            "PASSWORD": os.environ.get("POSTGRES_PASSWORD", ""),
            "HOST": os.environ.get("POSTGRES_HOST", "localhost"),
            "PORT": os.environ.get("POSTGRES_PORT", "5432"),
        }
    }
else:
    DATABASES = {
        "default": {
            "ENGINE": "django.db.backends.sqlite3",
            "NAME": BASE_DIR / "db.sqlite3",
        }
    }


AUTH_PASSWORD_VALIDATORS = [
    {"NAME": "django.contrib.auth.password_validation.UserAttributeSimilarityValidator"},
    {"NAME": "django.contrib.auth.password_validation.MinimumLengthValidator"},
    {"NAME": "django.contrib.auth.password_validation.CommonPasswordValidator"},
    {"NAME": "django.contrib.auth.password_validation.NumericPasswordValidator"},
]

LANGUAGE_CODE = "en-us"
TIME_ZONE = "UTC"
USE_I18N = True
USE_TZ = True

STATIC_URL = "static/"
DEFAULT_AUTO_FIELD = "django.db.models.BigAutoField"

REST_FRAMEWORK = {
    "DEFAULT_AUTHENTICATION_CLASSES": [
        "rest_framework.authentication.TokenAuthentication",
        "rest_framework.authentication.SessionAuthentication",
    ],
    "DEFAULT_PERMISSION_CLASSES": [
        "rest_framework.permissions.IsAuthenticated",
    ],
}

if os.environ.get("USE_IN_MEMORY_CHANNEL_LAYER", "false").lower() in {"1", "true", "yes"}:
    CHANNEL_LAYERS = {
        "default": {
            "BACKEND": "channels.layers.InMemoryChannelLayer",
        }
    }
else:
    redis_host: dict[str, object] = {
        "address": os.environ.get("REDIS_URL", "redis://127.0.0.1:6379/0")
    }

    # Backward-compatible escape hatch: old deployments used to pass connection
    # options as a nested `connection_kwargs` object. channels_redis 4.x expects
    # these options flattened into each host dict.
    redis_host.update(json_object_from_env("CHANNEL_REDIS_CONNECTION_KWARGS_JSON"))
    redis_host.update(json_object_from_env("CHANNEL_REDIS_HOST_OPTIONS_JSON"))
    if os.environ.get("REDIS_SOCKET_CONNECT_TIMEOUT"):
        redis_host["socket_connect_timeout"] = int(os.environ["REDIS_SOCKET_CONNECT_TIMEOUT"])
    if os.environ.get("REDIS_SOCKET_TIMEOUT"):
        redis_host["socket_timeout"] = int(os.environ["REDIS_SOCKET_TIMEOUT"])
    redis_host.setdefault("socket_connect_timeout", env_int("REDIS_SOCKET_CONNECT_TIMEOUT_DEFAULT", 5))
    redis_host.setdefault("socket_timeout", env_int("REDIS_SOCKET_TIMEOUT_DEFAULT", 30))
    redis_host.setdefault("health_check_interval", env_int("REDIS_HEALTH_CHECK_INTERVAL", 30))
    redis_host.setdefault("retry_on_timeout", env_bool("REDIS_RETRY_ON_TIMEOUT", True))
    redis_host.setdefault("socket_keepalive", env_bool("REDIS_SOCKET_KEEPALIVE", True))

    redis_layer_config: dict[str, object] = {
        "hosts": [redis_host],
        "capacity": int(os.environ.get("CHANNEL_LAYER_CAPACITY", "300")),
        "expiry": int(os.environ.get("CHANNEL_LAYER_EXPIRY", "10")),
        "group_expiry": int(os.environ.get("CHANNEL_LAYER_GROUP_EXPIRY", "3600")),
    }

    # Optional structured override for advanced tuning.
    # Any legacy `connection_kwargs` key is normalized into host options.
    extra_layer_config = json_object_from_env("CHANNEL_LAYER_CONFIG_JSON")
    legacy_connection_kwargs = extra_layer_config.pop("connection_kwargs", None)
    if isinstance(legacy_connection_kwargs, dict):
        redis_host.update(legacy_connection_kwargs)
    redis_layer_config.update(extra_layer_config)

    CHANNEL_LAYERS = {
        "default": {
            "BACKEND": "channels_redis.core.RedisChannelLayer",
            "CONFIG": redis_layer_config,
        }
    }
