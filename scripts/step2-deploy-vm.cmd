@echo off
rem Runs step 2 without touching the machine's execution policy.
rem
rem Windows blocks .ps1 files twice over: the default policy is Restricted, and a script
rem opened from a mapped drive counts as remote even when the policy allows local ones.
rem Bypass applies to this one invocation only - nothing is changed for anything else, and
rem nothing needs elevation.
rem
rem %* passes the switches through:  step2-deploy-vm.cmd -ToolsOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0step2-deploy-vm.ps1" %*
exit /b %ERRORLEVEL%
