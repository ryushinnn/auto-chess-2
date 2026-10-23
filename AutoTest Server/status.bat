@echo off
setlocal
rem ============================================================
rem  AutoTest STATUS - is the Unity bridge up, playing, compiling?
rem  Flags are passed to `py -m auto_touch status` (e.g. --json).
rem  Exit: 0 ok, 2 bridge unreachable, 5 Unity compile errors.
rem  Unattended (agent/CI): set AUTOTEST_NO_PAUSE=1 to skip "pause".
rem ============================================================
cd /d "%~dp0"
set "PYTHONIOENCODING=utf-8"

py -m auto_touch status %*
set "RC=%errorlevel%"
if "%RC%"=="2" (
  echo.
  echo [FAIL] Unity bridge not reachable on port 8766. Is the project open in Unity with
  echo        Tools ^> AutoTest ^> Start Server enabled?
)
if "%RC%"=="5" (
  echo.
  echo [FAIL] Unity has compile errors: py -m auto_touch compile-status
)
if not "%AUTOTEST_NO_PAUSE%"=="1" pause
exit /b %RC%
