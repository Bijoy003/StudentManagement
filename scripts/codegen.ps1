param(
    [string]$BaseUrl = 'http://localhost:5255',
    [string]$Output = 'e2e\tests\codegen.spec.ts',
    [string]$Target = 'playwright',
    [string]$Browser = 'chromium',
    [string]$StorageFile = '',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

# Launches Playwright codegen (test generator) against the running app so you can
# click around and record a Playwright spec (spec.ts). Mirrors scripts/e2e.ps1 infra:
#   1. starts the SQL Server container if needed
#   2. starts the web app on $BaseUrl
#   3. installs npm dependencies + browsers (uses e2e/ package)
#   4. runs `npx playwright codegen --target playwright` — a browser + recorder window
#      opens; do your clicks, then CLOSE that window. Codegen writes the generated
#      test to $Output and the script cleans up the app + container.
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$e2eDir = Join-Path $repoRoot 'e2e'
$dbContainer = 'studentmanagement-e2e-db'
$dbPassword = 'YourStrong@Password1'
$additionalConfig = Join-Path $repoRoot 'scripts\appsettings.e2e.json'
$startTime = Get-Date
$port = ([Uri]$BaseUrl).Port
if (-not $port) { $port = 80 }

if (-not $Output.Contains(':')) {
    $Output = Join-Path $repoRoot $Output
}
$outputDir = Split-Path -Parent $Output
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# --- 1. SQL Server container -------------------------------------------------
$existing = docker ps -a --filter "name=$dbContainer" --format '{{.Names}}'
if (-not $existing) {
    Write-Host "Starting SQL Server container '$dbContainer'..."
    docker run -d --name $dbContainer `
        -e ACCEPT_EULA=Y `
        -e "SA_PASSWORD=$dbPassword" `
        -p 1433:1433 `
        mcr.microsoft.com/mssql/server:2022-latest | Out-Null
}
else {
    $running = docker ps --filter "name=$dbContainer" --filter "status=running" --format '{{.Names}}'
    if (-not $running) {
        Write-Host "Starting existing container '$dbContainer'..."
        docker start $dbContainer | Out-Null
    }
}

Write-Host 'Waiting for SQL Server to accept connections...'
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    if (Test-NetConnection -ComputerName localhost -Port 1433 -WarningAction SilentlyContinue | Select-Object -ExpandProperty TcpTestSucceeded) {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw "SQL Server did not become ready within 120s." }
Write-Host 'SQL Server is ready.'
Start-Sleep -Seconds 5   # give sqlcmd init a moment

# --- 2. Web app ---------------------------------------------------------------
Write-Host "Building and starting web app on $BaseUrl ..."
$env:ConnectionStrings__DatabaseConnection = "Server=localhost,1433;Database=StudentManagement;User Id=sa;Password=$dbPassword;TrustServerCertificate=True;Encrypt=False"
$env:AdditionalConfig__Path = $additionalConfig
$env:ASPNETCORE_ENVIRONMENT = 'Production'

$startArgs = @('run', '--project', (Join-Path $repoRoot 'StudentManagement'), '--urls', $BaseUrl)
if ($IsWindows) {
    $appProc = Start-Process -FilePath 'dotnet' -ArgumentList $startArgs -PassThru -WindowStyle Hidden
}
else {
    $appProc = Start-Process -FilePath 'dotnet' -ArgumentList $startArgs -PassThru -NoNewWindow
}

try {
    $appReady = $false
    for ($i = 0; $i -lt 90; $i++) {
        try {
            $resp = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -UseBasicParsing -TimeoutSec 3
            if ($resp.StatusCode -eq 200) { $appReady = $true; break }
        }
        catch { Start-Sleep -Seconds 2 }
    }
    if (-not $appReady) { throw "Web app did not start within 180s." }
    Write-Host 'Web app is ready.'

    # --- 3. Install npm deps + browsers (playwright CLI comes from e2e/ package)
    if (-not (Test-Path (Join-Path $e2eDir 'node_modules'))) {
        Write-Host 'Installing npm dependencies...'
        Push-Location $e2eDir
        try { npm install --no-fund --no-audit }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "npm install failed (exit $LASTEXITCODE)." }
    }

    $playwrightCache = if ($IsWindows) { "$env:LOCALAPPDATA\ms-playwright" } else { "$HOME/.cache/ms-playwright" }
    if (-not (Test-Path $playwrightCache)) {
        Write-Host 'Installing Playwright browsers...'
        Push-Location $e2eDir
        try { npx playwright install chromium }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "Playwright browser install failed (exit $LASTEXITCODE)." }
    }

    # --- 4. Codegen ------------------------------------------------------------
    $codegenArgs = @('codegen', '--target', $Target, '-b', $Browser)
    if ($StorageFile) {
        $storagePath = if ($StorageFile.Contains(':')) { $StorageFile } else { Join-Path $repoRoot $StorageFile }
        $codegenArgs += @('--save-storage', $storagePath)
        $codegenArgs += @('--load-storage', $storagePath)
    }
    if ($ExtraArgs) { $codegenArgs += $ExtraArgs }
    $codegenArgs += @('-o', $Output, $BaseUrl)

    Write-Host ''
    Write-Host "Opening Playwright codegen at $BaseUrl ..."
    Write-Host "Target: $Target | Output: $Output"
    Write-Host 'Record your actions in the browser, then CLOSE the recorder window to write the test.'
    Write-Host ''
    Push-Location $e2eDir
    try { npx @codegenArgs }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "playwright codegen exited with code $LASTEXITCODE." }

    Write-Host ''
    Write-Host "Generated spec written to: $Output"
    Write-Host 'Next: adapt it to the e2e/ conventions (import { test, expect } from ./fixtures, use login/unique helpers, keep selectors role/id-based).'
}
finally {
    Write-Host 'Stopping web app...'
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    if ($appProc -and -not $appProc.HasExited) {
        if ($IsWindows) {
            taskkill /PID $appProc.Id /T /F 2>$null | Out-Null
        }
        else {
            Stop-Process -Id $appProc.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Get-Process dotnet -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -gt $startTime } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host 'Removing SQL Server container...'
docker rm -f $dbContainer | Out-Null
Write-Host 'Codegen run complete.'
