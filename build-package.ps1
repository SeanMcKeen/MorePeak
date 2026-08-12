<#
.SYNOPSIS
    Builds MorePeak and stages a Thunderstore package.

.DESCRIPTION
    The version is read from MorePeak/MorePeak.csproj and used for the plugin
    attribute, the manifest, the folder name and the zip, so those can never
    disagree. The script refuses to package if the compiled DLL does not match
    the csproj version or if CHANGELOG.md has no entry for it.

.PARAMETER SkipBuild
    Package the existing Release output instead of rebuilding.

.PARAMETER InstallTo
    Also copy the built DLL into a mod manager profile's plugin folder, e.g.
    "$env:APPDATA\Thunderstore Mod Manager\DataFolder\PEAK\profiles\Default"
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$InstallTo
)

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$csproj   = Join-Path $root 'MorePeak\MorePeak.csproj'
$template = Join-Path $root 'thunderstore\template'
$dll      = Join-Path $root 'MorePeak\bin\Release\netstandard2.1\MorePeak.dll'

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $csproj" }
Write-Host "MorePeak $version" -ForegroundColor Cyan

if (-not $SkipBuild) {
    dotnet build $csproj -c Release | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}
if (-not (Test-Path $dll)) { throw "Missing build output: $dll" }

# The plugin attribute is generated from <Version>, so a mismatch here means the
# DLL on disk is stale relative to the csproj.
$built = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
$builtShort = "$($built.Major).$($built.Minor).$($built.Build)"
if ($builtShort -ne $version) {
    throw "DLL is $builtShort but csproj says $version. Rebuild without -SkipBuild."
}

$changelog = Get-Content (Join-Path $template 'CHANGELOG.md') -Raw
if ($changelog -notmatch ("(?m)^##\s+" + [regex]::Escape($version) + "(\s|$)")) {
    throw "thunderstore/template/CHANGELOG.md has no '## $version' section."
}

foreach ($doc in @((Join-Path $template 'README.md'), (Join-Path $root 'README.md'))) {
    if ((Get-Content $doc -Raw) -notmatch [regex]::Escape($version)) {
        Write-Warning "$doc does not mention $version - version banner may be stale."
    }
}

$outDir = Join-Path $root "thunderstore\MorePeak_$version"
$zip    = Join-Path $root "thunderstore\MorePeak_$version.zip"
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
if (Test-Path $zip)    { Remove-Item -Force $zip }

Copy-Item -Recurse $template $outDir
$manifestPath = Join-Path $outDir 'manifest.json'
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText(
    $manifestPath,
    ((Get-Content $manifestPath -Raw) -replace '__VERSION__', $version),
    $utf8)

$pluginDir = Join-Path $outDir 'BepInEx\plugins\MorePeak'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -Force $dll (Join-Path $pluginDir 'MorePeak.dll')

Compress-Archive -Path "$outDir\*" -DestinationPath $zip -Force

# Fail loudly rather than shipping a package whose manifest lies about its DLL.
$staged = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $pluginDir 'MorePeak.dll')).Version
$manifestVersion = (Get-Content $manifestPath -Raw | ConvertFrom-Json).version_number
if ($manifestVersion -ne $version -or "$($staged.Major).$($staged.Minor).$($staged.Build)" -ne $version) {
    throw "Packaged contents do not match $version."
}

Write-Host "Packaged $outDir" -ForegroundColor Green
Write-Host "Zipped    $zip"   -ForegroundColor Green

if ($InstallTo) {
    $dest = Join-Path $InstallTo 'BepInEx\plugins\smckeen-MorePeak'
    if (-not (Test-Path $dest)) { throw "Profile mod folder not found: $dest" }
    New-Item -ItemType Directory -Force -Path (Join-Path $dest 'MorePeak') | Out-Null
    Copy-Item -Force $dll (Join-Path $dest 'MorePeak\MorePeak.dll')
    Copy-Item -Force $manifestPath (Join-Path $dest 'manifest.json')
    Copy-Item -Force (Join-Path $outDir 'README.md')    (Join-Path $dest 'README.md')
    Copy-Item -Force (Join-Path $outDir 'CHANGELOG.md') (Join-Path $dest 'CHANGELOG.md')
    Write-Host "Installed to $dest" -ForegroundColor Green
}
