# Builds the release archive from what step1 staged. Run this on the development PC.
#
# THIS CANNOT RUN IN CI, and that is not an oversight to fix. The .addin packages are made
# by Siemens' Publisher against Openness assemblies that are licensed and cannot be
# redistributed, so they live outside this repository and no hosted runner has them.
# Releases are cut from a machine that does.
#
# ASCII only, on purpose: PowerShell 5.1 reads a UTF-8 file with no BOM as ANSI.

[CmdletBinding()]
param(
    # Skip the rebuild and package whatever .deploy\ already holds. For a second attempt at
    # the same build; never for cutting a real release.
    [switch]$SkipStage
)

$ErrorActionPreference = "Stop"

$scripts = Split-Path $PSScriptRoot -Parent
. (Join-Path $scripts "paths.ps1")

$repo    = RepoRoot $PSScriptRoot
$deploy  = Join-Path $repo ".deploy"
$release = Join-Path $repo ".release"

# ------------------------------------------------------------------------- the version

# Read from the one place that states it, rather than typed here where it would drift the
# first time somebody bumps a release and forgets this file.
$props = Join-Path $repo "Directory.Build.props"
$version = ([xml](Get-Content $props)).Project.PropertyGroup.Version

if (-not $version) { throw "No <Version> found in $props." }
$version = $version.ToString().Trim()

# The v belongs on the archive, where a person reads it. It cannot go in Directory.Build.props
# - NuGet refuses to parse it - nor in the two Config.xml, whose schema allows digits and
# dots only. So it is added here and nowhere else.
#
# And so is the host. PLC-Framework is the product; TIA Portal is one host it plugs into, and
# the day a second one exists they will still share Core, UI.Shared and the config editor -
# so the brand stays as it is and the ARCHIVE says what this download is for. Naming the
# product after one host would be a rename later; naming the archive is a line here.
$name = "PLC-Framework-for-TIA-v$version"

Write-Host "packaging $name" -ForegroundColor Cyan

# ------------------------------------------------------------------------- the payload

if (-not $SkipStage) {
    & (Join-Path $scripts "deploy\step1-stage-host.ps1") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Staging failed; nothing was packaged." }
}

foreach ($needed in @("bin", "addins")) {
    if (-not (Test-Path (Join-Path $deploy $needed))) {
        throw "Nothing staged at $deploy. Run scripts\step1-stage-host.ps1 first."
    }
}

$packages = @(Get-ChildItem (Join-Path $deploy "addins") -Filter "*.addin")
if ($packages.Count -eq 0) {
    # The Publisher target is skipped when the Siemens tools are missing, and a release
    # without the Add-Ins is not a release - it is a folder of satellites nobody can launch.
    throw "No .addin packages were staged. The Siemens Publisher is required to cut a release."
}

# Cleared each time: a file left from a previous version would ship inside this one, and
# nobody opens an archive to audit it.
Remove-Item $release -Recurse -Force -ErrorAction SilentlyContinue
$staging = Join-Path $release $name
New-Item -ItemType Directory -Force $staging | Out-Null

Copy-Item (Join-Path $deploy "bin")    $staging -Recurse
Copy-Item (Join-Path $deploy "addins") $staging -Recurse

# ------------------------------------------------------------------------- the installer

# The same scripts the maintainer runs, under the names a stranger expects. They are copied
# rather than rewritten: a second implementation of the install would be the one nobody
# tests. step2 names itself in its own messages, so it tells the reader to right-click
# install.cmd once it is called that.
#
# The PowerShell goes one level down and only the two .cmd launchers stay in plain sight, so
# what somebody sees on unpacking is what they should double-click. install.ps1 finds the
# payload one level up by looking for bin\ and addins\, which is why nothing has to be told
# where it is.
$inner = Join-Path $staging ".installer"
New-Item -ItemType Directory -Force $inner | Out-Null

Copy-Item (Join-Path $scripts "paths.ps1")            $inner
Copy-Item (Join-Path $PSScriptRoot "uninstall.ps1")   $inner
Copy-Item (Join-Path $scripts "deploy\step2-deploy-vm.ps1") (Join-Path $inner "install.ps1")

Copy-Item (Join-Path $PSScriptRoot "uninstall.cmd")      $staging
Copy-Item (Join-Path $scripts "deploy\step2-deploy-vm.cmd") (Join-Path $staging "install.cmd")

# Each wrapper names its own .ps1 beside itself; in the archive that file moved, so both
# have to be pointed into .installer\ - and install.cmd at the renamed one.
foreach ($pair in @(@{ Cmd = "install.cmd"; Ps = "install.ps1"; Was = "step2-deploy-vm.ps1" },
                    @{ Cmd = "uninstall.cmd"; Ps = "uninstall.ps1"; Was = "uninstall.ps1" })) {

    $cmd = Join-Path $staging $pair.Cmd
    (Get-Content $cmd -Raw).Replace('%~dp0' + $pair.Was, '%~dp0.installer\' + $pair.Ps) |
        Set-Content $cmd -Encoding Ascii
}

Copy-Item (Join-Path $repo "LICENSE")                $staging
Copy-Item (Join-Path $repo "THIRD-PARTY-NOTICES.md") $staging

# ------------------------------------------------------------------------- the read-me

# Short on purpose. Somebody who just unpacked an archive wants the two commands and the one
# reason the Add-In will not appear; everything else is a link away.
$readme = @"
# PLC-Framework for TIA Portal v$version

A TIA Portal Add-In and the desktop apps it launches.
https://github.com/PLC-Framework/tia-portal-addins

## Install

Right-click **install.cmd** and pick "Run as administrator".

It copies the applications to C:\Program Files\PLC-Framework and puts each .addin in the
folder its TIA version reads. Nothing is asked; every destination is printed.

## Before it works

1. Your Windows user must belong to the local **"Siemens TIA Openness"** group, then sign
   out and back in. Without it the Add-In does not appear and TIA says nothing about why.
   This is the single most common reason for "nothing happened".
2. The package is **not signed**. Open TIA Portal, go to Options -> Add-Ins, and enable it.
3. Restart TIA Portal.

Right-click a project in the tree: there should be a **Config. Editor** entry.

## Uninstall

Run **uninstall.cmd**. It asks what to remove and only lists what it actually finds.

Your saved PLC credentials, your .env and your template live in
%LOCALAPPDATA%\PLC-Framework and are kept unless you ask for them to go. The configuration
inside each TIA project is never touched: it belongs to the project.

## Licence

MIT - see LICENSE. Third-party components under their own terms - see
THIRD-PARTY-NOTICES.md.

Not affiliated with or endorsed by Siemens. No Siemens binary is redistributed here.
"@

# No BOM: every other document here is written without one, and a README is the first
# thing a stranger opens.
[IO.File]::WriteAllText((Join-Path $staging "README.md"), $readme, (New-Object Text.UTF8Encoding $false))

# ------------------------------------------------------------------------- the archive

$zip = Join-Path $release "$name.zip"
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)

# Collected and then written, rather than piped: Write-Host goes straight to the host while
# pipeline output is buffered, so a bare pipeline here prints the listing AFTER the summary
# it is supposed to precede.
$listing = @(Get-ChildItem $staging -Recurse -File -Force |
             ForEach-Object { $_.FullName.Substring($staging.Length + 1) } |
             Sort-Object)

Write-Host "`ncontents" -ForegroundColor Cyan
foreach ($line in $listing) { Write-Host "   $line" }

# The folder was scaffolding for the archive and nothing reads it afterwards. Left behind it
# becomes a second copy of the release that somebody eventually edits, ships or mistakes for
# the real one - and the listing above already says what went in.
Remove-Item $staging -Recurse -Force

Write-Host "`n$zip" -ForegroundColor Green
Write-Host ("   {0} MB, {1} files" -f $size, $listing.Count)
Write-Host "`nTag the commit v$version so the archive and the history agree." -ForegroundColor DarkGray

exit 0
