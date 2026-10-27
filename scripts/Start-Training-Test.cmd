@echo off
cd /d "%~dp0.."
start "Echo" "%~dp0..\artifacts\app\Echo.exe" --data-dir "%~dp0..\artifacts\training-profile"
