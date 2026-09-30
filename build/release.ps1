<#
.SYNOPSIS
    Decides whether the version on this commit is due a release, and publishes it.

.DESCRIPTION
    A release is a version bump. Nobody pushes a tag: when a commit reaches main carrying a
    VersionPrefix that has never been released, CI builds, tests and packages it as it would any
    commit, and then this script publishes those same packages as the release, tagging the commit
    they were built from. What a release carries is byte for byte what CI tested, and the tag
    cannot disagree with the version inside the installer, because it is made from it.

    -Plan runs on every build, pull requests included, and fails on a bump that could not be
    released cleanly, so the problem shows on the pull request rather than after the merge.
    -Publish runs on main once the packages exist, and checks everything again first, because
    another run may have published the same version in the meantime.

    Only reads from GitHub, except -Publish without -WhatIf.

.PARAMETER Plan
    Work out the version and whether it is due a release. Under GitHub Actions the answer is
    written to the step's outputs as "version" and "release".

.PARAMETER Publish
    Publish the packages in -Artifacts as the release of this commit, if it is due one.

.PARAMETER Artifacts
    The folder holding the MSI and portable zip the package job built.

.PARAMETER Version
    Pretend Directory.Build.props says this, to try the checks against the real releases.

.EXAMPLE
    ./build/release.ps1 -Plan
    ./build/release.ps1 -Plan -Version 0.4.0
    ./build/release.ps1 -Publish -Artifacts artifacts -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Plan,
    [switch]$Publish,
    [string]$Artifacts = 'artifacts',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$inActions = $env:GITHUB_ACTIONS -eq 'true'

# Outside Actions, gh and git work out the repository from the checkout's own remote.
$repoArgs = if ($env:GITHUB_REPOSITORY) { @('--repo', $env:GITHUB_REPOSITORY) } else { @() }
$commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git -C $repoRoot rev-parse HEAD).Trim() }

$installNotes = @'
## Install

Run the MSI. It upgrades an existing install in place, keeping your pins, theme and
token. The portable zip needs nothing installed: unzip and run.

The MSI is unsigned, so SmartScreen will show its warning on first run.
'@

# Under Actions the reason also becomes an annotation, so a pull request with a bump that cannot
# be released says why on its checks, not only in a log.
function Stop-Release([string]$message) {
    if ($inActions) {
        Write-Host "::error title=Release::$message"
    } else {
        Write-Host $message -ForegroundColor Red
    }

    exit 1
}

function Get-ProjectVersion {
    $props = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw

    if ($props -notmatch '<VersionPrefix>([^<]+)</VersionPrefix>') {
        Stop-Release 'Could not read VersionPrefix from Directory.Build.props.'
    }

    return $Matches[1].Trim()
}

# The notes for a version: everything under its heading, up to the next version's.
function Get-ChangelogSection([string]$version) {
    # Compared as a plain prefix rather than a pattern. The heading is "## [0.4.0] (2026-09-30)",
    # and in a pattern the brackets turn into a character class unless they are escaped, which
    # fails as a silent no-match.
    $heading = "## [$version]"
    $section = [System.Collections.Generic.List[string]]::new()
    $inside = $false

    foreach ($line in @(Get-Content (Join-Path $repoRoot 'CHANGELOG.md'))) {
        if ($inside -and $line.StartsWith('## [', [StringComparison]::Ordinal)) {
            break
        }

        if ($inside) {
            $section.Add($line)
        } elseif ($line.StartsWith($heading, [StringComparison]::Ordinal)) {
            $inside = $true
        }
    }

    return ($section -join "`n").Trim()
}

# Every release, drafts too where this token can see them. Listed rather than looked up one tag at
# a time, because to gh "there is no such release" and "could not ask" are both exit code 1.
function Get-Releases {
    $json = gh release list @repoArgs --limit 1000 --json tagName,isDraft,isPrerelease

    if ($LASTEXITCODE -ne 0) {
        Stop-Release 'Could not list the releases on GitHub.'
    }

    return @($json | ConvertFrom-Json)
}

# The commit a tag points at, or null when there is no such tag. The "^{}" form peels an annotated
# tag down to its commit; a lightweight tag has no such line and is the commit itself.
function Get-TagCommit([string]$tag) {
    $lines = @(git -C $repoRoot ls-remote --tags origin "refs/tags/$tag" "refs/tags/$tag^{}")

    if ($LASTEXITCODE -ne 0) {
        Stop-Release "Could not ask the remote whether $tag exists."
    }

    $peeled = @($lines | Where-Object { $_ -match '\^\{\}$' })
    $line = if ($peeled.Count -gt 0) { $peeled[0] } elseif ($lines.Count -gt 0) { $lines[0] } else { $null }

    return $line ? ($line -split '\s+')[0] : $null
}

# Whether this version is due a release. Stops on anything that could not be released cleanly.
function Get-ReleasePlan([string]$version) {
    # The MSI keeps only these three numbers (see publish.ps1), so a pre-release and the build after
    # it would share an installer version, and Windows would not upgrade from one to the other.
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        $why = if ($version -match '^\d+\.\d+\.\d+-') {
            ' A pre-release and the build after it would share one MSI version, and Windows would ' +
            'not upgrade from one to the other.'
        } else {
            ''
        }

        Stop-Release "VersionPrefix '$version' is not a plain major.minor.patch, such as 0.4.0.$why"
    }

    $tag = "v$version"
    $releases = Get-Releases
    $existing = @($releases | Where-Object { $_.tagName -eq $tag })

    if ($existing.Count -gt 0) {
        # A failed upload normally cleans up its own draft; one left behind would otherwise stop
        # the version ever being published, while every run reported that it already had been.
        if ($existing[0].isDraft) {
            Stop-Release ("A draft release for $tag exists, probably left by an attempt that " +
                'failed part way. Delete the draft on GitHub, then re-run this workflow.')
        }

        return [pscustomobject]@{ Version = $version; Release = $false; Reason = "$version is already released." }
    }

    [version[]]$published = @($releases |
        Where-Object { -not $_.isDraft -and -not $_.isPrerelease } |
        ForEach-Object { $_.tagName.TrimStart('v', 'V') } |
        Where-Object { $_ -match '^\d+\.\d+\.\d+$' })

    $latest = $published | Sort-Object -Descending | Select-Object -First 1

    # The app offers only a newer version than it has, so this one would reach nobody, and it would
    # still become the "latest" release that a new install downloads.
    if ($latest -and [version]$version -le $latest) {
        Stop-Release ("VersionPrefix is $version, which is not newer than the latest release, " +
            "$latest. Every install already has something newer, and new installs would be given this.")
    }

    if (-not (Get-ChangelogSection $version)) {
        Stop-Release ("VersionPrefix is $version, which has not been released, but CHANGELOG.md " +
            "has no '## [$version]' section to publish as its notes. ./build/bump-version.ps1 " +
            'moves the version and the changelog on together.')
    }

    # A tag made by hand would be used as it stands, and the release would then name one commit
    # while carrying packages built from another.
    $tagCommit = Get-TagCommit $tag

    if ($tagCommit -and $tagCommit -ne $commit) {
        Stop-Release ("The tag $tag already exists, at $tagCommit, but this is $commit. Delete " +
            'the tag if it was made by mistake, or move VersionPrefix on.')
    }

    return [pscustomobject]@{ Version = $version; Release = $true; Reason = "$version has not been released." }
}

# ---------------------------------------------------------------- what is due

if ($Plan -eq $Publish) {
    Stop-Release 'Say -Plan or -Publish.'
}

if (-not $Version) {
    $Version = Get-ProjectVersion
}

$due = Get-ReleasePlan $Version

if ($Plan) {
    $verdict = if ($due.Release) {
        "Version $Version is due a release. On main, CI publishes it once it is built and tested."
    } else {
        "Nothing to release: $($due.Reason) Changing VersionPrefix is what makes a release."
    }

    Write-Host $verdict

    if ($env:GITHUB_OUTPUT) {
        Add-Content -Path $env:GITHUB_OUTPUT -Value "version=$Version"
        Add-Content -Path $env:GITHUB_OUTPUT -Value "release=$($due.Release.ToString().ToLowerInvariant())"
    }

    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $verdict
    }

    exit 0
}

# ---------------------------------------------------------------- publishing

if (-not $due.Release) {
    Write-Host "Nothing to publish: $($due.Reason)"
    exit 0
}

$tag = "v$Version"
$msi = Join-Path $Artifacts "FateTakesYouHome-$Version-win-x64.msi"
$zip = Join-Path $Artifacts "FateTakesYouHome-$Version-portable-win-x64.zip"

# The packages carry their version in their names. If these are missing, what was built is not
# the version the release would be called, and publishing it would ship one under another's name.
foreach ($package in $msi, $zip) {
    if (-not (Test-Path $package)) {
        Stop-Release "The packages do not include $(Split-Path -Leaf $package), so they were not built as $Version."
    }
}

$notesFile = Join-Path ([IO.Path]::GetTempPath()) "fate-release-notes-$Version.md"
$notes = (Get-ChangelogSection $Version) + "`n`n" + $installNotes
[IO.File]::WriteAllText($notesFile, $notes, [Text.UTF8Encoding]::new($false))

Write-Host "Release notes for ${tag}:"
Write-Host $notes
Write-Host ''

if ($PSCmdlet.ShouldProcess("$tag at $commit, with $(Split-Path -Leaf $msi) and $(Split-Path -Leaf $zip)", 'Publish the release')) {
    gh release create $tag @repoArgs --target $commit --title "Fate Takes You Home $Version" --notes-file $notesFile $msi $zip

    if ($LASTEXITCODE -ne 0) {
        Stop-Release "gh could not publish $tag."
    }

    Write-Host "Published $tag."

    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "Published Fate Takes You Home $Version as $tag."
    }
}
