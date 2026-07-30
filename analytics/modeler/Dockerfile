FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

WORKDIR /app

# The dependency install is deliberately split across several layers, and every pyproject dependency
# must appear in exactly one of them — test_dockerfile_layers_cover_every_declared_dependency pins
# that, because the project itself is installed with --no-deps below.
#
# Why: installing the whole scientific stack as one `pip install .` produced a single 567 MB layer.
# The prod host runs the containerd image store on spinning disk, where extracting and committing a
# layer that large outran the pull's lease and containerd GC'd it mid-commit — `docker pull` failed
# with "lease does not exist" and left a half-populated image that could not resolve its platform.
# Every image that pulls cleanly on that host has a largest layer under 20 MB. Keep these groups
# roughly balanced; do not collapse them back together.
#
# --no-compile keeps shipped .pyc out of the layers: PYTHONDONTWRITEBYTECODE is set and the runtime
# uid cannot write into site-packages, so that bytecode would never be reused anyway.
RUN pip install --no-compile "numpy>=2.1,<3"
RUN pip install --no-compile "pandas>=2.2,<3"
RUN pip install --no-compile "scikit-learn>=1.6,<2" "joblib>=1.4,<2"
RUN pip install --no-compile "pyarrow>=18,<21"
RUN pip install --no-compile "boto3>=1.35,<2" "psycopg[binary]>=3.2,<4"

# numpy/scipy/pandas ship their own test suites; nothing in this image runs them.
RUN find /usr/local/lib/python3.12/site-packages -type d -name tests -prune -exec rm -rf {} + \
 && find /usr/local/lib/python3.12/site-packages -type d -name __pycache__ -prune -exec rm -rf {} +

COPY analytics/modeler/pyproject.toml ./
COPY analytics/modeler/src ./src
# --no-deps: the layers above are the install plan. A dependency added to pyproject but not to those
# layers fails the coverage test instead of silently landing in one oversized layer.
RUN pip install --no-compile --no-deps .

ENV PYTHONPATH=/app/src
# /artifacts is the compose mount (BUILD_LAB_ARTIFACT_DIR) and a named volume, which Docker seeds
# from the image: without the directory owned by the runtime uid the first generation dies with a
# PermissionError immediately after claiming its lease.
RUN mkdir -p /artifacts && chown 65532:65532 /artifacts
USER 65532:65532
ENTRYPOINT ["python", "-m", "build_lab_modeler"]
