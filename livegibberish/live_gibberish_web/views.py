from __future__ import annotations

import json

from django.http import HttpRequest, HttpResponseBadRequest, JsonResponse
from django.shortcuts import render
from django.views.decorators.csrf import csrf_exempt

from .app_state import get_status, set_enabled, update_config


def index_view(request: HttpRequest):
    status = get_status()
    return render(
        request,
        "live_gibberish_web/index.html",
        {
            "status": status,
            "status_json": json.dumps(status),
        },
    )


@csrf_exempt
def config_view(request: HttpRequest):
    if request.method == "GET":
        return JsonResponse(get_status())
    if request.method != "POST":
        return HttpResponseBadRequest("Use GET or POST.")

    try:
        payload = json.loads(request.body.decode("utf-8") or "{}")
    except json.JSONDecodeError as exc:
        return HttpResponseBadRequest(f"Invalid JSON: {exc}")

    config = update_config(payload)
    return JsonResponse({"ok": True, "config": get_status(), "updated": list(payload.keys())})


def status_view(_request: HttpRequest):
    return JsonResponse({"ok": True, "status": get_status()})


@csrf_exempt
def control_view(request: HttpRequest):
    if request.method != "POST":
        return HttpResponseBadRequest("Use POST.")
    try:
        payload = json.loads(request.body.decode("utf-8") or "{}")
    except json.JSONDecodeError as exc:
        return HttpResponseBadRequest(f"Invalid JSON: {exc}")

    action = str(payload.get("action", "")).lower()
    if action == "start":
        set_enabled(True)
    elif action == "stop":
        set_enabled(False)
    else:
        return HttpResponseBadRequest("Expected action=start or action=stop.")
    return JsonResponse({"ok": True, "status": get_status()})
