<#
.SYNOPSIS
    Builds RapidFireKeys as one self-contained executable for release.

.DESCRIPTION
    Produces a single RapidFireKeys.exe with the .NET runtime inside it, so
    installing the app is putting one file in a folder. Nothing else has to be
    present on the machine.

    The file name stays RapidFireKeys.exe for every version: the README links
    and the in-app updater both look for an .exe asset on the release, and a
    stable name means a manual upgrade is a plain overwrite.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SponsorUrl "https://github.com/sponsors/levinium?frequency=one-time&amount={amount}"
    .\publish.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    # Where the exe lands. Defaults to dist/ off the repo root.
    [string] $OutputDirectory,

    # Publish without running the tests first. For a local trial build only - the
    # release workflow never passes this.
    [switch] $SkipTests,

    # Where "Support RapidFireKeys" sends people. Left unset - as it is for every
    # build from a clean checkout - the app has no support menu item at all, so a
    # fork cannot ship one asking on someone else's behalf.
    #
    # An {amount} placeholder turns the ask into a picker, and is only worth
    # including where the destination actually reads the amount out of the URL.
    # GitHub Sponsors does, via ?frequency=one-time&amount=N.
    [string] $SponsorUrl
)

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$solution = Join-Path $root 'RapidFireKeys\RapidFireKeys.sln'
$csproj   = Join-Path $root 'RapidFireKeys\RapidFireKeys\RapidFireKeys.csproj'
$staging  = Join-Path $root 'RapidFireKeys\RapidFireKeys\bin\Release\net8.0-windows\win-x64\publish'

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $csproj." }

# Tests before anything is built for release. What they cover - reading
# hand-written configs, whether a published version is newer than the running
# one, whether a download matches its checksum - all fail silently in the app.
if (-not $SkipTests) {
    Write-Host 'Running tests...'
    dotnet test $solution -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not publishing.' }
}

Write-Host "Publishing RapidFireKeys v$version..."

# [string[]] and the leading comma are both load-bearing. An `if` used as an
# expression yields its branch's OUTPUT, and PowerShell enumerates a one-element
# array down to a bare string on the way out - after which `@sponsor` splats a
# string, which it does one character at a time.
[string[]] $sponsor = if ($SponsorUrl) { , "-p:SponsorUrl=$SponsorUrl" } else { @() }

if ($SponsorUrl) {
    Write-Host "  Support menu item enabled: $SponsorUrl"
} else {
    Write-Host '  No support menu item (pass -SponsorUrl to include one).'
}

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $staging `
    --nologo `
    @sponsor

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
}

$exe = Join-Path $OutputDirectory 'RapidFireKeys.exe'
Copy-Item (Join-Path $staging 'RapidFireKeys.exe') $exe -Force

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Built $exe (v$version, $size MB)"
