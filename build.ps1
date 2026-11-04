param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not $NoBuild) {
    dotnet build (Join-Path $root 'CruciblePilot.csproj') -c Release | Write-Host
}

$dev = Join-Path $env:APPDATA 'XIVLauncher\devPlugins\CruciblePilot'
New-Item -ItemType Directory -Force -Path $dev | Out-Null

Copy-Item (Join-Path $root 'bin\Release\CruciblePilot.dll') $dev -Force
Copy-Item (Join-Path $root 'CruciblePilot.json') $dev -Force

$dll = Get-Item (Join-Path $dev 'CruciblePilot.dll')
Write-Host ("Deployed {0} ({1:N0} bytes) to {2}" -f $dll.Name, $dll.Length, $dev)
Write-Host 'In game: /xlplugins -> Dev Plugins -> enable CruciblePilot, then /cp help'
