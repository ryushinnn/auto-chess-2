@echo off
setlocal
rem ============================================================
rem  AutoTest START - bring up a test session:
rem    1. bridge status (stops on unreachable / compile errors)
rem    2. enter Play mode and wait until it is up (play_timeout_s)
rem  Flags are passed to `play on`, e.g.  start.bat --scene Assets/Scenes/Main.unity
rem  Exit: 0 playing, 2 bridge unreachable, 5 compile errors, 4 Play did not start.
rem  Unattended (agent/CI): set AUTOTEST_NO_PAUSE=1 to skip "pause".
rem ============================================================
cd /d "%~dp0"
set "PYTHONIOENCODING=utf-8"

echo [1/2] Bridge status...
py -m auto_touch status
set "RC=%errorlevel%"
if "%RC%"=="2" goto :unreachable
if "%RC%"=="5" goto :compile

echo.
echo [2/2] Entering Play mode...
py -m auto_touch play on %*
set "RC=%errorlevel%"
if not "%RC%"=="0" goto :play_failed
echo.
echo [OK] Unity is in Play mode. Next: run.bat auto_touch\scenarios\smoke_editor.json
goto :end

:unreachable
echo.
echo [FAIL] Unity bridge not reachable on port 8766. Open the project in Unity and
echo        enable Tools ^> AutoTest ^> Start Server.
goto :end

:compile
echo.
echo [FAIL] Unity has compile errors - Play mode cannot start. See: py -m auto_touch compile-status
goto :end

:play_failed
echo.
echo [FAIL] Play mode did not start (exit %RC%). Check the Unity console.

:end
if not "%AUTOTEST_NO_PAUSE%"=="1" pause
exit /b %RC%
