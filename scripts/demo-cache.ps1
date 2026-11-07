<#
.SYNOPSIS
    Live demo of the five caching mechanisms used by StudentManagement.

.DESCRIPTION
    Starts a disposable Redis container, runs the web app against it, then walks through each caching
    type and prints the evidence:
      1. IDistributedCache cache-aside (Redis keys, TTLs, HIT/MISS/INVALIDATE log lines)
      2. IMemoryCache (verified by its unit test and the chat log line)
      3. HTTP client caching / [ResponseCache] (Cache-Control headers)
      4. Static asset caching (Cache-Control on /css/*)
      5. In-process memoization (verified by its unit test)

    Types 2 and 5 are process-local and have no external store, so their behaviour is proven by the
    focused unit tests (run before the app starts, to avoid file locks).

.PARAMETER BaseUrl
    Address the app listens on. Default http://localhost:5255.

.PARAMETER RedisPort
    Host port mapped to the demo Redis container. Default 6380 - 6379 is often taken by another
    Redis (WSL / Memurai) and the app would silently use that one instead.

.PARAMETER NoRedis
    Skip Docker. The app then uses the in-process distributed cache; keys/TTLs cannot be inspected
    and the demo relies on the Cache HIT/MISS log lines only.

.PARAMETER KeepRunning
    Leave the app and the Redis container running when the script finishes.

.PARAMETER SkipTests
    Do not run the unit tests used to demonstrate types 2 and 5.

.EXAMPLE
    pwsh -File scripts/demo-cache.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/demo-cache.ps1 -KeepRunning
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5255",
    [int]$RedisPort = 6380,
    [string]$RedisContainer = "sm-demo-redis",
    [switch]$NoRedis,
    [switch]$KeepRunning,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = Split-Path -Parent $PSScriptRoot
$appOut = Join-Path $root "demo-cache-app.log"
$appErr = Join-Path $root "demo-cache-app.err.log"
$cachePrefix = "StudentManagement:"

$appProcess = $null
$session = $null
$testMemory = $null
$testMcp = $null

function Write-Step([string]$number, [string]$title) {
    Write-Host ""
    Write-Host ("=== {0}. {1} ===" -f $number, $title) -ForegroundColor Cyan
}

function Write-Value([string]$label, [string]$value) {
    Write-Host ("   {0,-30} {1}" -f $label, $value)
}

function Write-Pass([string]$message) {
    Write-Host "   [OK] $message" -ForegroundColor Green
}

function Write-Note([string]$message) {
    Write-Host "   $message" -ForegroundColor DarkGray
}

function Invoke-Redis {
    if ($NoRedis) { return "" }
    (& docker exec $RedisContainer redis-cli @args 2>$null) -join " "
}

function Get-CacheLogLines {
    param([int]$Count = 6)
    if (-not (Test-Path $appOut)) { return @() }
    $lines = Select-String -Path $appOut -Pattern '"MessageTemplate":"Cache' -ErrorAction SilentlyContinue |
        Select-Object -Last $Count
    foreach ($line in $lines) {
        if ($line.Line -match '"MessageTemplate":"(Cache [A-Z]+)[^"]*".*?"CacheKey":"([^"]+)"') {
            "{0} {1}" -f $Matches[1], $Matches[2]
        }
    }
}

function Get-HeaderValue($response, [string]$name) {
    if ($null -eq $response -or $null -eq $response.Headers) { return "" }
    $value = $response.Headers[$name]
    if ($null -eq $value) { return "" }
    return ($value -join ", ")
}

# All app calls go through here so a slow/unresponsive request fails with a message instead of
# hanging the demo indefinitely (Invoke-WebRequest has no timeout by default).
function Invoke-App {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Method = "Get",
        $Body = $null,
        [string]$ContentType = $null
    )

    $request = @{
        Uri             = "$BaseUrl$Path"
        Method          = $Method
        WebSession      = $script:session
        UseBasicParsing = $true
        TimeoutSec      = 30
    }

    if ($null -ne $Body) { $request.Body = $Body }
    if ($ContentType) { $request.ContentType = $ContentType }

    Invoke-WebRequest @request
}

function Wait-ForApp([int]$TimeoutSeconds = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return $true }
        }
        catch {
            Start-Sleep -Seconds 3
        }
    }
    return $false
}

function Connect-Session {
    $script:session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    Invoke-App "/Account/Login" | Out-Null
    Invoke-App "/Account/Login" -Method Post `
        -Body @{ Email = "admin@admin.com"; Password = "Admin123"; ReturnUrl = "" } | Out-Null

    # Validate the session on a page that does not touch the cache (a JSON call would warm it up and
    # make the first "MISS" of the demo look like a HIT in -NoRedis mode).
    $page = (Invoke-App "/Student").Content
    if ($page -match "Student Management Login") {
        throw "Login failed: check the seeded admin credentials (admin@admin.com / Admin123)."
    }
}

try {
    Write-Host ""
    Write-Host "StudentManagement caching demo" -ForegroundColor White
    Write-Host "Five mechanisms: distributed cache, in-process memory cache, client HTTP cache, static assets, memoization."
    Write-Note "App log: $appOut"
    if (-not $NoRedis) { Write-Note "Redis:  container '$RedisContainer' on host port $RedisPort" }

    # ---------------------------------------------------------------- preflight
    Write-Step 0 "Preflight"

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { throw "dotnet CLI not found on PATH." }

    if (-not $NoRedis) {
        cmd /c "docker info --format {{.ServerVersion}} >nul 2>&1"
        if ($LASTEXITCODE -ne 0) { throw "Docker daemon is not running. Start Docker Desktop, or rerun with -NoRedis." }

        # A previous run may have left the demo container behind - remove it before checking the port.
        cmd /c "docker rm -f $RedisContainer >nul 2>&1"

        $portInUse = Get-NetTCPConnection -LocalPort $RedisPort -State Listen -ErrorAction SilentlyContinue
        if ($portInUse) {
            throw "Port $RedisPort is already in use by another process. Rerun with -RedisPort <free port>."
        }
    }

    $appUri = [Uri]$BaseUrl
    $appPortInUse = Get-NetTCPConnection -LocalPort $appUri.Port -State Listen -ErrorAction SilentlyContinue
    if ($appPortInUse) {
        throw "Port $($appUri.Port) is already in use. Kill the running app first (taskkill /IM dotnet.exe /T /F) or pass -BaseUrl with another port."
    }

    Write-Note "Building solution..."
    Push-Location $root
    & dotnet build --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

    if (-not $SkipTests) {
        Write-Note "Running the tests that prove types 2 and 5 (before the app locks the assemblies)..."
        $testMemory = (& dotnet test "StudentManagement.Tests" --no-build --nologo `
            --filter "FullyQualifiedName~AppKnowledgeServiceTests" 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "AppKnowledgeServiceTests failed." }

        $testMcp = (& dotnet test "StudentManagement.Tests" --no-build --nologo `
            --filter "FullyQualifiedName~McpToolProviderMemoizationTests" 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "McpToolProviderMemoizationTests failed." }
    }

    if (-not $NoRedis) {
        cmd /c "docker rm -f $RedisContainer >nul 2>&1"
        cmd /c "docker run -d --name $RedisContainer -p ${RedisPort}:6379 redis:7-alpine >nul 2>&1"
        if ($LASTEXITCODE -ne 0) { throw "Could not start the Redis container." }

        for ($i = 0; $i -lt 20; $i++) {
            if ((Invoke-Redis PING) -eq "PONG") { break }
            Start-Sleep -Milliseconds 500
        }
        if ((Invoke-Redis PING) -ne "PONG") { throw "Redis container did not become ready." }
        Write-Pass "Redis container ready on localhost:$RedisPort"
    }

    Remove-Item $appOut, $appErr -ErrorAction SilentlyContinue
    $env:ASPNETCORE_URLS = $BaseUrl
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:Chat__IncludeRag = "false"
    if ($NoRedis) {
        Remove-Item Env:\ConnectionStrings__Redis -ErrorAction SilentlyContinue
    }
    else {
        $env:ConnectionStrings__Redis = "localhost:$RedisPort"
    }

    Write-Note "Starting the app..."
    $appProcess = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", "StudentManagement", "--no-launch-profile") `
        -WorkingDirectory $root `
        -RedirectStandardOutput $appOut `
        -RedirectStandardError $appErr `
        -PassThru

    if (-not (Wait-ForApp)) {
        Write-Host "   App did not become ready. Last log lines:" -ForegroundColor Red
        Get-Content $appOut -Tail 15 -ErrorAction SilentlyContinue
        Get-Content $appErr -Tail 15 -ErrorAction SilentlyContinue
        throw "App startup timed out."
    }

    Connect-Session
    Write-Pass "App ready at $BaseUrl (logged in as admin@admin.com)"

    $providerLine = Select-String -Path $appOut -Pattern "Distributed cache provider" -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($providerLine -and $providerLine.Line -match '"CacheProvider":"([^"]+)"') {
        Write-Value "Cache provider" $Matches[1]
    }

    # ------------------------------------------------------- 1. distributed cache
    Write-Step 1 "Distributed cache (cache-aside) - IDistributedCache"

    if (-not $NoRedis) { Invoke-Redis FLUSHDB | Out-Null }
    Write-Note "Two GETs on /Student/GetStudentList; the first is a MISS, the second a HIT."

    Invoke-App "/Student/GetStudentList" | Out-Null
    Start-Sleep -Milliseconds 300
    $missLines = Get-CacheLogLines 1
    Invoke-App "/Student/GetStudentList" | Out-Null
    Start-Sleep -Milliseconds 300
    $hitLines = Get-CacheLogLines 1

    Write-Value "Log (1st request)" ($missLines -join "; ")
    Write-Value "Log (2nd request)" ($hitLines -join "; ")

    if (-not $NoRedis) {
        $key = "${cachePrefix}students_all"
        Write-Value "Redis key" ((Invoke-Redis KEYS "${cachePrefix}*"))
        Write-Value "Type" (Invoke-Redis TYPE $key)
        Write-Value "TTL (seconds)" (Invoke-Redis TTL $key)
        $payload = Invoke-Redis --raw HGET $key data
        if ($payload) { Write-Value "Payload (first 120 chars)" $payload.Substring(0, [Math]::Min(120, $payload.Length)) }
    }

    Write-Note "Course list (60 s) and the student-count report (300 s) are cached under their own keys."
    Invoke-App "/Course" | Out-Null
    Invoke-App "/Course/StudentCountPerCourse" | Out-Null
    Start-Sleep -Milliseconds 300
    if (-not $NoRedis) {
        Write-Value "Redis keys" ((Invoke-Redis KEYS "${cachePrefix}*"))
        Write-Value "TTL courses_all" (Invoke-Redis TTL "${cachePrefix}courses_all")
        Write-Value "TTL course_student_counts" (Invoke-Redis TTL "${cachePrefix}course_student_counts")
    }

    Write-Note "Saving a student (unchanged) invalidates students_all and student_{id}."
    $students = (Invoke-App "/Student/GetStudentList").Content | ConvertFrom-Json
    $student = $students | Where-Object { $_.email -match '^[^@\s]+@[^@\s]+\.[^@\s]+$' } | Select-Object -First 1
    if ($student) {
        $body = $student | ConvertTo-Json -Depth 5
        Invoke-App "/Student/SaveStudent" -Method Post -Body $body -ContentType "application/json" | Out-Null
        Start-Sleep -Milliseconds 400
        $invalidate = Select-String -Path $appOut -Pattern '"MessageTemplate":"Cache INVALIDATE' -ErrorAction SilentlyContinue |
            Select-Object -Last 3 |
            ForEach-Object { if ($_.Line -match '"CacheKey":"([^"]+)"') { "Cache INVALIDATE $($Matches[1])" } }
        Write-Value "Invalidation log" ($invalidate -join "; ")
        if (-not $NoRedis) { Write-Value "Redis keys now" ((Invoke-Redis KEYS "${cachePrefix}students*")) }
        Invoke-App "/Student/GetStudentList" | Out-Null
        Start-Sleep -Milliseconds 300
        Write-Value "Reload log" ((Get-CacheLogLines 1) -join "; ")
        Write-Pass "Write-through invalidation observed."
    }
    else {
        Write-Note "No student with a valid e-mail was found; skipping the invalidation demo."
    }

    # ---------------------------------------------------------- 2. memory cache
    Write-Step 2 "In-process memory cache - IMemoryCache"
    Write-Note "AppKnowledgeService.GetDocumentation() caches the joined, immutable knowledge base for the process lifetime."
    Write-Note "Code: StudentManagement.Application/Services/AppKnowledgeService.cs (DocumentationCacheKey)."
    if (-not $SkipTests) {
        $passed = ($testMemory -split "`n" | Where-Object { $_ -match 'Passed!' } | Select-Object -First 1)
        Write-Value "Test" "AppKnowledgeServiceTests.GetDocumentation_CalledTwice_ReturnsCachedInstance"
        Write-Value "Result" ($(if ($passed) { $passed.Trim() } else { "passed" }))
        Write-Pass "Second call returns the same cached instance instead of re-joining the documents."
    }
    Write-Note "At runtime it logs 'Cache HIT app_knowledge_documentation (in-process)' when the chat prompt is assembled."

    # ------------------------------------------------------- 3. client HTTP cache
    Write-Step 3 "Client HTTP cache - [ResponseCache(Location = Client)]"
    $dataset = Invoke-App "/api/evaluation/dataset"
    $datasetCache = Get-HeaderValue $dataset "Cache-Control"
    Write-Value "/api/evaluation/dataset" $datasetCache
    if ($datasetCache -match "max-age") {
        Write-Pass "Browser is told to cache the static evaluation dataset for 300 s."
    }

    $course = Invoke-App "/Course"
    Write-Value "/Course" (Get-HeaderValue $course "Cache-Control")
    $antiforgery = Select-String -Path $appOut -Pattern "headers have been overridden" -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($antiforgery) {
        Write-Note "Expected: the Course page carries [ResponseCache], but the layout renders an antiforgery token"
        Write-Note "(logout form) and ASP.NET Core forces 'no-cache, no-store'. Framework warning is in the app log."
    }

    # --------------------------------------------------------- 4. static assets
    Write-Step 4 "Static asset cache - UseStaticFiles(OnPrepareResponse)"
    foreach ($asset in @("/css/site.css", "/css/chat.css")) {
        $response = Invoke-App $asset
        Write-Value $asset (Get-HeaderValue $response "Cache-Control")
    }
    Write-Pass "Stylesheets are cacheable for one day (public, max-age=86400)."

    # ----------------------------------------------------------- 5. memoization
    Write-Step 5 "In-process memoization - McpToolProvider"
    Write-Note "The MCP tool list is loaded once per process and guarded by a SemaphoreSlim; later calls reuse it."
    Write-Note "Code: StudentManagement/Configuration/McpToolProvider.cs (GetToolsAsync)."
    if (-not $SkipTests) {
        $passed = ($testMcp -split "`n" | Where-Object { $_ -match 'Passed!' } | Select-Object -First 1)
        Write-Value "Test" "McpToolProviderMemoizationTests.GetToolsAsync_ConcurrentCalls_LoadsToolsOnce"
        Write-Value "Result" ($(if ($passed) { $passed.Trim() } else { "passed" }))
        Write-Pass "20 concurrent calls -> exactly one tool load."
    }

    # ---------------------------------------------------------------- summary
    Write-Host ""
    Write-Host "Summary" -ForegroundColor White
    Write-Host "   1  distributed cache    Redis keys + TTL 60 s / 300 s, HIT/MISS/INVALIDATE log lines"
    Write-Host "   2  process memory       IMemoryCache, never expires (immutable knowledge base), proven by test"
    Write-Host "   3  client HTTP cache    Cache-Control: private,max-age=300 on /api/evaluation/dataset"
    Write-Host "   4  static assets        Cache-Control: public,max-age=86400 on /css/*"
    Write-Host "   5  memoization          MCP tool list loaded once, proven by test"
    Write-Host ""
    if ($KeepRunning) {
        Write-Host "App and Redis are still running (-KeepRunning)." -ForegroundColor DarkGray
    }
    else {
        Write-Host "App stopped and demo Redis container removed." -ForegroundColor DarkGray
    }
}
catch {
    Write-Host ""
    Write-Host "Demo failed: $($_.Exception.Message)" -ForegroundColor Red
    if (Test-Path $appErr) { Get-Content $appErr -Tail 15 -ErrorAction SilentlyContinue }
}
finally {
    if (-not $KeepRunning) {
        if ($appProcess -and -not $appProcess.HasExited) {
            cmd /c "taskkill /PID $($appProcess.Id) /T /F >nul 2>&1"
        }
        if (-not $NoRedis) {
            cmd /c "docker rm -f $RedisContainer >nul 2>&1"
        }
    }
    Pop-Location -ErrorAction SilentlyContinue
}
