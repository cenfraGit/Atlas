@echo off
rem runs every self-check against Atlas itself, or against a repo you drag on
setlocal
set "REPO=%~1"
rem no argument: Atlas opens on itself, with the path fully resolved
if "%REPO%"=="" for %%I in ("%~dp0.") do set "REPO=%%~fI"

cd /d "%~dp0app"
dotnet build -v q --nologo || (pause & exit /b 1)

for %%T in (flighttest searchtest bookmarktest boardtest annotationtest gittest pickingtest tokentest) do (
  echo.
  echo === %%T ===
  dotnet run --no-build -- --%%T "%REPO%"
)
echo.
pause
