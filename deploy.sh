#!/usr/bin/env bash
#
# Manual deploy for AppStatServer -> stat.pix2d.com
#
# Publishes a self-contained linux-x64 build, ships it to the server, restarts the
# systemd service, and verifies it responds.
#
#   ./deploy.sh              Full deploy: ship the entire self-contained runtime.
#                            Safe default; required after changing NuGet deps or the
#                            .NET version. Uses an atomic swap and keeps the previous
#                            build at <REMOTE_DIR>.old for rollback.
#
#   ./deploy.sh --app-only   Fast deploy: ship only AppStatServer.dll and restart.
#                            Use for pure code changes with no new dependencies.
#
# Requirements (run from a shell with these available — e.g. Git Bash on Windows):
#   - dotnet SDK locally
#   - ssh / scp access to the server (key-based)
#   - tar
#
# Server-side layout is untouched by this script except the app binaries in REMOTE_DIR;
# secrets (/etc/appstatserver/*.env) and data (/var/lib/appstatserver) live elsewhere
# and survive every deploy.
#
# Override any of these via environment variables:
SSH_HOST="${SSH_HOST:-root@stat.pix2d.com}"
REMOTE_DIR="${REMOTE_DIR:-/opt/appstatserver}"
SERVICE="${SERVICE:-appstatserver.service}"
PUBLIC_URL="${PUBLIC_URL:-https://stat.pix2d.com}"
RID="${RID:-linux-x64}"
PROJECT="${PROJECT:-src/AppStatServer/AppStatServer.csproj}"

set -euo pipefail

MODE="full"
[[ "${1:-}" == "--app-only" ]] && MODE="app"

cd "$(dirname "${BASH_SOURCE[0]}")"

PUB="$(mktemp -d)"
TAR=""
cleanup() { rm -rf "$PUB" "${TAR:-/nonexistent}"; }
trap cleanup EXIT

echo "==> Publishing $RID (self-contained, Release)…"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained -o "$PUB" --nologo -v m

if [[ "$MODE" == "app" ]]; then
    echo "==> App-only deploy: shipping AppStatServer.dll…"
    scp "$PUB/AppStatServer.dll" "$SSH_HOST:$REMOTE_DIR/AppStatServer.dll"
    ssh "$SSH_HOST" "chown root:root '$REMOTE_DIR/AppStatServer.dll' && systemctl restart '$SERVICE'"
else
    echo "==> Full deploy: shipping the self-contained runtime…"
    TAR="$PUB.tar.gz"
    tar -czf "$TAR" -C "$PUB" .
    scp "$TAR" "$SSH_HOST:/tmp/appstat-deploy.tar.gz"
    # Extract to a staging dir first, then swap atomically to minimise downtime and
    # keep a rollback copy. REMOTE_DIR/SERVICE are passed as env to the remote shell.
    ssh "$SSH_HOST" REMOTE_DIR="$REMOTE_DIR" SERVICE="$SERVICE" 'bash -s' <<'REMOTE'
set -euo pipefail
STAGE="${REMOTE_DIR}.new"
rm -rf "$STAGE"; mkdir -p "$STAGE"
tar -xzf /tmp/appstat-deploy.tar.gz -C "$STAGE"
chmod +x "$STAGE/AppStatServer"
chown -R root:root "$STAGE"
systemctl stop "$SERVICE"
rm -rf "${REMOTE_DIR}.old"
[ -d "$REMOTE_DIR" ] && mv "$REMOTE_DIR" "${REMOTE_DIR}.old"
mv "$STAGE" "$REMOTE_DIR"
systemctl start "$SERVICE"
rm -f /tmp/appstat-deploy.tar.gz
echo "   previous build kept at ${REMOTE_DIR}.old (rollback: systemctl stop ${SERVICE}; rm -rf ${REMOTE_DIR}; mv ${REMOTE_DIR}.old ${REMOTE_DIR}; systemctl start ${SERVICE})"
REMOTE
fi

echo "==> Verifying service…"
sleep 3
ssh "$SSH_HOST" "
    if systemctl is-active --quiet '$SERVICE'; then
        echo '   service: active'
    else
        echo '   service NOT active — recent logs:'; journalctl -u '$SERVICE' -n 20 --no-pager; exit 1
    fi
    curl -sf -o /dev/null -w '   local  127.0.0.1:5000/login.html -> HTTP %{http_code}\n' http://127.0.0.1:5000/login.html
"

code="$(curl -s -o /dev/null -w '%{http_code}' "$PUBLIC_URL/login.html" || true)"
echo "   public $PUBLIC_URL/login.html -> HTTP $code"
if [[ "$code" == "200" ]]; then
    echo "==> Deploy OK."
else
    echo "==> WARNING: public check did not return 200." >&2
    exit 1
fi
