@echo off
setlocal
rem ============================================================
rem  AutoTest SETUP (run once per machine / clone)
rem    1. checks the Python launcher `py` (3.10+)
rem    2. installs the /autotest Claude Code skill (install_skill.bat)
rem    3. registers the MCP server "autotest" in <repo>\.mcp.json
rem       (merged: other servers in that file are kept)
rem    4. asks the Unity bridge for its status
rem
rem  Exit code = exit code of step 4 (0 = bridge answered, 2 = Unity not
rem  reachable yet, 5 = Unity has compile errors), or 1 when py is missing,
rem  3 when .mcp.json could not be merged.
rem  Unattended (agent/CI): set AUTOTEST_NO_PAUSE=1 to skip "pause".
rem ============================================================
set "HERE=%~dp0"
cd /d "%HERE%"
set "PYTHONIOENCODING=utf-8"

where py >nul 2>nul
if errorlevel 1 goto :no_py
py -c "import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)"
if errorlevel 1 goto :old_py

echo [1/3] Installing the /autotest skill...
set "AUTOTEST_SKILL_QUIET=1"
call "%HERE%install_skill.bat"
if errorlevel 1 echo [warn] skill install failed - continuing
set "AUTOTEST_SKILL_QUIET="

echo.
echo [2/3] Registering the MCP server in .mcp.json...
py -m auto_touch register-mcp
if errorlevel 1 goto :mcp_failed

echo.
echo [3/3] Asking the Unity bridge for its status...
py -m auto_touch status
set "RC=%errorlevel%"
echo.
if "%RC%"=="0" goto :ok
if "%RC%"=="2" goto :no_unity
if "%RC%"=="5" goto :compile
goto :end

:ok
echo [OK] Setup done and the bridge answers. Restart the Claude Code session so it
echo      loads the /autotest skill and the autotest MCP server. Next: start.bat
goto :end

:no_unity
echo [OK] Setup done, but the Unity bridge is not reachable (port 8766). Open the
echo      project in Unity and enable Tools ^> AutoTest ^> Start Server, then run
echo      status.bat. Restart the Claude Code session to load the skill + MCP server.
goto :end

:compile
echo [!] Setup done, but Unity has compile errors - fix them, then run status.bat.
goto :end

:no_py
echo [FAIL] The Python launcher "py" was not found. Install Python 3.10+ from
echo        python.org with "py launcher" ticked, then run setup.bat again.
set "RC=1"
goto :end

:old_py
echo [FAIL] auto_touch needs Python 3.10 or newer (py -V says otherwise).
set "RC=1"
goto :end

:mcp_failed
set "RC=3"
echo [FAIL] Could not merge the MCP server into .mcp.json (see the message above).

:end
if not "%AUTOTEST_NO_PAUSE%"=="1" pause
exit /b %RC%
