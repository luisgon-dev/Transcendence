FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app
COPY analytics/modeler/pyproject.toml ./
COPY analytics/modeler/src ./src
RUN pip install --no-cache-dir .

ENV PYTHONPATH=/app/src
# /artifacts is the compose mount (BUILD_LAB_ARTIFACT_DIR) and a named volume, which Docker seeds
# from the image: without the directory owned by the runtime uid the first generation dies with a
# PermissionError immediately after claiming its lease.
RUN mkdir -p /artifacts && chown 65532:65532 /artifacts
USER 65532:65532
ENTRYPOINT ["python", "-m", "build_lab_modeler"]
