# Where everything goes, shared by the installer and the uninstaller.
#
# Dot-sourced by both, and that is deliberate rather than tidy: an uninstaller has to look
# in exactly the places the installer wrote to, so the two agreeing is a correctness
# property. Two copies of this arithmetic would drift, and the failure would be an
# uninstall that reports "nothing found" on a machine that plainly has it installed.
#
# ASCII only, on purpose: PowerShell 5.1 reads a UTF-8 file with no BOM as ANSI.

# Never a literal "C:\Program Files". The physical folder is not translated - Windows has
# not localised it since Vista, only the name the Explorer displays - but it does move: a
# Windows installed on another drive, and WOW64. TIA Portal is 64-bit only, so this must
# resolve to the 64-bit Program Files even when a 32-bit PowerShell is asking.
function ProgramFiles64
{
    if ($env:ProgramW6432) { return $env:ProgramW6432 }

    return $env:ProgramFiles
}

# Core.InstallPaths.Root, by the same two rules Core uses. PLC_FRAMEWORK_HOME wins, so a
# run without elevation can install into a staging folder the Add-In will also look in.
function InstallRoot
{
    if ($env:PLC_FRAMEWORK_HOME) { return $env:PLC_FRAMEWORK_HOME }

    return Join-Path (ProgramFiles64) "PLC-Framework"
}

# Core.InstallPaths.UserRoot. Per user, and never touched by the installer: whichever
# application first writes a secret creates it, which is what keeps it in the profile of the
# engineer who owns the secret rather than in the administrator's.
function UserRoot
{
    return Join-Path $env:LOCALAPPDATA "PLC-Framework"
}

# Roaming, not Local, and UserAddIns, not AddIns. Both are easy to get wrong and both fail
# silently: TIA simply never shows the Add-In.
function AddInFolder($portal, $scope, $installRoot)
{
    if ($scope -eq "Machine") {
        $root = $installRoot
        if (-not $root) { $root = Join-Path (ProgramFiles64) "Siemens\Automation" }

        return Join-Path $root "$portal\AddIns"
    }

    return Join-Path $env:AppData "Siemens\Automation\$portal\UserAddIns"
}

# The scope belongs to the TIA VERSION, not to the run, so a single setting for both could
# only ever describe one of them:
#
#   V20   <ProgramFiles>\Siemens\Automation\Portal V20\AddIns       machine-wide
#   V21   %AppData%\Siemens\Automation\Portal V21\UserAddIns        per user
#
# Stated by the maintainer as how TIA installs, not measured across machines - so if an
# Add-In fails to appear on a station, this table is the first thing to doubt.
function Targets
{
    return @(
        @{ Package = "PLC-Framework.V20.addin"; Portal = "Portal V20"; Scope = "Machine" },
        @{ Package = "PLC-Framework.V21.addin"; Portal = "Portal V21"; Scope = "User"    }
    )
}

function IsElevated
{
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# The .cmd sitting beside whichever .ps1 is running. The two scripts ship under different
# names - step2-deploy-vm.cmd in the repo, install.cmd in the release - so a message that
# named one of them would be wrong in the other. Deriving it costs a line and is never wrong.
function Launcher($scriptPath)
{
    return [IO.Path]::GetFileNameWithoutExtension($scriptPath) + ".cmd"
}

# Walks up from a starting folder until one of $names is found beside it.
#
# Every script here has been moved at least once - scripts\ into scripts\deploy\, the
# PowerShell into the archive's .installer\ - and each move broke a Split-Path that had the
# depth written into it. Searching upwards costs a loop and survives the next rearrangement,
# which is the only thing that can be said for certain about a folder layout.
function FindUp($from, $names)
{
    $dir = $from

    while ($dir) {
        foreach ($name in $names) {
            $candidate = Join-Path $dir $name
            if (Test-Path $candidate) { return $candidate }
        }

        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }

        $dir = $parent
    }

    return $null
}

# The repository root, found by the solution file rather than by counting folders.
function RepoRoot($from)
{
    $solution = FindUp $from @("tia-portal-addins.slnx")
    if (-not $solution) { throw "Could not find tia-portal-addins.slnx above $from." }

    return Split-Path $solution -Parent
}
