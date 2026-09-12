# Removes PLC-Framework from this machine.
#
# **It asks, and the installer does not.** Installing everything is safe and reversible;
# deleting is where somebody wants a say - above all over the per-user folder, which holds
# PLC credentials and a GitHub token that no reinstall can bring back.
#
# It only ever offers what it actually found. A menu listing things that are not there
# teaches the reader to skim it, and skimming is how the wrong line gets picked.
#
# ASCII only, on purpose: PowerShell 5.1 reads a UTF-8 file with no BOM as ANSI.

[CmdletBinding()]
param(
    # Given any of these, nothing is asked. Without them the menu appears.
    [switch]$ProgramFiles,
    [switch]$UserData,
    [switch]$V20,
    [switch]$V21,
    [switch]$All,

    # Only for the machine-wide Add-In folder, when TIA is not under %ProgramFiles%.
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"

# paths.ps1 sits beside this script in the release archive and one level up in the repo, so
# it is looked for rather than assumed - and finding it cannot depend on itself.
$dir = $PSScriptRoot
while ($dir -and -not (Test-Path (Join-Path $dir "paths.ps1"))) {
    $parent = Split-Path $dir -Parent
    if ($parent -eq $dir) { throw "paths.ps1 not found at or above $PSScriptRoot." }
    $dir = $parent
}
. (Join-Path $dir "paths.ps1")

$launcher = Launcher $PSCommandPath

# ------------------------------------------------------------------ what is actually here

$found = @()

$install = InstallRoot
if (Test-Path $install) {
    $files = @(Get-ChildItem $install -File -ErrorAction SilentlyContinue)
    $found += @{
        Key   = "ProgramFiles"
        Path  = $install
        What  = "the applications"
        Note  = "{0} file(s)" -f $files.Count
        Kind  = "Folder"
    }
}

$user = UserRoot
if (Test-Path $user) {
    $found += @{
        Key   = "UserData"
        Path  = $user
        What  = "your data"
        Note  = "saved PLC credentials, your .env and your template - NOT RECOMMENDED"
        Kind  = "Folder"
    }
}

foreach ($t in Targets) {
    # Both scopes, not just the one the installer uses: somebody may have put the package in
    # the other folder by hand, and an uninstaller that ignores it leaves TIA still loading
    # an Add-In the user believes is gone.
    foreach ($scope in @("Machine", "User")) {
        $file = Join-Path (AddInFolder $t.Portal $scope $InstallRoot) $t.Package

        if (Test-Path $file) {
            $found += @{
                Key   = $t.Portal.Replace("Portal ", "")
                Path  = $file
                What  = "the Add-In for TIA " + $t.Portal.Replace("Portal ", "")
                Note  = $(if ($scope -eq "Machine") { "machine-wide" } else { "this user only" })
                Kind  = "File"
            }
        }
    }
}

if ($found.Count -eq 0) {
    Write-Host "Nothing to remove: PLC-Framework is not installed on this machine." -ForegroundColor Green
    Write-Host "   looked in $install"
    Write-Host "   looked in $user"
    exit 0
}

# ------------------------------------------------------------------------------ the choice

$asked = $ProgramFiles -or $UserData -or $V20 -or $V21 -or $All

if ($asked) {
    $chosen = @($found | Where-Object {
        $All -or
        ($ProgramFiles -and $_.Key -eq "ProgramFiles") -or
        ($UserData     -and $_.Key -eq "UserData")     -or
        ($V20          -and $_.Key -eq "V20")          -or
        ($V21          -and $_.Key -eq "V21")
    })
}
else {
    Write-Host "`nPLC-Framework - what would you like to remove?`n" -ForegroundColor Cyan

    for ($i = 0; $i -lt $found.Count; $i++) {
        $item = $found[$i]
        $colour = $(if ($item.Key -eq "UserData") { "Yellow" } else { "Gray" })

        Write-Host ("   [{0}] {1}" -f ($i + 1), $item.What) -ForegroundColor $colour
        Write-Host ("       {0}" -f $item.Path) -ForegroundColor DarkGray
        Write-Host ("       {0}" -f $item.Note) -ForegroundColor DarkGray
    }

    Write-Host "`n   Numbers separated by commas, 'all', or Enter to cancel."
    $answer = (Read-Host "  >").Trim()

    if ($answer -eq "") {
        Write-Host "`nCancelled. Nothing was removed." -ForegroundColor Green
        exit 0
    }

    if ($answer -eq "all") { $chosen = $found }
    else {
        $chosen = @()
        foreach ($part in $answer -split "[,\s]+") {
            if ($part -match "^\d+$" -and [int]$part -ge 1 -and [int]$part -le $found.Count) {
                $chosen += $found[[int]$part - 1]
            }
            elseif ($part -ne "") {
                Write-Host ("`n'{0}' is not one of the numbers listed. Nothing was removed." -f $part) -ForegroundColor Red
                exit 1
            }
        }
    }
}

if ($chosen.Count -eq 0) {
    Write-Host "`nNothing selected. Nothing was removed." -ForegroundColor Green
    exit 0
}

# --------------------------------------------------------------------------- the deleting

Write-Host ""
$failures = 0

foreach ($item in $chosen) {
    try
    {
        if ($item.Kind -eq "Folder") { Remove-Item $item.Path -Recurse -Force -ErrorAction Stop }
        else                         { Remove-Item $item.Path -Force -ErrorAction Stop }

        Write-Host ("   removed  {0}" -f $item.Path) -ForegroundColor Green
    }
    catch
    {
        $failures++

        # The two ordinary reasons, and they need different answers: a satellite still
        # running holds its own .exe, and TIA still open holds the package it loaded.
        Write-Host ("   FAILED   {0}" -f $item.Path) -ForegroundColor Red
        Write-Host ("            {0}" -f $_.Exception.Message) -ForegroundColor Red

        if ($item.Kind -eq "File") {
            Write-Host "            Close TIA Portal and run this again." -ForegroundColor Red
        }
        else {
            Write-Host "            Close any PLC-Framework window and run this again." -ForegroundColor Red
        }

        if (-not (IsElevated) -and $item.Path -like (Join-Path (ProgramFiles64) "*")) {
            Write-Host ("            Or right-click {0} and pick 'Run as administrator'." -f $launcher) -ForegroundColor Red
        }
    }
}

# What is deliberately left behind, said out loud - otherwise it reads as something the
# uninstaller forgot rather than something it decided.
if (-not ($chosen | Where-Object { $_.Key -eq "UserData" }) -and (Test-Path $user)) {
    Write-Host ("`n   kept     {0}" -f $user) -ForegroundColor DarkGray
    Write-Host "            your PLC credentials, .env and template" -ForegroundColor DarkGray
}

Write-Host ("`n   The configuration inside each TIA project is untouched: it belongs to the project.") -ForegroundColor DarkGray

if ($failures -gt 0) { exit 1 }
exit 0
