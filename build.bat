@echo off
rem Forward all arguments to build-installer.ps1
rem Usage: build.bat            (default version 1.0.0)
rem        build.bat -Version 1.1.0
rem        build.bat -SkipPublish
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %*
pause
