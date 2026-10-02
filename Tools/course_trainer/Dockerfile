# syntax=docker/dockerfile:1
# Course Trainer image. Build context = the repository root (docker-compose.yml sets it); the sibling
# Dockerfile.dockerignore sends only Tools/course_{gen,prep,trainer} and Docs/hole-format, in the repo's layout
# (course_prep finds the package schemas at <root>/Docs/hole-format).

# ---- 1. web UI ----
FROM node:22-slim AS web
WORKDIR /web
COPY Tools/course_trainer/web/package.json Tools/course_trainer/web/package-lock.json ./
RUN npm ci --no-fund --no-audit
COPY Tools/course_trainer/web/ ./
RUN npm run build

# ---- 2. Python runtime ----
FROM python:3.13-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1 PIP_DISABLE_PIP_VERSION_CHECK=1
# libexpat: rasterio's bundled GDAL links it (course_prep imports rasterio at module level).
RUN apt-get update && apt-get install -y --no-install-recommends libexpat1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY Tools/course_prep/requirements.txt Tools/course_prep/requirements.txt
COPY Tools/course_trainer/requirements.txt Tools/course_trainer/requirements.txt
RUN pip install -r Tools/course_trainer/requirements.txt

COPY Docs/hole-format Docs/hole-format
COPY Tools/course_prep Tools/course_prep
COPY Tools/course_gen Tools/course_gen
COPY Tools/course_trainer Tools/course_trainer
COPY --from=web /web/dist Tools/course_trainer/web/dist

# Volumes mount over /data/*; creating them here gives fresh named volumes the right owner.
RUN useradd --uid 10001 --create-home trainer && mkdir -p /data/holes /data/model && chown -R trainer /data
USER trainer
ENV TRAINER_HOST=0.0.0.0 TRAINER_PORT=8765 TRAINER_HOLES_DIR=/data/holes \
    COURSE_GEN_MODEL_PATH=/data/model/preference_model.npz
WORKDIR /app/Tools/course_trainer
EXPOSE 8765
# `docker compose run --rm trainer adduser obi` -> python server.py adduser obi
ENTRYPOINT ["python", "server.py"]
