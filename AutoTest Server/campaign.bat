@echo off
setlocal
rem ============================================================
rem  AutoTest CAMPAIGN - run a campaign file (a list of scenarios, one
rem  Play session unless the file says freshPlay) as one verdict.
rem
rem  Examples:
rem    campaign.bat auto_touch\scenarios\smoke_campaign.json
rem    campaign.bat smoke_campaign.json --exit-play
rem
rem  Flags: --enter-play --exit-play --no-stop --verbose --param k=v
rem  Exit: 0 every case passed, 2 unreachable, 3 invalid file, 4 a case failed,
rem        5 compile errors.
rem  Report: auto_touch\out\reports\<stem>_campaign_<ts>.md + .csv
rem  Unattended (agent/CI): set AUTOTEST_NO_PAUSE=1 to skip "pause".
rem ============================================================
cd /d "%~dp0"
set "PYTHONIOENCODING=utf-8"

if "%~1"=="" goto :usage
py -m auto_touch campaign %*
set "RC=%errorlevel%"
echo.
if "%RC%"=="0" echo [PASS] Every case passed. Report: auto_touch\out\reports\
if "%RC%"=="2" echo [ERROR] Unity bridge unreachable - open Unity, Tools ^> AutoTest ^> Start Server.
if "%RC%"=="3" echo [INVALID] The campaign file or a scenario in it is invalid (see above).
if "%RC%"=="4" echo [FAIL] At least one case failed - see the campaign report in auto_touch\out\reports\
if "%RC%"=="5" echo [ERROR] Unity has compile errors - fix them first.
goto :end

:usage
echo Usage: campaign.bat ^<campaign.json^> [--enter-play] [--exit-play] [--no-stop] [--verbose]
echo.
py -m auto_touch list
set "RC=3"

:end
if not "%AUTOTEST_NO_PAUSE%"=="1" pause
exit /b %RC%
