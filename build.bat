@echo off
rem modswitch build script - uses only the .NET Framework compiler that is already part of Windows.
rem   build.bat        ->  dist\modswitch-gui.exe    the window
rem                          dist\modswitch-cli.exe  the same engine without a window
rem   build.bat test   ->  dist\modswitch-test.exe   headless acceptance run (exit 0 = all passed)
rem No SDK, no Visual Studio, no NuGet, nothing to install.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo !! .NET Framework 4.x compiler not found under %WINDIR%\Microsoft.NET
    echo    It is present on Windows 10 and 11 by default.
    exit /b 1
)

if not exist "%~dp0dist" mkdir "%~dp0dist"
cd /d "%~dp0src"

set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
rem The loader we compile in (MIT - see THIRD_PARTY.md).  The name after the comma is what the code asks
rem the assembly for, so it is found without touching the disk.  A build without it is still fine:
rem InstallLoader() then falls back to loader\dinput8.dll or the copy already in the game folder.
set PAYLOAD=/resource:"..\payload\dinput8.dll",modswitch.dinput8.dll
set SOURCES=modswitch.cs modswitch-loc.cs modswitch-gui.cs

"%CSC%" -nologo -optimize -target:winexe -main:ModSwitchGui -out:"..\dist\modswitch-gui.exe" %PAYLOAD% %REFS% %SOURCES%
if errorlevel 1 (
    echo.
    echo !! build failed.  CS0016 or "the file is being used by another process" means
    echo    dist\modswitch-gui.exe is still running - close it and run this again.
    echo    CS1566 on the resource line means payload\dinput8.dll is missing.
    exit /b 1
)
echo built  dist\modswitch-gui.exe

"%CSC%" -nologo -optimize -target:exe -main:ModSwitch -out:"..\dist\modswitch-cli.exe" %PAYLOAD% %REFS% %SOURCES%
if errorlevel 1 exit /b 1
echo built  dist\modswitch-cli.exe

if /i "%~1"=="test" (
    "%CSC%" -nologo -optimize -target:exe -main:FeatureTest -out:"..\dist\modswitch-test.exe" %PAYLOAD% %REFS% %SOURCES% modswitch-test.cs
    if errorlevel 1 exit /b 1
    echo built  dist\modswitch-test.exe
    echo        run it; exit code 0 means every assertion passed.
)

rem double-clicked without arguments: keep the window open so the output is readable
if "%~1"=="" pause
endlocal
