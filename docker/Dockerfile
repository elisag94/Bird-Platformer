FROM nginx:alpine

# The Unity Web build output. Both the local workflow and CI place it here,
# so this Dockerfile doesn't care which produced it.
COPY web/game/ /usr/share/nginx/html/

# Server config: /healthz for the k8s probes, plus cache rules.
# NOTE: COPY paths are relative to the build CONTEXT (the repo root), not to
# this file's location. That's why it reads docker/nginx/... even though this
# Dockerfile already lives inside docker/.
COPY docker/nginx/default.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
