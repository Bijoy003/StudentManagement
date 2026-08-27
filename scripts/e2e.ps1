param(
    [switch]$KeepDb,
    [switch]$Headed,
    [string]$BaseUrl = 'http://localhost:5255'
)

# Runs the full E2E suite (e2e/ — Node.js Playwright, spec.ts files):
#   1. starts a disposable SQL Server container (or reuses an existing one)
#   2. starts the web app with the DB pointed at that container
#   3. installs npm dependencies + Playwright browsers if needed
#   4. runs `npx playwright test` (config in e2e/playwright.config.ts)
#   5. cleans up the app process (and the DB container unless -KeepDb)
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$e2eDir = Join-Path $repoRoot 'e2e'
$dbContainer = 'studentmanagement-e2e-db'
$dbPassword = 'YourStrong@Password1'
$additionalConfig = Join-Path $repoRoot 'scripts\appsettings.e2e.json'
$startTime = Get-Date
$port = ([Uri]$BaseUrl).Port
if (-not $port) { $port = 80 }

if (-not (Test-Path $additionalConfig)) {
    throw "Missing $additionalConfig - create it with content '{ }'"
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

    # --- 3/4. Install deps + run Playwright tests -----------------------------
    if (-not (Test-Path (Join-Path $e2eDir 'node_modules'))) {
        Write-Host 'Installing npm dependencies...'
        Push-Location $e2eDir
        try { npm install --no-fund --no-audit }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "npm install failed (exit $LASTEXITCODE)." }
    }

    # Install Playwright browsers on first run (reuses the shared ms-playwright cache).
    $playwrightCache = if ($IsWindows) { "$env:LOCALAPPDATA\ms-playwright" } else { "$HOME/.cache/ms-playwright" }
    if (-not (Test-Path $playwrightCache)) {
        Write-Host 'Installing Playwright browsers...'
        Push-Location $e2eDir
        try { npx playwright install chromium }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "Playwright browser install failed (exit $LASTEXITCODE)." }
    }

    $env:E2E_BASE_URL = $BaseUrl
    if ($Headed) { $env:HEADED = '1' } else { $env:HEADED = $null }
    Push-Location $e2eDir
    try { npx playwright test }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "Playwright tests failed (exit $LASTEXITCODE)." }
}
finally {
    Write-Host 'Stopping web app...'
    # Kill whatever is listening on the app port (the Kestrel child process),
    # then the `dotnet run` parent, then any stray dotnet started for this run.
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    if ($appProc -and -not $appProc.HasExited) {
        if ($IsWindows) {
            # dotnet run spawns a child process; kill the whole tree.
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

# --- 5. Cleanup ----------------------------------------------------------------
if (-not $KeepDb) {
    Write-Host "Removing container '$dbContainer'..."
    docker rm -f $dbContainer | Out-Null
}
else {
    Write-Host "Keeping container '$dbContainer' (use -KeepDb to reuse across runs)."
}
Write-Host 'E2E run complete.'
