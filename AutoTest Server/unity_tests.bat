@echo off
setlocal
rem ============================================================
rem  AutoTest UNITY TESTS - run the Unity Test Runner through the bridge
rem  and wait for the result.
rem
rem  Examples:
rem    unity_tests.bat
rem    unity_tests.bat --mode PlayMode --exit-play
rem    unity_tests.bat --filter ShopTests --assembly Game.Tests
rem
rem  Flags: --mode EditMode^|PlayMode --filter F --assembly A --exit-play --timeout S
rem  Exit: 0 all passed, 2 unreachable, 3 no test matched, 4 failures (or Play mode
rem        is on without --exit-play), 5 compile errors.
rem  Report: auto_touch\out\unity-tests\<ts>.md + .json
rem  Unattended (agent/CI): set AUTOTEST_NO_PAUSE=1 to skip "pause".
rem ============================================================
cd /d "%~dp0"
set "PYTHONIOENCODING=utf-8"

py -m auto_touch unity-tests %*
set "RC=%errorlevel%"
if "%RC%"=="2" echo [ERROR] Unity bridge unreachable - open Unity, Tools ^> AutoTest ^> Start Server.
if not "%AUTOTEST_NO_PAUSE%"=="1" pause
exit /b %RC%
