from __future__ import annotations

import json
import re

from django.http import HttpRequest, HttpResponseBadRequest, JsonResponse
from django.shortcuts import render
from django.views.decorators.csrf import csrf_exempt

from live_gibberish.audio_bank import (
    AUDIO_BANK_ROOT,
    load_manifest,
    save_gibberish_recording,
    save_whitelist_recording,
    validate_audio_bank,
)

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


def audio_bank_status_view(request: HttpRequest):
    user_id = request.GET.get("user_id") or get_status().get("audio_bank_user") or "default"
    whitelist = _split_request_words(request.GET.getlist("whitelist"))
    report = validate_audio_bank(user_id, required_whitelist=whitelist, root=AUDIO_BANK_ROOT)
    return JsonResponse(
        {
            "ok": report.ok,
            "manifest": load_manifest(user_id, root=AUDIO_BANK_ROOT),
            "validation": report.to_dict(),
        }
    )


@csrf_exempt
def audio_bank_recording_view(request: HttpRequest):
    if request.method != "POST":
        return HttpResponseBadRequest("Use POST.")

    user_id = request.POST.get("user_id") or get_status().get("audio_bank_user") or "default"
    kind = str(request.POST.get("kind") or "").strip().lower()
    upload = request.FILES.get("file")
    if upload is None:
        return HttpResponseBadRequest("Upload a WAV file in the 'file' field.")

    try:
        if kind == "whitelist":
            word = request.POST.get("word") or ""
            manifest = save_whitelist_recording(user_id, word, upload.read(), root=AUDIO_BANK_ROOT)
        elif kind == "gibberish":
            bucket = request.POST.get("bucket") or ""
            name = request.POST.get("name") or ""
            manifest = save_gibberish_recording(user_id, bucket, upload.read(), name=name, root=AUDIO_BANK_ROOT)
        else:
            return HttpResponseBadRequest("Expected kind=whitelist or kind=gibberish.")
    except ValueError as exc:
        return HttpResponseBadRequest(str(exc))

    return JsonResponse({"ok": True, "user_id": user_id, "manifest": manifest})


def _split_request_words(values: list[str]) -> list[str]:
    words = []
    for value in values:
        words.extend(item for item in re.split(r"[\s,;]+", value) if item)
    return words
