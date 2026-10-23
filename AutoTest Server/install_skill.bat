@echo off
setlocal
rem ============================================================
rem  AutoTest INSTALL SKILL - copy skills\autotest\ (the source of truth)
rem  to <project>\.claude\skills\autotest\ so "/autotest ..." is offered.
rem
rem  Usage:  install_skill.bat              project root = the folder above this one
rem          install_skill.bat "D:\Other"   install into another project root
rem
rem  Edit skills\autotest\SKILL.md here, then run this again; restart the
rem  Claude Code session afterwards so it re-scans .claude\skills\.
rem  Exit codes: 0 installed, 1 source missing, 2 target root is not a folder,
rem              3 copy failed.
rem  Unattended (agent/CI): set AUTOTEST_NO_PAUSE=1 to skip "pause".
rem ============================================================
set "HERE=%~dp0"
cd /d "%HERE%"
set "PYTHONIOENCODING=utf-8"

if "%~1"=="" (set "ROOT=%HERE%..") else (set "ROOT=%~1")
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "SRC=%HERE%skills\autotest"
set "DST=%ROOT%\.claude\skills\autotest"

if not exist "%SRC%\SKILL.md" goto :no_src
if not exist "%ROOT%\" goto :bad_root
if not exist "%DST%\" md "%DST%"
xcopy /E /I /Y /Q "%SRC%" "%DST%" >nul
if errorlevel 1 goto :copy_failed
echo [ok] /autotest -^> %DST%\SKILL.md
echo      Restart the Claude Code session in that project so it picks the skill up.
set "RC=0"
goto :end

:no_src
echo [FAIL] Source skill not found: %SRC%\SKILL.md
set "RC=1"
goto :end

:bad_root
echo [FAIL] Target project root is not a folder: %ROOT%
set "RC=2"
goto :end

:copy_failed
echo [FAIL] Could not copy %SRC% to %DST%
set "RC=3"

:end
if not "%AUTOTEST_NO_PAUSE%"=="1" if not defined AUTOTEST_SKILL_QUIET pause
exit /b %RC%
