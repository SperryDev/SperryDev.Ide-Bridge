#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$MSBuildPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\SperryDev.Ide.Bridge.Vsix\SperryDev.Ide.Bridge.Vsix.csproj"
$Vsix = Join-Path $RepoRoot "src\SperryDev.Ide.Bridge.Vsix\SperryDev.Ide.Bridge.Vsix.vsix"

if ([string]::IsNullOrWhiteSpace($MSBuildPath)) {
    $vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw "vswhere.exe not found at $vswhere. Pass -MSBuildPath explicitly."
    }

    $installation = & $vswhere -latest -prerelease -products '*' -version '[18.0,19.0)' -property installationPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installation)) {
        throw "No Visual Studio 18 installation was found. Pass -MSBuildPath explicitly."
    }

    $MSBuildPath = Join-Path $installation "MSBuild\Current\Bin\MSBuild.exe"
}

if (-not (Test-Path -LiteralPath $MSBuildPath)) {
    throw "MSBuild.exe not found at $MSBuildPath."
}

& $MSBuildPath $Project /restore "/p:Configuration=$Configuration" /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed for $Project with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $Vsix)) {
    throw "VSIX package was not created at $Vsix."
}

Write-Host "Built $Vsix" -ForegroundColor Green
