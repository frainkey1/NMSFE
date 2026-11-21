param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$') {
    throw "Version must look like 1.2.3 or 1.2.3-preview.1."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packagesRoot = Join-Path $repoRoot "Build\Packages"
New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null

$packageName = "NMSFE-$Version-win-x64"
$packageDir = [IO.Path]::GetFullPath((Join-Path $packagesRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $packagesRoot "$packageName.zip"))
$packagesPrefix = [IO.Path]::GetFullPath($packagesRoot).TrimEnd('\') + '\'

if (-not $packageDir.StartsWith($packagesPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $zipPath.StartsWith($packagesPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output resolved outside Build\Packages."
}

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

dotnet publish (Join-Path $repoRoot "Desktop\Desktop.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $packageDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$readme = @"
No Man's Sky File Editor (NMSFE) $Version — Windows x64

GETTING STARTED
1. Extract the ZIP into a folder.
2. Double-click NMSFE.exe.
3. Keep the Resources folder beside NMSFE.exe; it contains game data and images.
4. Open a save or select a detected save slot, then choose Load. Choose Save to write edits.

The .NET runtime is bundled in NMSFE.exe. Windows extracts a small set of runtime files to its temporary .NET folder when the app starts.

Project: https://github.com/frainkey1/NMSFE
License: AGPL-3.0
Derived engine: NMSE by vectorcmdr
"@
Set-Content -LiteralPath (Join-Path $packageDir "README-FIRST.txt") -Value $readme -Encoding utf8

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Output "Package folder: $packageDir"
Write-Output "ZIP: $zipPath"
