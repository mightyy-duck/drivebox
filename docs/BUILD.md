# Building PartiChart

## Prerequisites
* Windows 10/11 x64
* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* For the installer: the WiX Toolset 5 command line tool (free, MS-RL)

```powershell
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.UI.wixext/5.0.2
```

## Run from source
```powershell
dotnet run -c Release
# open a folder directly, pick a theme, or start on the apps page
dotnet run -c Release -- "C:\Program Files" --dark
```

## Publish a single, self-contained exe
```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts
```
The result is `artifacts\PartiChart.exe` (about 60 MB, includes the .NET runtime).

## Build the MSI
```powershell
.\installer\build-installer.ps1
```
This publishes the exe (unless `-SkipPublish` is given), builds `artifacts\PartiChart-<version>-x64.msi` and a portable zip, and writes `SHA256SUMS.txt`. The version comes from `<Version>` in `PartiChart.csproj`.

The installer is per-machine (`C:\Program Files\PartiChart`), adds Start menu and desktop shortcuts, uses the standard "install directory" wizard with the MIT license page, and upgrades older versions in place (fixed UpgradeCode in `installer\Package.wxs`, do not change it).

## Release checklist
1. Bump `<Version>` / `<AssemblyVersion>` / `<FileVersion>` / `<InformationalVersion>` in `PartiChart.csproj` and the `Version` in `installer\Package.wxs`.
2. Update `CHANGELOG.md`.
3. `.\installer\build-installer.ps1`
4. Commit, tag (`git tag v1.0.1`), push, then create the GitHub release and attach the files from `artifacts\`.
```powershell
gh release create v1.0.1 artifacts\PartiChart-1.0.1-x64.msi artifacts\PartiChart-1.0.1-win-x64-portable.zip artifacts\SHA256SUMS.txt `
  --title "PartiChart 1.0.1" --notes-file CHANGELOG.md
```

## Command-line options
| Option | Effect |
|---|---|
| `<folder>` | scan and open this folder on start |
| `--dark` / `--light` | force a theme for this run |
| `--apps` | open the Apps & games page on start |
