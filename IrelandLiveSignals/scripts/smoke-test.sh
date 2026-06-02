#!/bin/bash
BASE=${1:-https://yourdomain.ie}
set -e
curl -sf "$BASE/api/grid/health" | grep '"status":"ok"'
curl -sf "$BASE/api/grid/current" | grep '"greenScore"'
curl -sf "$BASE/api/push/vapid-public-key" | grep '"publicKey"'
curl -o /dev/null -sw "%{http_code}" "$BASE/privacy" | grep 200
curl -o /dev/null -sw "%{http_code}" "$BASE/attribution" | grep 200
echo "All smoke tests passed."
