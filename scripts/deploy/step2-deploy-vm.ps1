# Installs what step1-stage-host.ps1 gathered. Run this INSIDE THE VM.
#
# It reports the timestamp of everything it installs, and that is not decoration: the
# failure this exists to prevent is testing a stale build and spending an afternoon
# debugging something already fixed. "It is deployed" has to be a fact you can read.
#
# ASCII only, on purpose: PowerShell 5.1 reads a UTF-8 file with no BOM as ANSI.

[CmdletBinding()]
param(
    # Only the Add-Ins, when nothing but a satellite changed.
    [switch]$AddInsOnly,

    # Only the satellites, when TIA is open and holding the packages.
    [switch]$ToolsOnly,

    # Forces BOTH versions into the same folder, overriding the per-version default in
    # $targets below:
    #
    #     Machine   C:\Program Files\Siemens\Automation\Portal V2x\AddIns      needs elevation
    #     User      %AppData%\Siemens\Automation\Portal V2x\UserAddIns         needs nothing
    #
    # Left unset - the normal case - each version goes where it goes on this station.
    [ValidateSet("Machine", "User")]
    [string]$Scope,

    # Only for Machine scope, and only when TIA is not under %ProgramFiles%. Point it at
    # the folder holding "Portal V20", not at the Portal folder itself.
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"

# paths.ps1 holds everything the installer and the uninstaller must agree on, but finding it
# cannot itself depend on it - so this one walk is written out here.
function FindUpPaths($from)
{
    $dir = $from
    while ($dir) {
        $candidate = Join-Path $dir "paths.ps1"
        if (Test-Path $candidate) { return $candidate }

        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }

    throw "paths.ps1 not found at or above $from."
}

# Shared with the uninstaller, which has to look in exactly the places this writes to.
# Beside this script in the release archive, one level up in the repo. Loaded here, before
# anything that needs it: StagingFolder below calls FindUp, which lives in there.
. (FindUpPaths $PSScriptRoot)

# Three places this runs from, and it works out which by LOOKING FOR THE PAYLOAD rather than
# by being told:
#
#   from the repo        scripts\deploy\step2-deploy-vm.ps1  ->  the repo's .deploy\
#   beside the payload   <anywhere>\install.ps1              ->  this very folder
#   from the archive     <pkg>\.installer\install.ps1        ->  one level up
#
# The last one is the release layout: the two .cmd launchers stay in plain sight and the
# PowerShell goes out of the way, so what somebody sees on unpacking is what they should
# double-click. Looking rather than being told means the same file serves every case with no
# switch to get wrong - and it has already survived two rearrangements on that basis.
function StagingFolder
{
    foreach ($candidate in @($PSScriptRoot, (Split-Path $PSScriptRoot -Parent))) {
        if ((Test-Path (Join-Path $candidate "bin")) -and
            (Test-Path (Join-Path $candidate "addins"))) { return $candidate }
    }

    $staged = FindUp $PSScriptRoot @(".deploy")
    if ($staged) { return $staged }

    return Join-Path (RepoRoot $PSScriptRoot) ".deploy"
}

$deploy = StagingFolder

if (-not (Test-Path $deploy)) {
    throw "Nothing staged at $deploy. Run scripts\deploy\step1-stage-host.ps1 on the development PC first."
}

# What to tell somebody to right-click. This file ships under two names - step2-deploy-vm
# in the repo, install in the release - so a hardcoded one would be wrong in the other.
$launcher = Launcher $PSCommandPath

# Answers the question actually being asked - can this process write there - instead of
# "is it elevated", which is only a proxy for it. A TIA installed outside Program Files is
# writable without elevation, and refusing that run would be wrong. It creates the folder
# as it goes, which is wanted anyway: the leaf does not exist until an Add-In is installed.
function CanWrite($folder)
{
    $probe = Join-Path $folder ([IO.Path]::GetRandomFileName())

    try
    {
        New-Item -ItemType Directory -Force $folder -ErrorAction Stop | Out-Null
        New-Item -ItemType File $probe -ErrorAction Stop | Out-Null
        Remove-Item $probe -Force -ErrorAction SilentlyContinue

        return $true
    }
    catch { return $false }
}

function Age($path) {
    if (-not (Test-Path $path)) { return "missing" }

    $when = (Get-Item $path).LastWriteTime
    $mins = [int]((Get-Date) - $when).TotalMinutes

    # Age rather than a bare timestamp: "4 min ago" answers the question being asked,
    # which is whether this is the build just made.
    if ($mins -lt 1)     { return "just now" }
    if ($mins -lt 120)   { return "$mins min ago" }

    return $when.ToString("dd/MM HH:mm")
}

if (-not $AddInsOnly) {
    # InstallPaths.Root, worked out by the same two rules Core uses - otherwise the installer
    # and the Add-In would disagree about where the framework lives, which fails as a
    # "not installed" error on an install that looks perfectly fine.
    #
    #   PLC_FRAMEWORK_HOME wins, so a run without elevation can stage into a folder the Add-In
    #   will also look in; and ProgramW6432 rather than ProgramFiles, so a 32-bit PowerShell
    #   still installs where the x64 Add-In inside TIA is going to look.
    $install = InstallRoot

    Write-Host "satellites -> $install" -ForegroundColor Cyan

    # Same question as for the machine-wide Add-In folder, asked the same way: whether this
    # process can write there, not whether it is elevated. Program Files needs elevation on
    # a normal station, but PLC_FRAMEWORK_HOME can point this at a staging folder that does
    # not - and refusing that run would be wrong.
    if (-not (CanWrite $install)) {
        Write-Host "`n   Cannot write into $install" -ForegroundColor Red

        if (-not (IsElevated)) {
            Write-Host ("   Program Files needs elevation: right-click {0}" -f $launcher) -ForegroundColor Red
            Write-Host "   and pick 'Run as administrator'." -ForegroundColor Red
        }
        else {
            Write-Host "   This session is already elevated, so check the folder's permissions." -ForegroundColor Red
        }

        exit 1
    }

    # /PURGE so a renamed or removed executable does not linger. Safe only because this
    # folder is entirely ours and holds nothing but the build output - anything else put
    # there by hand is deleted on the next run, which is the price of not leaving a stale
    # satellite behind after a project is renamed.
    #
    # robocopy reports success with a NON-ZERO exit code - 1 copied, 2 extras, 3 both - and
    # only 8 and above are failures. Left alone, the script would end up reporting failure
    # to its caller while printing success on screen.
    & robocopy (Join-Path $deploy "bin") $install /E /PURGE /NJH /NJS /NDL /NP | Out-Null

    $code = $LASTEXITCODE
    $global:LASTEXITCODE = 0

    if ($code -ge 8) { throw "Copying the satellites failed (robocopy $code)." }

    foreach ($exe in Get-ChildItem $install -Filter "*.exe") {
        Write-Host ("   {0,-46} {1}" -f $exe.Name, (Age $exe.FullName))
    }
}

if (-not $ToolsOnly) {
    Write-Host "`npackages" -ForegroundColor Cyan

    # The scope belongs to the TIA VERSION, not to the run, so a single switch for both
    # could only ever describe one of them:
    #
    #   V20   <ProgramFiles>\Siemens\Automation\Portal V20\AddIns       machine-wide
    #   V21   %AppData%\Siemens\Automation\Portal V21\UserAddIns        per user
    #
    # Stated by the maintainer as how TIA installs, not measured across machines - so if an
    # Add-In fails to appear on a station, this table is the first thing to doubt. It fails
    # loudly rather than quietly: a missing parent folder is reported per version, with the
    # full path it looked in, and a package found in the OTHER folder is reported too.
    # -Scope overrides the pair for a station set up differently.
    $targets = Targets

    foreach ($t in $targets) {
        if ($Scope) { $t.Scope = $Scope }
        $t.Folder = AddInFolder $t.Portal $t.Scope $InstallRoot
    }

    # Checked once, up front, and only for the machine-scoped versions actually present:
    # being sent to find an administrator for a TIA version this station does not have
    # would be a lie. Left to the copy it surfaces as an Access denied, which reads as the
    # OTHER thing that fails here - a package locked by a running TIA.
    $blocked = @($targets | Where-Object {
        $_.Scope -eq "Machine" -and
        (Test-Path (Split-Path $_.Folder -Parent)) -and
        -not (CanWrite $_.Folder)
    })

    if ($blocked.Count -gt 0) {
        Write-Host "   Cannot write into TIA's own installation folder:" -ForegroundColor Red
        foreach ($b in $blocked) { Write-Host ("      {0}" -f $b.Folder) -ForegroundColor Red }

        if (-not (IsElevated)) {
            Write-Host ("`n   Right-click {0} and pick ''Run as administrator''," -f $launcher) -ForegroundColor Red
            Write-Host "   or pass -Scope User to send every version to UserAddIns instead." -ForegroundColor Red
        }
        else {
            # Already administrator and still refused, so elevation is not the answer.
            Write-Host "`n   This session is already elevated, so check the folder's permissions." -ForegroundColor Red
        }

        exit 1
    }

    foreach ($t in $targets) {
        $folder = $t.Folder

        # The destination is printed first, and printed whatever happens next. It differs
        # per version and moves with -Scope and -InstallRoot, so where a package went must
        # never be something to reconstruct from the switches afterwards - least of all on
        # a station where the wrong answer is a stale Add-In loading instead of the new one.
        #
        # It is also the only way to catch an elevated run whose %AppData% belongs to the
        # administrator rather than to the engineer TIA runs as: the printed path says
        # C:\Users\<someone else>\... and the package lands where TIA will never look.
        Write-Host ("   {0}" -f $folder) -ForegroundColor Gray

        $source = Join-Path $deploy "addins\$($t.Package)"
        if (-not (Test-Path $source)) {
            Write-Host ("      {0,-43} not staged" -f $t.Package) -ForegroundColor Yellow
            continue
        }

        # The same rule serves both scopes, and the two absences do not mean the same thing.
        # The PARENT is TIA's own folder - the installation for Machine, the per-user
        # settings folder for User - so its absence means that version is not here. The leaf
        # is ours: neither AddIns nor UserAddIns exists until an Add-In is installed, and
        # treating a missing one as "TIA is not installed" would be wrong in both scopes.
        $portal = Split-Path $folder -Parent

        if (-not (Test-Path $portal)) {
            Write-Host ("      {0,-43} {1} is not installed here" -f $t.Package, $t.Portal) -ForegroundColor Yellow

            if ($t.Scope -eq "Machine") {
                Write-Host ("      {0,-43} pass -InstallRoot if TIA is on another drive" -f "") -ForegroundColor DarkGray
            }

            continue
        }

        New-Item -ItemType Directory -Force $folder | Out-Null

        try
        {
            Copy-Item $source $folder -Force
            Write-Host ("      {0,-43} {1}" -f $t.Package, (Age (Join-Path $folder $t.Package)))
        }
        catch
        {
            # TIA holds the package open while it is running, so this is the expected
            # failure rather than a surprise. Say what to do about it.
            Write-Host ("      {0,-43} LOCKED - close TIA {1} and run again" -f $t.Package, $t.Portal) -ForegroundColor Red
            continue
        }

        # TIA reads both folders, so the same package in both is loaded twice - and the
        # copy being tested is then whichever one TIA picked, which is not something to
        # find out by guessing. Report it; deleting the other one is the operator's call.
        $other = AddInFolder $t.Portal $(if ($t.Scope -eq "Machine") { "User" } else { "Machine" }) $InstallRoot

        if (Test-Path (Join-Path $other $t.Package)) {
            Write-Host ("      {0,-43} ALSO INSTALLED, so TIA loads it twice:" -f "") -ForegroundColor Yellow
            Write-Host ("      {0,-43} {1}" -f "", (Join-Path $other $t.Package)) -ForegroundColor Yellow
        }
    }

    Write-Host "`nRestart TIA Portal to pick up a changed .addin." -ForegroundColor DarkGray
}

exit 0
