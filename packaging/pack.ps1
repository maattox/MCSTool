#Requires -Version 5.1
<#
.SYNOPSIS
  Publish MCSTool (P1 layout) and build the per-user Inno Setup installer.

.DESCRIPTION
  Fails if artifacts\mcmgr-fn-softstop-linux-arm64.tar is missing — the installer
  must ship that tarball so users do not need Docker.

  Prerequisites: .NET 8 SDK, Inno Setup 6 (ISCC.exe).
#>
[CmdletBinding()]
param(
    [string]$FunctionTarPath,
    [string]$Configuration = 'Release',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packagingDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $packagingDir '..')).Path
$tarName = 'mcmgr-fn-softstop-linux-arm64.tar'
if ([string]::IsNullOrWhiteSpace($FunctionTarPath)) {
    $FunctionTarPath = Join-Path $repoRoot "artifacts\$tarName"
}

$outDir = Join-Path $packagingDir 'out'
$publishDir = Join-Path $outDir 'publish'
$issPath = Join-Path $packagingDir 'McManager.iss'
$csproj = Join-Path $repoRoot 'src\McManager.Hybrid\McManager.Hybrid.csproj'

function Get-ProductVersion {
    $text = Get-Content -LiteralPath $csproj -Raw
    if ($text -notmatch '<Version>([^<]+)</Version>') {
        throw "Could not read <Version> from $csproj"
    }
    return $Matches[1].Trim()
}

function Get-IsccPath {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            return $path
        }
    }
    $cmd = Get-Command ISCC -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    return $null
}

Write-Host "Repo: $repoRoot"

if (-not (Test-Path -LiteralPath $FunctionTarPath)) {
    throw @"
Missing Function image tar: $FunctionTarPath

The installer must include $tarName so users do not need Docker.

Rebuild it with Docker Desktop (developer only). See:
  functions\shutdown_vm\README.md
  section 'Developer rebuild (Docker Desktop)'

Do not commit the tar. Do not ship an installer without it.
"@
}

$iscc = Get-IsccPath
if (-not $iscc) {
    throw @"
Inno Setup 6 compiler (ISCC.exe) was not found.

Install it, then re-run this script. In PowerShell:

  winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements

Typical paths after install:
  %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
  C:\Program Files (x86)\Inno Setup 6\ISCC.exe
"@
}

$version = Get-ProductVersion
Write-Host "Version: $version"
Write-Host "Function tar: $FunctionTarPath"
Write-Host "ISCC: $iscc"

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $outDir) {
        Remove-Item -LiteralPath $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    Write-Host "Publishing Hybrid (win-x64 self-contained)..."
    & dotnet publish $csproj -c $Configuration -r win-x64 --self-contained -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish folder missing: $publishDir (run without -SkipPublish)"
}

$publishedTar = Join-Path $publishDir $tarName
Copy-Item -LiteralPath $FunctionTarPath -Destination $publishedTar -Force
if (-not (Test-Path -LiteralPath $publishedTar)) {
    throw "Function tar was not copied into the publish folder: $publishedTar"
}

$exe = Join-Path $publishDir 'McManager.Hybrid.exe'
$infra = Join-Path $publishDir 'infra\main.tf'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Published exe missing: $exe"
}
if (-not (Test-Path -LiteralPath $infra)) {
    throw "Published product tree missing infra\main.tf under $publishDir"
}

Write-Host "Compiling Inno Setup installer..."
$logPath = Join-Path $outDir 'iscc.log'
& $iscc /Q "/DMyAppVersion=$version" $issPath | Tee-Object -FilePath $logPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE (see $logPath)"
}

$installer = Join-Path $outDir "MCSTool-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer was not produced: $installer"
}

Write-Host "Installer: $installer"
Write-Host "Do not commit this .exe or the Function tar."
