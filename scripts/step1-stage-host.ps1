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

$repo    = Split-Path $PSScriptRoot -Parent
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

$satellites = @("Satellite.About", "Satellite.DataBlockSnapshot", "Satellite.ConfigEditor")
$versions   = @(@{ Project = "AddIn.V20"; Package = "PLC-Framework.V20.addin" },
                @{ Project = "AddIn.V21"; Package = "PLC-Framework.V21.addin" })

if (-not $SkipBuild) {
    Write-Host "building..." -ForegroundColor Cyan
    & dotnet build (Join-Path $repo "tia-portal-addins.slnx") -v quiet --nologo
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

# The installer travels WITH what it installs, so .deploy\ is one self-contained thing the
# VM can copy to a local disk - which is the only way in when the share is a mapped drive,
# because a mapped drive belongs to the logon session that made it and is simply not there
# in an elevated one. Refreshed on every run, since .deploy\ is cleared above: that is what
# stops the copy on the VM from quietly becoming last week's deployer.
Write-Host "`ninstaller -> .deploy\" -ForegroundColor Cyan
foreach ($name in @("step2-deploy-vm.cmd", "step2-deploy-vm.ps1")) {
    Copy-Item (Join-Path $PSScriptRoot $name) $deploy -Force
    Write-Host ("   {0,-46} {1}" -f $name, (Get-Item (Join-Path $deploy $name)).LastWriteTime.ToString("HH:mm:ss"))
}

Write-Host "`nstaged in $deploy" -ForegroundColor Green
Write-Host "on the VM, in an ELEVATED PowerShell - V20 installs under Program Files:" -ForegroundColor Cyan
Write-Host '   & "\\vmware-host\Shared Folders\E\PlcFramework\tia-portal-addins\scripts\step2-deploy-vm.cmd"'
Write-Host ""
Write-Host "   By UNC, not by the Z: mapping: a mapped drive belongs to the logon session" -ForegroundColor DarkGray
Write-Host "   that created it, so an elevated one does not have it. Where a station's UNC" -ForegroundColor DarkGray
Write-Host "   does not cross either, copy .deploy\ to a local disk - it carries its own" -ForegroundColor DarkGray
Write-Host "   installer - and run the step2-deploy-vm.cmd inside it." -ForegroundColor DarkGray

exit 0
