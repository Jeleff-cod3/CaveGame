# Seamless Streaming Docker (Known-Good Setup)

This is a condensed, reproducible setup for running `facebook/seamless-streaming` locally with Docker GPU support, based on the current project state.

Use this when you want a working run without repeating hours of debugging.

## 1) Prerequisites

- Windows + Docker Desktop (WSL2 backend).
- NVIDIA GPU driver installed.
- Docker GPU support works:

```powershell
docker run --rm --gpus all nvidia/cuda:12.3.1-base-ubuntu22.04 nvidia-smi
```

If this fails, fix Docker GPU setup first.

## 2) Files expected in repo root

- `Dockerfile`
- `docker-compose.yml`

Current compose exposes:
- host `8000` -> container `7860`

## 3) Build and run backend (from scratch)

Run from repo root (`C:\Users\sashk\CaveGame`):

```powershell
docker compose down
docker compose build seamless-streaming
docker compose up -d seamless-streaming
docker logs -f seamless-streaming
```

Important:
- Do **not** use `--no-cache` unless you really need it.
- Do **not** run prune commands while troubleshooting, or you lose cached heavy layers.

## 4) Build and attach the real frontend (no image rebuild)

By default, backend starts with a placeholder page until frontend `dist` is provided.

```powershell
git clone https://huggingface.co/spaces/facebook/seamless-streaming seamless-ui-temp
cd seamless-ui-temp\streaming-react-app
npm install -g yarn
yarn
yarn build
```

Copy built frontend into running container:

```powershell
docker exec seamless-streaming /bin/sh -lc "mkdir -p /opt/seamless-streaming/streaming-react-app/dist"
docker cp .\dist\. seamless-streaming:/opt/seamless-streaming/streaming-react-app/dist
docker restart seamless-streaming
docker logs -f seamless-streaming
```

Open:
- `http://localhost:8000`

## 5) One-time runtime fixes that may be needed

### A) `libsox.so` missing error

If logs show:
`OSError: libsox.so: cannot open shared object file`

Run:

```powershell
docker exec -u 0 seamless-streaming /bin/sh -lc "apt-get update && apt-get install -y --no-install-recommends sox libsox3 libsox-dev libsox-fmt-all && rm -rf /var/lib/apt/lists/*"
docker restart seamless-streaming
```

### B) NLTK `averaged_perceptron_tagger_eng` missing

If logs show lookup error for `averaged_perceptron_tagger_eng`:

```powershell
docker exec seamless-streaming python -c "import nltk; nltk.download('averaged_perceptron_tagger_eng', download_dir='/root/nltk_data')"
docker exec seamless-streaming python -c "import nltk; nltk.download('cmudict', download_dir='/root/nltk_data')"
docker restart seamless-streaming
```

### C) `asset_store` import error from `fairseq2.assets`

If logs show:
`ImportError: cannot import name 'asset_store' from fairseq2.assets`

It means `fairseq2` version mismatch at runtime. Rebuild from current Dockerfile and avoid overriding packages manually in container:

```powershell
docker compose down
docker compose build seamless-streaming
docker compose up -d seamless-streaming
docker logs -f seamless-streaming
```

## 6) Behavior notes

- Seeing `dtype=float16` on GPU is normal; this is default inference behavior, not quantization.
- Warning spam like `Passing 'expressive' but the agent does not support expressive output!` means the selected agent/mode is not expressive-capable. Disable expressive mode in UI for that run.

## 7) Quick health checks

```powershell
docker ps --filter "name=seamless-streaming"
docker logs --tail 200 seamless-streaming
```

If container is up and logs have no traceback loop, service should be reachable at `http://localhost:8000`.

