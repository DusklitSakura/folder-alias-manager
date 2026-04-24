<#
.SYNOPSIS
  鎵撳寘 WFAM 鍙戣鐗堬紙win-x64锛夛紝鐢熸垚涓や釜 zip锛?
    1) WFAM-<ver>-win-x64.zip                 (渚濊禆 .NET 10 妗岄潰杩愯鏃?
    2) WFAM-<ver>-win-x64-self-contained.zip  (鍐呯疆杩愯鏃讹紝寮€绠卞嵆鐢?

.NOTES
  - 浣跨敤瑙ｅ喅鏂规鏍圭洰褰曠殑 Directory.Build.props 涓殑 <Version>銆?
  - 鍚屾椂鍙戝竷涓荤▼搴忎笌 Helper锛屽啀鍚堝苟鍒板悓涓€涓洰褰曞悗鍘嬬缉銆?
  - 涓嶅寘鍚?.pdb銆?
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$sln = Join-Path $root 'WFAM.sln'
$appProj = Join-Path $root 'src/WFAM.App/WFAM.App.csproj'
$helperProj = Join-Path $root 'src/WFAM.Helper/WFAM.Helper.csproj'
$propsPath = Join-Path $root 'Directory.Build.props'

# 璇诲彇鐗堟湰鍙凤紙鍙?<Version> 鑺傜偣锛?
[xml]$props = Get-Content -LiteralPath $propsPath
$version = $props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "鏈湪 Directory.Build.props 涓壘鍒?<Version>銆?
}
Write-Host "Packaging WFAM v$version ($Runtime)" -ForegroundColor Cyan

if (Test-Path $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
$null = New-Item -ItemType Directory -Path $OutputRoot -Force

function Publish-One {
    param(
        [Parameter(Mandatory)] [string]$Project,
        [Parameter(Mandatory)] [string]$OutDir,
        [Parameter(Mandatory)] [bool]$SelfContained
    )
    $args = @(
        'publish', $Project,
        '-c', $Configuration,
        '-r', $Runtime,
        '-o', $OutDir,
        '--nologo',
        "/p:SelfContained=$SelfContained",
        "/p:PublishSingleFile=false",
        "/p:DebugType=none",
        "/p:DebugSymbols=false",
        "/p:IncludeNativeLibrariesForSelfExtract=true",
        "/p:CopyOutputSymbolsToPublishDirectory=false",
        "/p:CopyDebugSymbolsToPublishDirectory=false"
    )
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $Project (self-contained=$SelfContained)" }
}

function Pack-Variant {
    param(
        [Parameter(Mandatory)] [bool]$SelfContained,
        [Parameter(Mandatory)] [string]$ZipPath
    )

    $stage = Join-Path $OutputRoot ("stage-" + ([guid]::NewGuid().ToString('N')))
    $null = New-Item -ItemType Directory -Path $stage -Force

    Publish-One -Project $appProj    -OutDir $stage -SelfContained:$SelfContained
    Publish-One -Project $helperProj -OutDir $stage -SelfContained:$SelfContained

    # 鍏滃簳鍒犻櫎娈嬬暀 pdb锛堟煇浜?SDK 琛屼负涓嬩粛鍙兘澶嶅埗锛?
    Get-ChildItem -LiteralPath $stage -Recurse -Include *.pdb -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    if (Test-Path $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $ZipPath -CompressionLevel Optimal

    Remove-Item -LiteralPath $stage -Recurse -Force
    $size = [math]::Round(((Get-Item $ZipPath).Length / 1MB), 2)
    Write-Host ("  -> {0}  ({1} MB)" -f $ZipPath, $size) -ForegroundColor Green
}

Write-Host "[1/2] Framework-dependent build..." -ForegroundColor Yellow
$zipFx  = Join-Path $OutputRoot ("WFAM-$version-$Runtime.zip")
Pack-Variant -SelfContained:$false -ZipPath $zipFx

Write-Host "[2/2] Self-contained build..." -ForegroundColor Yellow
$zipSc  = Join-Path $OutputRoot ("WFAM-$version-$Runtime-self-contained.zip")
Pack-Variant -SelfContained:$true -ZipPath $zipSc

Write-Host "`nDone. Artifacts in: $OutputRoot" -ForegroundColor Cyan
