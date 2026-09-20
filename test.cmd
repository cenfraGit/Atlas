@echo off
rem runs the unit suite. it is hermetic - it builds its own fixture repos and
rem its own git history - so it takes no argument and does not depend on what
rem this repo happens to contain.
setlocal

cd /d "%~dp0"
dotnet test tests\Atlas.Tests\Atlas.Tests.csproj --nologo
set CODE=%ERRORLEVEL%

echo.
if %CODE%==0 (echo ALL TESTS PASSED) else (echo TESTS FAILED)
pause
exit /b %CODE%
