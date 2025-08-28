@echo off
REM Stellar Sync Client Build Script for Windows

echo Building Stellar Sync Client...

REM Check if DALAMUD_PATH is set
if "%DALAMUD_PATH%"=="" (
    echo Warning: DALAMUD_PATH environment variable is not set.
    echo Please set it to your Dalamud installation path:
    echo set DALAMUD_PATH=C:\path\to\your\Dalamud
    echo.
)

REM Change to the StellarSync project directory
cd StellarSync

REM Clean previous builds
echo Cleaning previous builds...
dotnet clean

REM Restore packages
echo Restoring packages...
dotnet restore

REM Build in Release configuration
echo Building in Release configuration...
dotnet build --configuration Release --no-restore

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful!
    echo Output location: bin\Release\net8.0-windows7.0\
    echo.
    echo To install in Dalamud:
    echo 1. Copy the contents of bin\Release\net8.0-windows7.0\ to your Dalamud devPlugins folder
    echo 2. Restart FFXIV and Dalamud
    echo 3. Enable the plugin in Dalamud's plugin installer
) else (
    echo.
    echo Build failed!
    pause
    exit /b 1
)

pause
