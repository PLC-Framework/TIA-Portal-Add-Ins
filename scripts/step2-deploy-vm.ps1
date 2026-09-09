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

    # Where the .addin goes. User is the per-user UserAddIns folder and needs nothing;
    # Machine is the AddIns folder inside TIA's own installation, which serves every
    # engineer who logs into the station and needs elevation to write.
    [ValidateSet("User", "Machine")]
    [string]$Scope = "User",

    # Only for Scope Machine, and only when TIA is not under %ProgramFiles%. Point it at
    # the folder holding "Portal V20", not at the Portal folder itself.
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"

$repo   = Split-Path $PSScriptRoot -Parent
$deploy = Join-Path $repo ".deploy"

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
    $tools = Join-Path $env:ProgramData "PLC-Framework\tools"

    Write-Host "satellites -> $tools" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $tools | Out-Null

    # /PURGE so a renamed or removed executable does not linger. The folder holds only what
    # this framework ships, so nothing else can be caught by it.
    #
    # robocopy reports success with a NON-ZERO exit code - 1 copied, 2 extras, 3 both - and
    # only 8 and above are failures. Left alone, the script would end up reporting failure
    # to its caller while printing success on screen.
    & robocopy (Join-Path $deploy "tools") $tools /E /PURGE /NJH /NJS /NDL /NP | Out-Null

    $code = $LASTEXITCODE
    $global:LASTEXITCODE = 0

    if ($code -ge 8) { throw "Copying the satellites failed (robocopy $code)." }

    foreach ($exe in Get-ChildItem $tools -Filter "*.exe") {
        Write-Host ("   {0,-46} {1}" -f $exe.Name, (Age $exe.FullName))
    }
}

if (-not $ToolsOnly) {
    $machine = $Scope -eq "Machine"

    if ($machine) { Write-Host "`npackages -> AddIns, every user on this station" -ForegroundColor Cyan }
    else          { Write-Host "`npackages -> UserAddIns, this user only" -ForegroundColor Cyan }

    $targets = @(
        @{ Package = "PLC-Framework.V20.addin"; Portal = "Portal V20" },
        @{ Package = "PLC-Framework.V21.addin"; Portal = "Portal V21" }
    )

    foreach ($t in $targets) {
        $source = Join-Path $deploy "addins\$($t.Package)"
        if (-not (Test-Path $source)) {
            Write-Host ("   {0,-46} not staged" -f $t.Package) -ForegroundColor Yellow
            continue
        }

        $folder = AddInFolder $t.Portal $Scope

        if ($machine) {
            # AddIns sits INSIDE the installation, so the two absences mean different
            # things: no Portal folder is "TIA is not here", while no AddIns folder is
            # just one to create - it does not exist until an Add-In is installed.
            $portal = Split-Path $folder -Parent

            if (-not (Test-Path $portal)) {
                Write-Host ("   {0,-46} not found at {1}" -f $t.Package, $portal) -ForegroundColor Yellow
                Write-Host ("   {0,-46} pass -InstallRoot if TIA is on another drive" -f "") -ForegroundColor DarkGray
                continue
            }

            # Demanded by name, and only now that there is somewhere to write - being told
            # to find an administrator for a TIA version this station does not have would
            # be a lie. Left to the copy, it surfaces as an Access denied, which reads as a
            # locked package: the OTHER thing that fails here.
            if (-not (IsElevated)) {
                Write-Host "`n   Scope Machine writes into TIA's own installation folder, which needs elevation." -ForegroundColor Red
                Write-Host "   Right-click scripts\step2-deploy-vm.cmd and pick 'Run as administrator'," -ForegroundColor Red
                Write-Host "   or drop -Scope Machine to install for this user only." -ForegroundColor Red
                exit 1
            }

            New-Item -ItemType Directory -Force $folder | Out-Null
        }
        elseif (-not (Test-Path $folder)) {
            Write-Host ("   {0,-46} {1} is not installed" -f $t.Package, $t.Portal) -ForegroundColor Yellow
            continue
        }

        try
        {
            Copy-Item $source $folder -Force
            Write-Host ("   {0,-46} {1}" -f "$($t.Portal): $($t.Package)", (Age (Join-Path $folder $t.Package)))
        }
        catch
        {
            # TIA holds the package open while it is running, so this is the expected
            # failure rather than a surprise. Say what to do about it.
            Write-Host ("   {0,-46} LOCKED - close TIA {1} and run again" -f $t.Package, $t.Portal) -ForegroundColor Red
            continue
        }

        # TIA reads both folders, so the same package in both is loaded twice - and the
        # copy being tested is then whichever one TIA picked, which is not something to
        # find out by guessing. Report it; deleting the other one is the operator's call.
        $other = AddInFolder $t.Portal $(if ($machine) { "User" } else { "Machine" })

        if (Test-Path (Join-Path $other $t.Package)) {
            Write-Host ("   {0,-46} ALSO INSTALLED AT {1}" -f "", $other) -ForegroundColor Yellow
            Write-Host ("   {0,-46} remove one, or TIA loads the package twice" -f "") -ForegroundColor Yellow
        }
    }

    Write-Host "`nRestart TIA Portal to pick up a changed .addin." -ForegroundColor DarkGray
}

exit 0
