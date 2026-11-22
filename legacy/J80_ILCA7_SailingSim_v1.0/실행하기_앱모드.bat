@echo off
set "CUR_DIR=%~dp0"
set "HTML_FILE=%CUR_DIR%index.html"
set "HTML_URI=file:///%HTML_FILE:\=/%"

set "EDGE1=C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
set "EDGE2=C:\Program Files\Microsoft\Edge\Application\msedge.exe"
set "CHROME=C:\Program Files\Google\Chrome\Application\chrome.exe"

if exist "%EDGE1%" (
    start "" "%EDGE1%" --app="%HTML_URI%"
    exit /b
)

if exist "%EDGE2%" (
    start "" "%EDGE2%" --app="%HTML_URI%"
    exit /b
)

if exist "%CHROME%" (
    start "" "%CHROME%" --app="%HTML_URI%"
    exit /b
)

start "" "%HTML_FILE%"
exit /b
