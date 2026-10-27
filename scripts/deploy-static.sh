#!/usr/bin/env bash
set -euo pipefail
ENV="${1:?usage: deploy-static.sh <uat|prod>}"
BUCKET="bird-platformer-${ENV}"
SRC="web/game"
DRY="${DRYRUN:+--dryrun}"

# 1. everything that ISN'T shrink-wrapped
aws s3 sync "$SRC/" "s3://$BUCKET/" --delete --exclude "*.gz" $DRY

# 2. shrink-wrapped files: right label + "unwrap first" sticker
upload_gz() {
  aws s3 cp "$SRC/Build/" "s3://$BUCKET/Build/" --recursive \
    --exclude "*" --include "$1" \
    --content-type "$2" --content-encoding gzip $DRY
}
upload_gz "*.wasm.gz"         application/wasm
upload_gz "*.data.gz"         application/octet-stream
upload_gz "*.framework.js.gz" application/javascript