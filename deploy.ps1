<#
.SYNOPSIS
    Manual deploy for AppStatServer -> the "stat" host (PowerShell port of deploy.sh).

.DESCRIPTION
    Publishes a self-contained linux-x64 build, ships it to the server, restarts the
    systemd service, and verifies it responds.

        .\deploy.ps1              Full deploy: ship the entire self-contained runtime.
                                  Safe default; required after changing NuGet deps, the
                                  .NET version, or any static asset (wwwroot / Pages).
                                  Uses an atomic swap and keeps the previous build at
                                  <REMOTE_DIR>.old for rollback.

        .\deploy.ps1 -AppOnly     Fast deploy: ship only AppStatServer.dll and restart.
                                  Use ONLY for pure C# changes with no new dependencies
                                  and no static-asset changes.

    Requirements (on PATH): dotnet SDK, ssh, scp, tar, curl.exe. ssh must reach the
    server via the "stat" host alias in ~/.ssh/config (key-based). Windows 10/11 ship
    tar.exe and curl.exe.

    Server-side layout is untouched except the app binaries in REMOTE_DIR; secrets
    (/etc/appstatserver/*.env) and data (/var/lib/appstatserver) live elsewhere and
    survive every deploy.

    Override any setting via environment variables of the same name, e.g.:
        $env:SSH_HOST = "stat"; .\deploy.ps1
#>
[CmdletBinding()]
param(
    [switch]$AppOnly
)

$ErrorActionPreference = 'Stop'
# Take manual control of native exit codes (checked via Assert-Ok) so behaviour is the
# same on Windows PowerShell 5.1 and PowerShell 7+.
$PSNativeCommandUseErrorActionPreference = $false

function Get-Or([string]$value, [string]$default) {
    if ([string]::IsNullOrEmpty($value)) { $default } else { $value }
}

function Assert-Ok([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

# Run a bash script on the server over ssh. The script is fed via stdin (no argv quoting
# headaches) and normalised to LF so the remote bash never sees a stray CR. Simple values
# are handed to the remote shell as environment variables.
function Invoke-RemoteBash {
    param(
        [Parameter(Mandatory)][string]$Script,
        [hashtable]$RemoteEnv = @{}
    )
    $prefix = ($RemoteEnv.GetEnumerator() | ForEach-Object { "$($_.Key)='$($_.Value)'" }) -join ' '
    $lf = ($Script -replace "`r`n", "`n") -replace "`r", "`n"
    $lf | & ssh $SSH_HOST "$prefix bash -s"
    Assert-Ok "remote command"
}

# --- Config (override via env vars of the same name) ---
# SSH_HOST is an ssh config alias — define the real user/host under "Host stat" in
# ~/.ssh/config so no server hostname lives in this public repo.
$SSH_HOST   = Get-Or $env:SSH_HOST   'stat'
$REMOTE_DIR = Get-Or $env:REMOTE_DIR '/opt/appstatserver'
$SERVICE    = Get-Or $env:SERVICE    'appstatserver.service'
# Optional: set PUBLIC_URL (e.g. https://your-host) to also verify the public endpoint.
$PUBLIC_URL = Get-Or $env:PUBLIC_URL ''
$HEALTH_URL = Get-Or $env:HEALTH_URL 'http://127.0.0.1:5000/login.html'
$HEALTH_WAIT_SECONDS = [int](Get-Or $env:HEALTH_WAIT_SECONDS '30')
$RID        = Get-Or $env:RID        'linux-x64'
$PROJECT    = Get-Or $env:PROJECT    'src/AppStatServer/AppStatServer.csproj'

Set-Location $PSScriptRoot

$pub = Join-Path ([System.IO.Path]::GetTempPath()) ('appstat-pub-' + [System.IO.Path]::GetRandomFileName())
$tar = "$pub.tar.gz"
New-Item -ItemType Directory -Path $pub | Out-Null

try {
    Write-Host "==> Publishing $RID (self-contained, Release)..."
    & dotnet publish $PROJECT -c Release -r $RID --self-contained -o $pub --nologo -v m
    Assert-Ok "dotnet publish"

    if ($AppOnly) {
        Write-Host "==> App-only deploy: shipping AppStatServer.dll..."
        & scp (Join-Path $pub 'AppStatServer.dll') "${SSH_HOST}:${REMOTE_DIR}/AppStatServer.dll"
        Assert-Ok "scp"
        Invoke-RemoteBash -RemoteEnv @{ REMOTE_DIR = $REMOTE_DIR; SERVICE = $SERVICE } -Script @'
set -euo pipefail
chown root:root "$REMOTE_DIR/AppStatServer.dll"
systemctl restart "$SERVICE"
'@
    }
    else {
        Write-Host "==> Full deploy: shipping the self-contained runtime..."
        & tar -czf $tar -C $pub .
        Assert-Ok "tar"
        & scp $tar "${SSH_HOST}:/tmp/appstat-deploy.tar.gz"
        Assert-Ok "scp"

        # Extract to a staging dir first, then swap atomically to minimise downtime and
        # keep a rollback copy. REMOTE_DIR/SERVICE arrive as remote env vars.
        Invoke-RemoteBash -RemoteEnv @{ REMOTE_DIR = $REMOTE_DIR; SERVICE = $SERVICE } -Script @'
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
'@
    }

    Write-Host "==> Verifying service..."
    Invoke-RemoteBash -RemoteEnv @{ SERVICE = $SERVICE; HEALTH_URL = $HEALTH_URL; HEALTH_WAIT_SECONDS = $HEALTH_WAIT_SECONDS } -Script @'
set -euo pipefail

if systemctl is-active --quiet "$SERVICE"; then
    echo '   service: active'
else
    echo '   service NOT active — recent logs:'
    journalctl -u "$SERVICE" -n 40 --no-pager
    exit 1
fi

deadline=$((SECONDS + HEALTH_WAIT_SECONDS))
last_code="000"

while (( SECONDS <= deadline )); do
    # curl exits non-zero on connection errors; keep retrying until timeout.
    code="$(curl -sS -o /dev/null -w '%{http_code}' "$HEALTH_URL" || true)"
    last_code="${code:-000}"

    if [[ "$last_code" == "200" ]]; then
        echo "   local  ${HEALTH_URL} -> HTTP ${last_code}"
        exit 0
    fi

    sleep 1
done

echo "   local  ${HEALTH_URL} -> HTTP ${last_code}"
echo '   health check timeout — recent logs:'
journalctl -u "$SERVICE" -n 60 --no-pager
exit 1
'@

    if (-not [string]::IsNullOrEmpty($PUBLIC_URL)) {
        $code = (& curl.exe -s -o NUL -w "%{http_code}" "$PUBLIC_URL/login.html")
        $code = "$code".Trim()
        Write-Host "   public $PUBLIC_URL/login.html -> HTTP $code"
        if ($code -eq '200') {
            Write-Host "==> Deploy OK."
        }
        else {
            Write-Error "==> WARNING: public check did not return 200."
            exit 1
        }
    }
    else {
        Write-Host "==> Deploy OK. (set PUBLIC_URL to also verify the public endpoint)"
    }
}
finally {
    Remove-Item -Recurse -Force $pub -ErrorAction SilentlyContinue
    Remove-Item -Force $tar -ErrorAction SilentlyContinue
}
