FROM nginx:alpine

# The Unity Web build output. Both the local workflow and CI place it here,
# so this Dockerfile doesn't care which produced it.
COPY web/game/ /usr/share/nginx/html/

# MIME types, cache headers and the /healthz endpoint used by the k8s probes.
COPY nginx/default.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
