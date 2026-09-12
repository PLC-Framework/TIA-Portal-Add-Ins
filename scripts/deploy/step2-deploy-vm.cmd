@echo off
rem Runs step 2 without touching the machine's execution policy.
rem
rem Windows blocks .ps1 files twice over: the default policy is Restricted, and a script
rem opened from a share counts as remote even when the policy allows local ones. Bypass
rem clears both for THIS invocation only - nothing is changed for anything else, and the
rem bypass itself needs no elevation.
rem
rem RUN THIS ELEVATED, on the VM. Not for the bypass above, but because V20's Add-In folder
rem is inside TIA's installation under Program Files. And an elevated session cannot see a
rem mapped drive - it belongs to the logon session that made it - so reach this file by the
rem UNC behind the mapping instead:
rem
rem   & "\\vmware-host\Shared Folders\E\PlcFramework\TIA-Portal-Add-Ins\scripts\deploy\step2-deploy-vm.cmd"
rem
rem %* passes the switches through:  step2-deploy-vm.cmd -ToolsOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0step2-deploy-vm.ps1" %*
exit /b %ERRORLEVEL%
