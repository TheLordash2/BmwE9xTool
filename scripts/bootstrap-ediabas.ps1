$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Dest = Join-Path $Root "vendor\ediabaslib"
$Commit = "00b53173c84db420c3bb20711a71b8dd92df0f6e"
if (-not (Test-Path (Join-Path $Dest ".git"))) {
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Dest) | Out-Null
  git clone https://github.com/uholeschak/ediabaslib.git $Dest
}
git -C $Dest fetch --all --tags
git -C $Dest checkout $Commit
Write-Host "EdiabasLib pinned to $Commit"
