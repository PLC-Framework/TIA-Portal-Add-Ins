# Builds the solution and gathers everything the VM needs into .deploy\.
#
# Run this on the development PC. It writes to .deploy\ rather than leaving the VM to
# read bin\Debug\net48 directly, and that is the whole reason it exists: TIA holds the
# .addin open through the shared folder, so the Publisher cannot overwrite the file the
# VM is reading and the build dies with MSB3073, naming vmware-vmx.exe as the owner.
# Two copies of the artefact, and the build only ever touches one of them.
#
# ASCII only, on purpose: PowerShell 5.1 reads a UTF-8 file with no BOM as ANSI, and a
# stray accent turns into a parse error.

[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

. (Join-Path (Split-Path $PSScriptRoot -Parent) "paths.ps1")

$repo    = RepoRoot $PSScriptRoot
$deploy  = Join-Path $repo ".deploy"
$binaries = Join-Path $deploy "bin"
$addins  = Join-Path $deploy "addins"

# robocopy reports success with a NON-ZERO exit code - 1 means files were copied, 2 that
# extras were found, 3 both - and only 8 and above are failures. Left alone, the script
# inherits that and reports failure to whatever called it while printing success on screen.
function Copy-Tree($from, $to, $extra)
{
    $arguments = @($from, $to, "/E", "/NJH", "/NJS", "/NDL", "/NP") + $extra
    & robocopy @arguments | Out-Null

    $code = $LASTEXITCODE
    $global:LASTEXITCODE = 0

    if ($code -ge 8) { throw "robocopy failed with $code copying $from" }
}

# The core updater is two executables, one per TIA version, because it is an Openness client
# and Openness is a different assembly with a different public key token in V20 and V21. Its
# shared library travels inside each of their build outputs, so it is not listed here.
$satellites = @("Satellite.About", "Satellite.DataBlockSnapshot", "Satellite.ConfigEditor", "Satellite.CodingStyleReport",
                "Satellite.CoreUpdater.V20", "Satellite.CoreUpdater.V21")
$versions   = @(@{ Project = "AddIn.V20"; Package = "PLC-Framework.V20.addin" },
                @{ Project = "AddIn.V21"; Package = "PLC-Framework.V21.addin" })

if (-not $SkipBuild) {
    Write-Host "building..." -ForegroundColor Cyan
    & dotnet build (Join-Path $repo "TIA-Portal-Add-Ins.slnx") -v quiet --nologo
    if ($LASTEXITCODE -ne 0) { throw "The build failed; nothing was staged." }
}

# Cleared each time. A stale executable left behind from a renamed project is exactly the
# kind of thing that gets loaded for weeks without anybody noticing.
Remove-Item $deploy -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $binaries | Out-Null
New-Item -ItemType Directory -Force $addins   | Out-Null

Write-Host "`nsatellites -> .deploy\bin\" -ForegroundColor Cyan
foreach ($name in $satellites) {
    $from = Join-Path $repo "src\$name\bin\Debug\net48"
    if (-not (Test-Path $from)) { throw "$name has not been built: $from" }

    # All three land in one flat folder, which is what InstallPaths.Root expects. They share
    # Core.dll and UI.Shared.dll; the copies are identical because they were built together.
    # /XF *.pdb: line numbers in stack traces are worth having on the VM, but not the noise
    # here - drop the switch if a crash needs chasing.
    Copy-Tree $from $binaries @("/XF", "*.pdb")

    $exe = Get-ChildItem $from -Filter "*.exe" | Select-Object -First 1
    Write-Host ("   {0,-46} {1}" -f $exe.Name, $exe.LastWriteTime.ToString("HH:mm:ss"))
}

Write-Host "`npackages -> .deploy\addins\" -ForegroundColor Cyan
foreach ($v in $versions) {
    $package = Join-Path $repo "src\$($v.Project)\bin\Debug\net48\$($v.Package)"
    if (-not (Test-Path $package)) {
        # The Publisher target is skipped when the Siemens tools are missing, so this is a
        # normal state on a machine without them - worth saying rather than failing.
        Write-Host ("   {0,-46} NOT BUILT (Siemens Publisher missing?)" -f $v.Package) -ForegroundColor Yellow
        continue
    }

    Copy-Item $package $addins -Force
    Write-Host ("   {0,-46} {1}" -f $v.Package, (Get-Item $package).LastWriteTime.ToString("HH:mm:ss"))
}

# The installer is NOT copied in here. It was, as a fallback for a station where neither the
# mapped drive nor the UNC behind it reaches an elevated session - .deploy\ was then one
# self-contained folder to copy to a local disk. The UNC does cross on the VM this is built
# against, so that fallback never runs, and staging for the VM is a different job from
# packaging a release. The release script is what pairs an installer with this payload, and
# it names the files what a stranger expects to see.
#
# If a station ever does need the local-copy route, copy scripts\ across as well: step2
# resolves the payload relative to itself and stops with "Nothing staged" without it.
Write-Host "`nstaged in $deploy" -ForegroundColor Green
Write-Host "on the VM, in an ELEVATED PowerShell - V20 installs under Program Files:" -ForegroundColor Cyan
Write-Host '   & "\\vmware-host\Shared Folders\E\PlcFramework\TIA-Portal-Add-Ins\scripts\deploy\step2-deploy-vm.cmd"'
Write-Host ""
Write-Host "   By UNC, not by the Z: mapping: a mapped drive belongs to the logon session" -ForegroundColor DarkGray
Write-Host "   that created it, so an elevated one does not have it. Where a station's UNC" -ForegroundColor DarkGray
Write-Host "   does not cross either, copy BOTH .deploy\ and scripts\ to a local disk," -ForegroundColor DarkGray
Write-Host "   keeping them side by side, and run step2 from the copy." -ForegroundColor DarkGray

exit 0
