@echo off
rem Removes PLC-Framework. Asks what to remove before touching anything.
rem
rem Bypass, like install.cmd: the default execution policy is Restricted, and a script
rem opened from a share counts as remote. It applies to THIS invocation only and changes
rem nothing on the machine.
rem
rem Run as administrator to remove the machine-wide parts - the applications under
rem Program Files and the V20 Add-In inside TIA's own installation. Without elevation the
rem per-user parts still go, and the rest is reported rather than half-done.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
exit /b %ERRORLEVEL%
