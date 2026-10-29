<#
.SYNOPSIS
    Tests, publishes the self-contained single-file GitPersona.exe and optionally builds the installer.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/publish.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/publish.ps1 -Version 1.0.1 -Installer
#>
param(
    [string]$Version = '1.0.0',
    [switch]$SkipTests,
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish'

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'GitPersona.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\GitPersona.App') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Get-Item (Join-Path $publish 'GitPersona.exe')
Write-Host ("Published {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))

if ($Installer) {
    $iscc = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isdl.php' }

    & $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\GitPersona.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }
    Get-ChildItem (Join-Path $root 'installer\Output') -Filter '*.exe' | ForEach-Object { Write-Host "Installer: $($_.FullName)" }
}
