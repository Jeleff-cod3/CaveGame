SECRET_KEY = "live-gibberish-prototype-only"
DEBUG = True
ALLOWED_HOSTS = ["*"]
ROOT_URLCONF = "live_gibberish_web.urls"
ASGI_APPLICATION = "live_gibberish_web.asgi.application"
DEFAULT_AUTO_FIELD = "django.db.models.BigAutoField"

INSTALLED_APPS = [
    "channels",
    "live_gibberish_web",
]

MIDDLEWARE = [
    "django.middleware.common.CommonMiddleware",
]

TEMPLATES = [
    {
        "BACKEND": "django.template.backends.django.DjangoTemplates",
        "DIRS": [],
        "APP_DIRS": True,
        "OPTIONS": {
            "context_processors": [],
        },
    }
]

CHANNEL_LAYERS = {
    "default": {
        "BACKEND": "channels.layers.InMemoryChannelLayer",
    }
}
