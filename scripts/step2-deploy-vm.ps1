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

# There are two places this runs from, and it works out which by looking for the payload
# rather than by being told:
#
#   from the repo         scripts\step2-deploy-vm.ps1   ->  ..\.deploy\
#   from a staged copy    .deploy\step2-deploy-vm.ps1   ->  this very folder
#
# The second is what lets the VM hold a local copy: step1 puts this script inside .deploy\,
# so the folder is one self-contained thing to copy - payload and installer together. That
# pairing is the point. A deployer copied on its own goes stale silently, which is the same
# disease as testing a stale build, one level up and with nothing printing its age.
function StagingFolder
{
    if ((Test-Path (Join-Path $PSScriptRoot "bin")) -and
        (Test-Path (Join-Path $PSScriptRoot "addins"))) { return $PSScriptRoot }

    return Join-Path (Split-Path $PSScriptRoot -Parent) ".deploy"
}

$deploy = StagingFolder

if (-not (Test-Path $deploy)) {
    throw "Nothing staged at $deploy. Run scripts\step1-stage-host.ps1 on the development PC first."
}

# Roaming, not Local, and UserAddIns, not AddIns. Both are easy to get wrong and both fail
# silently: TIA simply never shows the Add-In.
function AddInFolder($portal, $scope)
{
    if ($scope -eq "Machine") {
        $root = $InstallRoot
        if (-not $root) { $root = Join-Path $env:ProgramFiles "Siemens\Automation" }

        return Join-Path $root "$portal\AddIns"
    }

    return Join-Path $env:AppData "Siemens\Automation\$portal\UserAddIns"
}

function IsElevated
{
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

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
    $install = $env:PLC_FRAMEWORK_HOME

    if (-not $install) {
        $programFiles = $env:ProgramW6432
        if (-not $programFiles) { $programFiles = $env:ProgramFiles }

        $install = Join-Path $programFiles "PLC-Framework"
    }

    Write-Host "satellites -> $install" -ForegroundColor Cyan

    # Same question as for the machine-wide Add-In folder, asked the same way: whether this
    # process can write there, not whether it is elevated. Program Files needs elevation on
    # a normal station, but PLC_FRAMEWORK_HOME can point this at a staging folder that does
    # not - and refusing that run would be wrong.
    if (-not (CanWrite $install)) {
        Write-Host "`n   Cannot write into $install" -ForegroundColor Red

        if (-not (IsElevated)) {
            Write-Host "   Program Files needs elevation: right-click scripts\step2-deploy-vm.cmd" -ForegroundColor Red
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

    # The scope belongs to the TIA VERSION, not to the run. On the station this is built
    # for, V20 takes the machine-wide folder inside its own installation while V21 takes
    # the per-user one - so a single switch for both could only ever describe one of them.
    # -Scope overrides the pair for a station set up differently.
    $targets = @(
        @{ Package = "PLC-Framework.V20.addin"; Portal = "Portal V20"; Scope = "Machine" },
        @{ Package = "PLC-Framework.V21.addin"; Portal = "Portal V21"; Scope = "User" }
    )

    foreach ($t in $targets) {
        if ($Scope) { $t.Scope = $Scope }
        $t.Folder = AddInFolder $t.Portal $t.Scope
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
            Write-Host "`n   Right-click scripts\step2-deploy-vm.cmd and pick 'Run as administrator'," -ForegroundColor Red
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
        $other = AddInFolder $t.Portal $(if ($t.Scope -eq "Machine") { "User" } else { "Machine" })

        if (Test-Path (Join-Path $other $t.Package)) {
            Write-Host ("      {0,-43} ALSO INSTALLED, so TIA loads it twice:" -f "") -ForegroundColor Yellow
            Write-Host ("      {0,-43} {1}" -f "", (Join-Path $other $t.Package)) -ForegroundColor Yellow
        }
    }

    Write-Host "`nRestart TIA Portal to pick up a changed .addin." -ForegroundColor DarkGray
}

exit 0
