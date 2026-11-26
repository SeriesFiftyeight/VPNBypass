@echo off
:: VPN Bypass - emergency stop: closes the app and restores Windows proxy settings
chcp 65001 >nul

echo ===================================================
echo   VPN Bypass :: stopping and cleaning up
echo ===================================================

taskkill /f /im VPNBypass.exe >nul 2>&1

echo [1/2] Restoring Windows proxy settings...
if exist "%~dp0VPNBypass.exe" (
    start "" /wait "%~dp0VPNBypass.exe" --restore
) else (
    powershell -NoProfile -Command "$k='HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'; $s=Get-ItemProperty $k; if ($s.AutoConfigURL -like 'http://127.0.0.1:*/proxy.pac') { Remove-ItemProperty $k AutoConfigURL }; if ($s.ProxyServer -like '127.0.0.1:*') { Set-ItemProperty $k ProxyEnable 0; Remove-ItemProperty $k ProxyServer }"
)

echo [2/2] Flushing DNS cache...
ipconfig /flushdns >nul

echo ===================================================
echo   Done. Internet settings are back to normal.
echo ===================================================
timeout /t 3
