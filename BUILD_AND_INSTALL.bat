@echo off
setlocal EnableExtensions
rem BUILD_AND_INSTALL.bat (Hideout Uncensored) - builds, checks, installs and packages both parts.
rem Close the game AND the SPT server first. Needs the .NET SDK (dotnet) and an SPT 4.1.x install.
rem   client plugin -> <game>\BepInEx\plugins\HideoutUncensored\HideoutUncensored.dll
rem   server part   -> <game>\SPT_Runtime\user\mods\HideoutUncensored\HideoutUncensoredServer.dll (discards mailed back)
rem   release zip -> dist\SPTMOD-Hideout-Uncensored-<version>.zip
rem The SPT folder is asked once and saved in build.local.cfg (not committed). Log: _build\build.log
set "HERE=%~dp0"
set "LOGDIR=%HERE%_build"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "LOG=%LOGDIR%\build.log"

call :getgame || ( pause & exit /b 1 )
tasklist /FI "IMAGENAME eq EscapeFromTarkov.exe" 2>nul | find /I "EscapeFromTarkov.exe" >nul && ( echo Close the game first. & pause & exit /b 1 )
tasklist /FI "IMAGENAME eq SPT.Server.exe" 2>nul | find /I "SPT.Server.exe" >nul && ( echo Close the SPT server first. & pause & exit /b 1 )
where dotnet >nul 2>&1 || ( echo dotnet not found - install the .NET SDK: https://dotnet.microsoft.com/download & pause & exit /b 1 )

set "VER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content -Raw '%HERE%Directory.Build.props')).Project.PropertyGroup.Version"`) do set "VER=%%v"
if not defined VER ( echo Could not read the version from Directory.Build.props. & pause & exit /b 1 )

> "%LOG%" echo Hideout Uncensored %VER% build %DATE% %TIME% - game: %GAME%
echo === plugin %VER% ===
dotnet build "%HERE%HideoutUncensored.csproj" -c Release -p:TarkovDir="%GAME%" -p:DeployToGame=true >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%LOG%" | findstr /I /C:"error" /C:"warning CS" /C:"Deployed" /C:"Build succeeded"
if not "%RC%"=="0" ( echo. & echo BUILD FAILED - see %LOG% & pause & exit /b 1 )
if not exist "%GAME%\BepInEx\plugins\HideoutUncensored\HideoutUncensored.dll" ( echo NOT installed - see %LOG% & pause & exit /b 1 )

echo === server part %VER% ===
dotnet build "%HERE%Server\HideoutUncensoredServer.csproj" -c Release >> "%LOG%" 2>&1
if errorlevel 1 ( echo. & echo SERVER BUILD FAILED - see %LOG% & pause & exit /b 1 )
set "SRVDLL=%HERE%Server\dist\SPT_Runtime\user\mods\HideoutUncensored\HideoutUncensoredServer.dll"
set "SRV=%GAME%\SPT_Runtime\user\mods\HideoutUncensored"
if not exist "%SRV%" mkdir "%SRV%"
copy /Y "%SRVDLL%" "%SRV%\" >nul || ( echo Server part copy FAILED & pause & exit /b 1 )

echo === patch targets ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%tools\verify-targets.ps1" -Game "%GAME%" >> "%LOG%" 2>&1
if errorlevel 1 ( echo A patch target no longer matches this game build - see %LOG% ) else ( echo All patch targets resolve. )

set "ZIP=%HERE%dist\SPTMOD-Hideout-Uncensored-%VER%.zip"
del /Q "%HERE%dist\*.zip" 2>nul
if exist "%HERE%dist\SPT_Runtime" rmdir /S /Q "%HERE%dist\SPT_Runtime"
mkdir "%HERE%dist\SPT_Runtime\user\mods\HideoutUncensored"
copy /Y "%SRVDLL%" "%HERE%dist\SPT_Runtime\user\mods\HideoutUncensored\" >nul
copy /Y "%HERE%LICENSE" "%HERE%dist\SPT_Runtime\user\mods\HideoutUncensored\LICENSE" >nul
rem Forge rule: the license goes inside the archive, in the mod folder
copy /Y "%HERE%LICENSE" "%HERE%dist\BepInEx\plugins\HideoutUncensored\LICENSE" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%HERE%dist\BepInEx','%HERE%dist\SPT_Runtime' -DestinationPath '%ZIP%' -Force" >> "%LOG%" 2>&1
if exist "%ZIP%" ( echo Packaged:  %ZIP% ) else ( echo Packaging FAILED - see %LOG% )

echo.
echo Installed: %GAME%\BepInEx\plugins\HideoutUncensored\HideoutUncensored.dll
echo Installed: %SRV%\HideoutUncensoredServer.dll
echo Start the SPT server (it must show "[Hideout Uncensored] server part %VER% loaded"), then the game.
echo In game: F12 ^> trappuss-HideoutUncensored. Default draw/holster key in the hideout: J.
pause
exit /b 0

rem ---- SPT folder: build.local.cfg, else the default below if it exists, else ask (and save) ----
:getgame
set "GAME="
set "CFG=%HERE%build.local.cfg"
if exist "%CFG%" for /f "usebackq tokens=1,* delims==" %%a in ("%CFG%") do if /I "%%a"=="GAME" set "GAME=%%b"
if not defined GAME if exist "G:\G Games\SPT4.1\SPT4.1 GAME\EscapeFromTarkov.exe" set "GAME=G:\G Games\SPT4.1\SPT4.1 GAME"
if defined GAME if exist "%GAME%\EscapeFromTarkov.exe" goto savegame
echo SPT folder not found. Paste the folder that contains EscapeFromTarkov.exe:
set /p "GAME=> "
set "GAME=%GAME:"=%"
if "%GAME:~-1%"=="\" set "GAME=%GAME:~0,-1%"
if not exist "%GAME%\EscapeFromTarkov.exe" ( echo EscapeFromTarkov.exe not found in "%GAME%". & exit /b 1 )
:savegame
> "%CFG%" echo GAME=%GAME%
exit /b 0
