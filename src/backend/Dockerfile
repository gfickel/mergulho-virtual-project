# Cloud Run image for the Mergulho Virtual FastAPI backend.
# Same image deployed twice — only ROUTER_MODE differs (api vs admin).
FROM python:3.11-slim

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

# libheif is needed at runtime by pillow-heif for HEIC decoding.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libheif1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt ./
RUN pip install -r requirements.txt

COPY . .

# Cloud Run injects PORT (default 8080). uvicorn binds to all interfaces so the
# Cloud Run frontend can reach it. Use `sh -c` so $PORT expands at runtime.
ENV PORT=8080
EXPOSE 8080
CMD ["sh", "-c", "uvicorn main:app --host 0.0.0.0 --port ${PORT}"]
