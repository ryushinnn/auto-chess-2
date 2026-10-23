@echo off
setlocal
rem ============================================================
rem  AutoTest RUN - run one scenario file (every matrix case).
rem
rem  Examples:
rem    run.bat auto_touch\scenarios\smoke_editor.json
rem    run.bat smoke_editor.json --enter-play --exit-play
rem    run.bat my_matrix.json --case "amount=5" --verbose
rem    run.bat my_matrix.json --list-cases
rem
rem  Flags: --enter-play --exit-play --timescale N --no-stop --case k=v,..
rem         --list-cases --verbose --param k=v (see: py -m auto_touch run -h)
rem  Paths: relative to this folder, to auto_touch\scenarios\, or absolute.
rem  Exit: 0 PASS, 2 unreachable, 3 invalid scenario, 4 FAIL, 5 compile errors.
rem  Report: auto_touch\out\diagnostics\*.json + auto_touch\out\reports\*.md
rem  Unattended (agent/CI): set AUTOTEST_NO_PAUSE=1 to skip "pause".
rem ============================================================
cd /d "%~dp0"
set "PYTHONIOENCODING=utf-8"

if "%~1"=="" goto :usage
py -m auto_touch run %*
set "RC=%errorlevel%"
echo.
if "%RC%"=="0" echo [PASS] Report: auto_touch\out\reports\  (JSON: auto_touch\out\diagnostics\)
if "%RC%"=="2" echo [ERROR] Unity bridge unreachable - open Unity, Tools ^> AutoTest ^> Start Server.
if "%RC%"=="3" echo [INVALID] The scenario file or the arguments are invalid (see above).
if "%RC%"=="4" echo [FAIL] A step failed - the report in auto_touch\out\reports\ names it.
if "%RC%"=="5" echo [ERROR] Unity has compile errors - fix them first.
goto :end

:usage
echo Usage: run.bat ^<scenario.json^> [--enter-play] [--exit-play] [--timescale N] [--no-stop] [--case k=v] [--verbose]
echo.
py -m auto_touch list
set "RC=3"

:end
if not "%AUTOTEST_NO_PAUSE%"=="1" pause
exit /b %RC%
