@echo off
:: VPN Bypass - emergency stop and network cleanup
chcp 65001 >nul

net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo ===================================================
echo   VPN Bypass :: stopping engine and cleaning up
echo ===================================================

taskkill /f /im VPNBypass.exe 2>nul
taskkill /f /im goodbyedpi.exe 2>nul
:: v1 leftovers
taskkill /f /im dcbypass.exe 2>nul
sc stop GoodbyeDPI >nul 2>&1
sc stop WinDivert >nul 2>&1
sc stop WinDivert14 >nul 2>&1

echo [1/2] Flushing DNS cache...
ipconfig /flushdns >nul

echo [2/2] Engine and drivers stopped.
echo ===================================================
timeout /t 3
