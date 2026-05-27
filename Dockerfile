FROM nvidia/cuda:11.8.0-cudnn8-runtime-ubuntu20.04

ENV DEBIAN_FRONTEND=noninteractive \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    HF_HOME=/models/hf \
    TORCH_HOME=/models/torch

RUN apt-get update && apt-get install -y --no-install-recommends \
    python3.8 python3-pip git ffmpeg libsndfile1 ca-certificates \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

RUN update-alternatives --install /usr/bin/python python /usr/bin/python3.8 1
RUN python -m pip install --upgrade pip setuptools wheel

# PyTorch stack matching Meta guidance (pt2.1.1 + cu118)
RUN pip install --no-cache-dir --index-url https://download.pytorch.org/whl/cu118 \
    torch==2.1.1 torchvision==0.16.1 torchaudio==2.1.1

# fairseq2 compatibility set for seamless_communication
RUN pip install --no-cache-dir --pre --force-reinstall \
    fairseq2==0.2.1 fairseq2n==0.2.1 \
    --extra-index-url https://fair.pkg.atmeta.com/fairseq2/whl/nightly/pt2.1.1/cu118

RUN pip install --no-cache-dir fastapi "uvicorn[standard]"

WORKDIR /opt
RUN git clone --depth 1 https://huggingface.co/spaces/facebook/seamless-streaming

WORKDIR /opt/seamless-streaming/seamless_server
RUN mkdir -p ../streaming-react-app/dist \
    && printf '%s\n' '<!doctype html><html><body><h1>Seamless Streaming API is running.</h1></body></html>' > ../streaming-react-app/dist/index.html
RUN grep -v '^git+https://github.com/facebookresearch/seamless_communication.git' requirements.txt > /tmp/requirements.txt \
    && pip install --no-cache-dir --retries 10 --timeout 120 -r /tmp/requirements.txt \
    && pip install --no-cache-dir --retries 10 --timeout 120 --no-deps git+https://github.com/facebookresearch/seamless_communication.git \
    && pip install --no-cache-dir --retries 10 --timeout 120 "simuleval~=1.1.3"
RUN chmod +x run_docker.sh

EXPOSE 7860
CMD ["bash", "./run_docker.sh"]
