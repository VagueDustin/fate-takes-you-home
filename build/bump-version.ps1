<#
.SYNOPSIS
    Moves the version on for a release: Directory.Build.props and the changelog, together.

.DESCRIPTION
    A release is a commit on main that changes VersionPrefix; see release.ps1. This makes that
    change the same way every time. The new version goes into Directory.Build.props. The
    changelog's Unreleased section becomes the new version's, dated, with an empty Unreleased
    above it for whatever comes next, and the comparison links at the foot of the changelog move
    on to match.

    Nothing is committed. A release deserves a sentence of its own under its heading saying what
    it is about, which only a person can write. Then commit it as "Fate Takes You Home <version>"
    and push to main, and CI builds, tests and publishes it.

.PARAMETER Part
    Which number to move on: Minor for new features, Patch for fixes alone, Major when something
    that used to work no longer does.

.PARAMETER Version
    An exact version instead, for when the next one is not simply the next.

.PARAMETER Date
    The date for the new heading. Today, unless a release is being prepared ahead of time.

.EXAMPLE
    ./build/bump-version.ps1 -Part Minor
    ./build/bump-version.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$Part,
    [string]$Version,
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'

if ([bool]$Part -eq [bool]$Version) {
    throw 'Say -Part Major, Minor or Patch, or give an exact -Version.'
}

# ---------------------------------------------------------------- the numbers

$props = [IO.File]::ReadAllText($propsPath)

if ($props -notmatch '<VersionPrefix>(\d+)\.(\d+)\.(\d+)</VersionPrefix>') {
    throw 'VersionPrefix in Directory.Build.props is not a plain major.minor.patch.'
}

$current = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
[int]$major = $Matches[1]
[int]$minor = $Matches[2]
[int]$patch = $Matches[3]

$next = if ($Version) {
    $Version
} else {
    switch ($Part) {
        'Major' { "$($major + 1).0.0" }
        'Minor' { "$major.$($minor + 1).0" }
        'Patch' { "$major.$minor.$($patch + 1)" }
    }
}

# release.ps1 refuses these too, but only once the commit is pushed. Better to hear it now.
if ($next -notmatch '^\d+\.\d+\.\d+$') {
    throw "$next is not a plain major.minor.patch. The MSI keeps only those three numbers."
}

if ([version]$next -le [version]$current) {
    throw "$next is not newer than $current, the version already in Directory.Build.props."
}

# ---------------------------------------------------------------- the changelog

$text = [IO.File]::ReadAllText($changelogPath)
$newline = $text.Contains("`r`n") ? "`r`n" : "`n"
$lines = [System.Collections.Generic.List[string]]::new([string[]]($text -split '\r?\n'))

$unreleased = $lines.FindIndex({ param($line) $line.Trim() -eq '## [Unreleased]' })

if ($unreleased -lt 0) {
    throw 'CHANGELOG.md has no "## [Unreleased]" heading to turn into the release.'
}

# A release with nothing under it would publish empty notes, which release.ps1 treats as no notes.
$end = $lines.FindIndex($unreleased + 1, { param($line) $line.StartsWith('## [', [StringComparison]::Ordinal) })
$end = $end -lt 0 ? $lines.Count : $end
$recorded = @($lines.GetRange($unreleased + 1, $end - $unreleased - 1) | Where-Object { $_.Trim() }).Count

if ($recorded -eq 0) {
    throw 'Nothing is recorded under [Unreleased] in CHANGELOG.md, so there is nothing to release.'
}

$lines.InsertRange($unreleased + 1, [string[]]@('', "## [$next] ($Date)"))

# The foot of the file compares each version with the one before. Unreleased moves on to compare
# with the new version, and the new version gets a line of its own beneath it.
$unreleasedLink = '^\[Unreleased\]: (.+)/compare/.+\.\.\.HEAD$'
$link = $lines.FindIndex({ param($line) $line -match $unreleasedLink })

# Matched again here rather than read from the search: the predicate runs in a scope of its own,
# and its $Matches never reaches this one.
if ($link -ge 0 -and $lines[$link] -match $unreleasedLink) {
    $base = $Matches[1]
    $lines[$link] = "[Unreleased]: $base/compare/v$next...HEAD"
    $lines.Insert($link + 1, "[$next]: $base/compare/v$current...v$next")
}

# ---------------------------------------------------------------- writing

$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($changelogPath, ($lines -join $newline), $utf8)
[IO.File]::WriteAllText(
    $propsPath,
    ($props -replace '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$next</VersionPrefix>"),
    $utf8)

Write-Host "Moved on from $current to $next." -ForegroundColor White
Write-Host ''
Write-Host "Next: write a sentence under '## [$next] ($Date)' in CHANGELOG.md saying what the release"
Write-Host "is about, commit both files as ""Fate Takes You Home $next"", and push to main. CI builds,"
Write-Host 'tests and publishes it, and installs are offered it within a day.'
