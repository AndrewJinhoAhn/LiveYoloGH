<#
  LiveYolo first-run bootstrap.

  Creates a self-contained Python environment + all dependencies under $Base
  (default: %LOCALAPPDATA%\LiveYolo). The user installs nothing manually.

  Steps:
    1. Download a self-contained CPython (python-build-standalone) -> $Base\python
    2. Detect GPU -> choose CUDA (cu124) or CPU torch build
    3. Download torch + torchvision wheels -> install
    4. pip install remaining deps from requirements.txt
    5. Write .ready marker, clean up the wheel cache

  Downloads use curl.exe (built into Windows 10/11): resumable (-C -), retried,
  and aborted+resumed automatically on stalls. Works in any session context.

  Idempotent: exits immediately if .ready already exists.
  Invoked by the Grasshopper component as:
    powershell -ExecutionPolicy Bypass -File setup.ps1 -GhaDir <dir> -Base <dir>
#>
param(
    [Parameter(Mandatory = $true)][string]$GhaDir,
    [Parameter(Mandatory = $true)][string]$Base
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# --- pinned versions (must have matching cp313 wheels on download.pytorch.org) ---
$TorchVer  = "2.6.0"
$VisionVer = "0.21.0"
$PyMinor   = "3.13"     # self-contained Python minor; pairs with cp313 torch wheels

$pyDir   = Join-Path $Base "python"
$pyExe   = Join-Path $pyDir "python.exe"
$backend = Join-Path $Base "backend"
$wheels  = Join-Path $Base "wheels"
$ready   = Join-Path $Base ".ready"
$curl    = Join-Path $env:SystemRoot "System32\curl.exe"

# Log to stderr so messages never pollute function return values (stdout).
# The launcher merges stderr into the log via 2>&1.
function Log($m) { [Console]::Error.WriteLine("[setup] " + $m) }

# Resumable, retrying download. --speed-limit/--speed-time aborts a stalled
# transfer so --retry can resume it from where it stopped (-C -).
function Download-File([string]$Url, [string]$Dest) {
    Log ("downloading " + (Split-Path $Dest -Leaf) + " ...")
    & $curl -L --fail --retry 20 --retry-delay 5 --retry-all-errors `
        --speed-limit 2048 --speed-time 30 -C - -o "$Dest" "$Url" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Download failed (curl exit $LASTEXITCODE): $Url" }
}

if (Test-Path $ready) { Log "Already set up. Nothing to do."; exit 0 }
if (-not (Test-Path $curl)) { throw "curl.exe not found (requires Windows 10/11)." }

New-Item -ItemType Directory -Force -Path $Base, $backend, $wheels | Out-Null

# ---------- 1. self-contained Python ----------
if (-not (Test-Path $pyExe)) {
    Log "Locating self-contained Python $PyMinor ..."
    $rel = Invoke-RestMethod "https://api.github.com/repos/astral-sh/python-build-standalone/releases/latest" `
        -Headers @{ "User-Agent" = "LiveYolo" }
    $pat = "cpython-$([regex]::Escape($PyMinor))\.\d+\+\d+-x86_64-pc-windows-msvc-install_only\.tar\.gz$"
    $asset = $rel.assets | Where-Object { $_.name -match $pat } | Select-Object -First 1
    if (-not $asset) { throw "No self-contained Python $PyMinor build in latest python-build-standalone release." }

    $archive = Join-Path $Base "python.tar.gz"
    Download-File $asset.browser_download_url $archive
    Log "Extracting Python ..."
    tar -xzf $archive -C $Base          # install_only tarball extracts to $Base\python\
    Remove-Item $archive -Force
}
if (-not (Test-Path $pyExe)) { throw "Python extraction failed: $pyExe not found." }
$pyVer = (& $pyExe --version) 2>&1
Log "Python: $pyVer"

# ---------- 2. pip ----------
& $pyExe -m pip install --upgrade pip --disable-pip-version-check | Out-Null
$pyTag = (& $pyExe -c "import sys;print('cp%d%d' % (sys.version_info.major, sys.version_info.minor))").Trim()

# ---------- 3. GPU detection ----------
$hasNvidia = $false
try {
    if (Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "NVIDIA" }) {
        $hasNvidia = $true
    }
} catch { }
$variant = if ($hasNvidia) { "cu124" } else { "cpu" }
Log ("GPU: " + ($(if ($hasNvidia) { "NVIDIA detected -> CUDA build (cu124)" } else { "no NVIDIA -> CPU build" })))

# ---------- 4. torch + torchvision ----------
function Get-Wheel([string]$name, [string]$ver) {
    $file = "$name-$ver+$variant-$pyTag-$pyTag-win_amd64.whl"
    $dest = Join-Path $wheels $file
    if (Test-Path $dest) { Log "Using cached $file"; return $dest }
    $url = "https://download.pytorch.org/whl/$variant/$name-$ver%2B$variant-$pyTag-$pyTag-win_amd64.whl"
    Download-File $url $dest
    return $dest
}
$torchWhl  = Get-Wheel "torch"       $TorchVer
$visionWhl = Get-Wheel "torchvision" $VisionVer
Log "Installing torch + torchvision ..."
& $pyExe -m pip install --disable-pip-version-check "$torchWhl" "$visionWhl"
if ($LASTEXITCODE -ne 0) { throw "torch / torchvision install failed." }

# ---------- 5. remaining deps + backend script ----------
Copy-Item (Join-Path $GhaDir "backend.py")       (Join-Path $backend "backend.py")       -Force
Copy-Item (Join-Path $GhaDir "requirements.txt") (Join-Path $backend "requirements.txt") -Force
Log "Installing ultralytics / fastapi / uvicorn / opencv ..."
& $pyExe -m pip install --disable-pip-version-check -r (Join-Path $backend "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Dependency install failed." }

# ---------- 6. done ----------
Set-Content -Path $ready -Value ("ok | $pyVer | $variant | $(Get-Date -Format o)")
Remove-Item $wheels -Recurse -Force -ErrorAction SilentlyContinue   # reclaim wheel cache (~2.4 GB)
Log "DONE. Environment ready at $Base"
exit 0
