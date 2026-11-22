[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host ""
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "    서울수상레포츠센터 요트교육 시뮬레이터 v1.0 (바탕화면 바로가기 등록)" -ForegroundColor White
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host " 바로가기를 생성하고 있습니다..." -ForegroundColor Gray

try {
    $curDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutPath = Join-Path $desktop "서울수상레포츠센터 요트교육 v1.0.lnk"

    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($shortcutPath)

    $edgePath1 = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    $edgePath2 = "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    $chromePath = "C:\Program Files\Google\Chrome\Application\chrome.exe"
    $htmlFile = Join-Path $curDir "index.html"
    $iconFile = Join-Path $curDir "app_icon.ico"
    $htmlUri = "file:///" + ($htmlFile -replace "\\", "/")

    if (Test-Path $edgePath1) {
        $sc.TargetPath = $edgePath1
        $sc.Arguments = "--app=`"$htmlUri`""
    } elseif (Test-Path $edgePath2) {
        $sc.TargetPath = $edgePath2
        $sc.Arguments = "--app=`"$htmlUri`""
    } elseif (Test-Path $chromePath) {
        $sc.TargetPath = $chromePath
        $sc.Arguments = "--app=`"$htmlUri`""
    } else {
        $sc.TargetPath = $htmlFile
    }

    $sc.WorkingDirectory = $curDir
    $sc.Description = "서울수상레포츠센터 요트교육 시뮬레이터 v1.0"
    if (Test-Path $iconFile) {
        $sc.IconLocation = "$iconFile,0"
    }
    $sc.Save()

    Write-Host ""
    Write-Host " [성공] 바탕화면에 [서울수상레포츠센터 요트교육 v1.0] 바로가기가 생성되었습니다!" -ForegroundColor Green
    Write-Host " 이제 바탕화면의 아이콘을 더블클릭하시면 앱 창으로 바로 실행됩니다." -ForegroundColor Yellow
    Write-Host ""
} catch {
    Write-Host " [오류] 바로가기 생성 중 오류 발생: $_" -ForegroundColor Red
}
Write-Host "==============================================================================" -ForegroundColor Cyan
