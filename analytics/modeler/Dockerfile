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
# uid cannot write into site-packages, so that bytecode would never be reused anyway. It prevents
# the files at creation time on purpose. Do NOT add a later layer that deletes files belonging to
# an earlier one (vendored test suites, __pycache__ from the base image): that writes overlayfs
# whiteouts across layers, and extracting them fails on this host with
# "failed to Lchown ...: no such file or directory".
RUN pip install --no-compile "numpy>=2.1,<3"
# scipy on its own, primarily because it is a direct import (`from scipy.special import erfc`) that
# used to arrive only as scikit-learn's transitive dependency -- a scikit-learn release that repins
# or drops it would have broken the modeler with nothing in pyproject to explain why.
#
# Measured, so the layering claim is not overstated: this split takes scikit-learn's layer from
# 153 MB to 36.9 MB, but scipy is 115 MB of that and simply moves rather than shrinks. The LARGEST
# layer barely moves (153 MB -> 141 MB, pyarrow), because neither pyarrow nor scipy can be split
# further -- they are single wheels, and carving them up would need cross-layer deletes this host
# cannot extract. Layering alone therefore does NOT fix the containerd lease race on a cold pull;
# scripts/ops/pull-modeler-image.sh retrying is what fixes that. This split is still worth having
# for the declared dependency and for one fewer 150 MB commit, but it is not the cure.
RUN pip install --no-compile "scipy>=1.14,<2"
RUN pip install --no-compile "pandas>=2.2,<3"
RUN pip install --no-compile "scikit-learn>=1.6,<2" "joblib>=1.4,<2" "threadpoolctl>=3.5,<4"
# pyarrow is a single ~142 MB wheel and cannot be split further without deleting files across
# layers, which this host cannot extract (see the whiteout note above). It is therefore the layer
# most likely to lose the containerd lease race on a cold pull, and the reason
# scripts/ops/pull-modeler-image.sh retries rather than trusting one attempt.
RUN pip install --no-compile "pyarrow>=18,<21"
RUN pip install --no-compile "psycopg[binary]>=3.2,<4"
RUN pip install --no-compile "boto3>=1.35,<2"

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
