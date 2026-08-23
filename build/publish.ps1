<#
.SYNOPSIS
    Builds the release artifacts: a self-contained publish, a portable zip, and an MSI.

.DESCRIPTION
    Produces everything in artifacts/. Nothing is installed and nothing outside the repository is
    touched — run the MSI yourself when you are ready.

    The publish is self-contained on purpose. Asking somebody to find and install the .NET Desktop
    Runtime before a tray utility will run is a bad first five minutes, and the size difference is
    a download, not a decision.

.PARAMETER Configuration
    Build configuration. Release unless you are debugging the packaging itself.

.PARAMETER Version
    Overrides the version stamped into the assemblies and the MSI. Defaults to the VersionPrefix
    in Directory.Build.props.

.PARAMETER SkipInstaller
    Produce only the portable build. Useful when the WiX toolset is not installed.

.EXAMPLE
    ./build/publish.ps1
    ./build/publish.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src/FateTakesYouHome/FateTakesYouHome.csproj'
$artifacts = Join-Path $repoRoot 'artifacts'
$publishDir = Join-Path $artifacts 'publish'

function Write-Step($message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

# ---------------------------------------------------------------- version

if (-not $Version) {
    $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
    if ($props -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
        $Version = $Matches[1]
    } else {
        throw 'Could not read VersionPrefix from Directory.Build.props. Pass -Version instead.'
    }
}

# Windows Installer compares only the first three fields of a version, and rejects anything with
# a pre-release suffix, so the MSI gets a plain numeric form.
$msiVersion = ($Version -split '-')[0]

Write-Host "Fate Takes You Home $Version ($Configuration)" -ForegroundColor White

# ---------------------------------------------------------------- clean

Write-Step 'Cleaning previous artifacts'

if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

# ---------------------------------------------------------------- icon

Write-Step 'Regenerating the brand mark'

# Run before the build: the application icon is a compile input.
dotnet run --project (Join-Path $repoRoot 'build/FateIconGen/FateIconGen.csproj') `
    --configuration $Configuration --verbosity quiet -- $repoRoot

if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }

# ---------------------------------------------------------------- test

Write-Step 'Running tests'

dotnet test (Join-Path $repoRoot 'tests/FateTakesYouHome.Tests/FateTakesYouHome.Tests.csproj') `
    --configuration $Configuration --nologo --verbosity quiet

if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Not packaging a build that does not pass.' }

# ---------------------------------------------------------------- publish

Write-Step 'Publishing self-contained win-x64'

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    --nologo `
    --verbosity quiet `
    -p:Version=$Version `
    -p:PublishReadyToRun=true

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# The PDBs roughly double the payload and are of no use to anyone installing this.
Get-ChildItem $publishDir -Filter '*.pdb' -Recurse | Remove-Item -Force

$publishSize = (Get-ChildItem $publishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("    {0:N0} files, {1:N1} MB" -f `
    (Get-ChildItem $publishDir -Recurse -File).Count, ($publishSize / 1MB))

# ---------------------------------------------------------------- portable zip

Write-Step 'Packing the portable build'

$portableZip = Join-Path $artifacts "FateTakesYouHome-$Version-portable-win-x64.zip"

# A note so somebody unzipping it knows what they have and how it differs from the installer.
$readme = @"
Fate Takes You Home $Version — portable build
=============================================

Run FateTakesYouHome.exe. Nothing is installed and nothing is written to Program Files.

Settings, themes and logs still live in your user profile, exactly as they do for an installed
copy, so the two share their configuration:

    %APPDATA%\VagueDustin Enterprises\Fate Takes You Home
    %LOCALAPPDATA%\VagueDustin Enterprises\Fate Takes You Home\Logs

Start with Windows works from here too — the app registers whatever path it is running from. If
you later move this folder, open Settings once and the registration is repaired.

Command line:
    FateTakesYouHome.exe            open the full window
    FateTakesYouHome.exe --panel    open the tray panel
    FateTakesYouHome.exe --tray     start into the tray with no window

Licence: GNU Affero General Public License v3 or later. See LICENSE.txt.
Source:  https://github.com/VagueDustin/fate-takes-you-home
"@

Set-Content -Path (Join-Path $publishDir 'README.txt') -Value $readme -Encoding UTF8
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $publishDir 'LICENSE.txt') -Force

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $portableZip -CompressionLevel Optimal

Write-Host ("    {0} ({1:N1} MB)" -f `
    (Split-Path -Leaf $portableZip), ((Get-Item $portableZip).Length / 1MB))

# ---------------------------------------------------------------- installer

if ($SkipInstaller) {
    Write-Step 'Skipping the installer'
    Write-Host "Artifacts in $artifacts"
    return
}

Write-Step 'Building the installer'

$wix = Get-Command wix -ErrorAction SilentlyContinue

if (-not $wix) {
    Write-Warning 'The WiX toolset is not on PATH. Install it with: dotnet tool install --global wix'
    Write-Warning 'Skipping the MSI. The portable build is still in artifacts/.'
    return
}

# WiX wants RTF for the licence page, and the AGPL is plain text. Wrapping it keeps a single
# source of truth rather than a second copy that drifts.
$licenseRtf = Join-Path $artifacts 'LICENSE.rtf'
$licenseText = Get-Content (Join-Path $repoRoot 'LICENSE') -Raw

$escaped = $licenseText -replace '\\', '\\\\' -replace '\{', '\{' -replace '\}', '\}'
$escaped = $escaped -replace "`r`n", '\par ' -replace "`n", '\par '

Set-Content -Path $licenseRtf -Encoding ASCII -Value @"
{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Consolas;}}
\fs16 $escaped
}
"@

$msi = Join-Path $artifacts "FateTakesYouHome-$Version-win-x64.msi"

& wix build `
    (Join-Path $repoRoot 'installer/wix/Package.wxs') `
    -define "ProductVersion=$msiVersion" `
    -define "PublishDir=$publishDir" `
    -define "LicenseRtf=$licenseRtf" `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Util.wixext `
    -arch x64 `
    -out $msi

if ($LASTEXITCODE -ne 0) { throw 'The installer build failed.' }

Write-Host ("    {0} ({1:N1} MB)" -f (Split-Path -Leaf $msi), ((Get-Item $msi).Length / 1MB))

Write-Step 'Done'
Write-Host "Artifacts in $artifacts" -ForegroundColor Green
Get-ChildItem $artifacts -File | ForEach-Object {
    Write-Host ("  {0,-52} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB))
}

Write-Host ''
Write-Host 'The MSI is unsigned, so SmartScreen will warn on first run.' -ForegroundColor Yellow
Write-Host 'Sign it with signtool before distributing it to anyone else.' -ForegroundColor Yellow
