FROM nginx:alpine

# The Unity Web build output. Both the local workflow and CI place it here,
# so this Dockerfile doesn't care which produced it.
COPY web/game/ /usr/share/nginx/html/
# Version is a fact about the BUILD, so it's stamped in here. Environment is
# NOT — that's mounted at runtime from a ConfigMap (env.json).
ARG APP_VERSION=local
COPY docker/build-info.js /usr/share/nginx/html/build-info.js
RUN printf '{"version": "%s"}\n' "$APP_VERSION" > /usr/share/nginx/html/version.json \
 && sed -i 's#</body>#<script src="build-info.js"></script></body>#' /usr/share/nginx/html/index.html
# Server config: /healthz for the k8s probes, plus cache rules.
# NOTE: COPY paths are relative to the build CONTEXT (the repo root), not to
# this file's location. That's why it reads helm/bird-platformer/files/...
# even though this Dockerfile lives inside docker/. That file is the ONE home
# for the nginx config — Helm reads the same file via .Files.Get.
COPY helm/bird-platformer/files/nginx-default.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
