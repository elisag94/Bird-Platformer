#!/usr/bin/env bash
set -euo pipefail
export AWS_PAGER=""   # don't open less; a deploy script must never wait for a keypress
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

# 3. tell the "corner shops" (i.e. cloudfroint distros) to throw out old stock (one distribution per env)
DIST_ID=$(aws cloudfront list-distributions \
  --query "DistributionList.Items[?Comment=='bird-platformer-${ENV}'].Id | [0]" \
  --output text)

if [[ -z "$DIST_ID" || "$DIST_ID" == "None" ]]; then
  echo "No CloudFront distribution found for ${ENV}" >&2; exit 1
fi

if [[ -n "$DRY" ]]; then
  echo "(dryrun) would invalidate $DIST_ID"
else
  aws cloudfront create-invalidation --distribution-id "$DIST_ID" --paths "/*"
fi