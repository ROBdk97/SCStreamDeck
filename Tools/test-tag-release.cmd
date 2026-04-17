@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%.."
if errorlevel 1 (
    echo Failed to enter repository root.
    exit /b 1
)

set "DOTNET_PROJECT=PluginCore\PluginCore.csproj"
set "PLUGIN_FOLDER=PluginCore\bin\Release\com.robdk97.scstreamdeck.sdPlugin"
set "PLUGIN_OUT=plugin-out"
set "SIMPLE_PACKAGE=com.robdk97.scstreamdeck.runtime-included.streamDeckPlugin"
set "ADVANCED_PACKAGE=com.robdk97.scstreamdeck.runtime-required.streamDeckPlugin"
set "VERSION=%~1"

if "%VERSION%"=="" (
    echo Usage: %~nx0 ^<version-or-tag^>
    echo Example: %~nx0 v1.2.0.0
    popd
    exit /b 1
)

if /i "%VERSION:~0,1%"=="v" set "VERSION=%VERSION:~1%"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found on PATH.
    popd
    exit /b 1
)

where streamdeck >nul 2>nul
if errorlevel 1 (
    echo streamdeck CLI was not found on PATH.
    echo Install it with: npm install -g @elgato/cli@latest
    popd
    exit /b 1
)

echo [1/6] Cleaning output folders...
if exist "%PLUGIN_FOLDER%" rmdir /s /q "%PLUGIN_FOLDER%"
if exist "%PLUGIN_OUT%" rmdir /s /q "%PLUGIN_OUT%"
if errorlevel 1 (
    echo Failed to clean output folders.
    popd
    exit /b 1
)

if exist "PluginCore\package-lock.json" (
    if exist "PluginCore\package.json" (
        echo [2/6] Running npm ci...
        pushd "PluginCore"
        call npm ci
        if errorlevel 1 (
            popd
            popd
            exit /b 1
        )

        echo [3/6] Running npm run build...
        call npm run build
        if errorlevel 1 (
            popd
            popd
            exit /b 1
        )
        popd
    ) else (
        echo [2/6] package-lock.json found without package.json, skipping npm steps.
    )
) else (
    echo [2/6] No PluginCore\package-lock.json found, skipping npm ci / npm run build.
)

echo [4/6] Publishing simple package ^(runtime included^)...
if exist "%PLUGIN_FOLDER%" rmdir /s /q "%PLUGIN_FOLDER%"
dotnet publish "%DOTNET_PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o "%PLUGIN_FOLDER%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -p:Version=%VERSION% ^
    -p:AssemblyVersion=%VERSION% ^
    -p:FileVersion=%VERSION%
if errorlevel 1 goto fail

echo [5/6] Packing simple package...
call streamdeck pack "%PLUGIN_FOLDER%" -o "%PLUGIN_OUT%" --version "%VERSION%" -f
if errorlevel 1 goto fail
if exist "%PLUGIN_OUT%\%SIMPLE_PACKAGE%" del /q "%PLUGIN_OUT%\%SIMPLE_PACKAGE%"
move /y "%PLUGIN_OUT%\com.robdk97.scstreamdeck.streamDeckPlugin" "%PLUGIN_OUT%\%SIMPLE_PACKAGE%" >nul
if errorlevel 1 goto fail

echo [4/6] Publishing advanced package ^(runtime required^)...
if exist "%PLUGIN_FOLDER%" rmdir /s /q "%PLUGIN_FOLDER%"
dotnet publish "%DOTNET_PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    -p:SelfContained=false ^
    -o "%PLUGIN_FOLDER%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -p:Version=%VERSION% ^
    -p:AssemblyVersion=%VERSION% ^
    -p:FileVersion=%VERSION%
if errorlevel 1 goto fail

echo [5/6] Packing advanced package...
call streamdeck pack "%PLUGIN_FOLDER%" -o "%PLUGIN_OUT%" --version "%VERSION%" -f
if errorlevel 1 goto fail
if exist "%PLUGIN_OUT%\%ADVANCED_PACKAGE%" del /q "%PLUGIN_OUT%\%ADVANCED_PACKAGE%"
move /y "%PLUGIN_OUT%\com.robdk97.scstreamdeck.streamDeckPlugin" "%PLUGIN_OUT%\%ADVANCED_PACKAGE%" >nul
if errorlevel 1 goto fail

echo [6/6] Created package(s):
if exist "%PLUGIN_OUT%\%SIMPLE_PACKAGE%" echo   %PLUGIN_OUT%\%SIMPLE_PACKAGE%
if exist "%PLUGIN_OUT%\%ADVANCED_PACKAGE%" echo   %PLUGIN_OUT%\%ADVANCED_PACKAGE%
popd
endlocal
exit /b 0

:fail
popd
endlocal
exit /b 1
