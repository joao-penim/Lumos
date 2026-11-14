# Publishes Lumos for win-x64 into publish/win-x64:
#   Lumos.exe        settings UI (self-contained, ReadyToRun)
#   LumosAgent.exe   background agent (Native AOT when the MSVC linker is available)
#
# -Version 0.1.0   stamps the version into both exes (default: Directory.Build.props)
# -Zip             also creates publish/Lumos-<version>-win-x64.zip and publish/SHA256SUMS.txt
# -RequireAot      fail instead of falling back when the MSVC linker is missing (used for releases)
#
# Native AOT needs Visual Studio (or Build Tools) with the "Desktop development with C++" workload.
# Without it the agent is published as a regular self-contained app (works the same, uses more memory).
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version,
    [switch]$Zip,
    [switch]$RequireAot
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishRoot = Join-Path $root 'publish'
$out = Join-Path $publishRoot $Runtime

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$versionArgs = @("-p:Version=$Version")

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$hasLinker = (Test-Path $vswhere) -and
    [bool](& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
if ($RequireAot -and -not $hasLinker) { throw "Native AOT required but the MSVC linker was not found." }

if (Test-Path $publishRoot) { Remove-Item $publishRoot -Recurse -Force }

Write-Host "==> Lumos $Version ($Runtime)"
Write-Host "==> Settings UI"
dotnet publish "$root/src/Lumos.App" -c $Configuration -r $Runtime --self-contained -o $out -nologo @versionArgs
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if ($hasLinker) {
    Write-Host "==> Agent (Native AOT)"
    $tmp = Join-Path $publishRoot 'agent-aot'
    dotnet publish "$root/src/Lumos.Agent" -c $Configuration -r $Runtime -o $tmp -nologo @versionArgs
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Copy-Item "$tmp\LumosAgent.exe" $out -Force
    Remove-Item $tmp -Recurse -Force
} else {
    Write-Warning "MSVC linker not found: publishing the agent without Native AOT (install the 'Desktop development with C++' workload for the lean build)."
    dotnet publish "$root/src/Lumos.Agent" -c $Configuration -r $Runtime --self-contained -p:PublishAot=false -o $out -nologo @versionArgs
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

Get-ChildItem $out -Filter *.pdb | Remove-Item
Copy-Item (Join-Path $root 'LICENSE') $out -Force

if ($Zip) {
    $zipName = "Lumos-$Version-$Runtime.zip"
    $zipPath = Join-Path $publishRoot $zipName
    Compress-Archive -Path "$out\*" -DestinationPath $zipPath -CompressionLevel Optimal
    # sha256sum-compatible format: "<hash>  <file>"
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path (Join-Path $publishRoot 'SHA256SUMS.txt') -Value "$hash  $zipName" -Encoding ascii
    Write-Host "==> $zipName  sha256=$hash"
}

Write-Host "==> Done: $out"
Get-ChildItem $out -Filter *.exe | ForEach-Object { "{0,-18} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
