<#
  Builds the release files into .\artifacts :
    PartiChart-<version>-x64.msi
    PartiChart-<version>-win-x64-portable.zip
    SHA256SUMS.txt
  Requires: .NET 10 SDK and WiX 5  (dotnet tool install --global wix --version 5.0.2
                                     wix extension add -g WixToolset.UI.wixext/5.0.2)
#>
param([switch]$SkipPublish)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"

$csproj = Get-Content (Join-Path $root 'PartiChart.csproj') -Raw
$version = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw 'Could not read <Version> from PartiChart.csproj' }
$wxs = Get-Content (Join-Path $PSScriptRoot 'Package.wxs') -Raw
if ($wxs -notmatch "Version=`"$([regex]::Escape($version)).0`"") { throw "installer\Package.wxs Version must be $version.0" }

$art = Join-Path $root 'artifacts'
if (-not $SkipPublish) {
    if (Test-Path $art) { Remove-Item $art -Recurse -Force }
    dotnet publish -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $art
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
    Get-ChildItem $art -Filter *.pdb | Remove-Item -Force
}

$msi = Join-Path $art "PartiChart-$version-x64.msi"
wix build -arch x64 -ext WixToolset.UI.wixext -b $PSScriptRoot -d "SourceDir=$art" -d "IconFile=$(Join-Path $root 'Assets\app.ico')" -o $msi (Join-Path $PSScriptRoot 'Package.wxs')
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }
Get-ChildItem $art -Filter *.wixpdb | Remove-Item -Force

$zip = Join-Path $art "PartiChart-$version-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $art 'PartiChart.exe'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') -DestinationPath $zip

$sums = Join-Path $art 'SHA256SUMS.txt'
Get-ChildItem $art -Include *.msi, *.zip -Recurse | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content $sums -Encoding ascii
Write-Host "`nDone:"; Get-ChildItem $art | Where-Object { $_.Extension -in '.msi', '.zip', '.txt' } | Format-Table Name, @{n='MB'; e={ [math]::Round($_.Length / 1MB, 1) }}
