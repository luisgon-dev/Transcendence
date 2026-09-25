FROM python:3.14-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

WORKDIR /app

COPY analytics/modeler/pyproject.toml ./
COPY analytics/modeler/src ./src
# --no-compile: PYTHONDONTWRITEBYTECODE is set and the runtime uid cannot write into site-packages,
# so shipped bytecode would never be reused.
RUN pip install --no-compile .

ENV PYTHONPATH=/app/src
# /artifacts is the compose mount (BUILD_LAB_ARTIFACT_DIR) and a named volume, which Docker seeds
# from the image: without the directory owned by the runtime uid the first generation dies with a
# PermissionError immediately after claiming its lease.
RUN mkdir -p /artifacts && chown 65532:65532 /artifacts
USER 65532:65532
ENTRYPOINT ["python", "-m", "build_lab_modeler"]
