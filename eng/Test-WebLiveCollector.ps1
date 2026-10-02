[CmdletBinding()]
param(
    [string] $WebSampleDll = '',
    [ValidateRange(1, 30)] [int] $ShutdownBudgetSeconds = 10
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'observability'
if ([string]::IsNullOrWhiteSpace($WebSampleDll)) {
    $WebSampleDll = Join-Path $root 'artifacts/consumer-bin/web/OnuObservability.Sample.Web.dll'
}
$WebSampleDll = [IO.Path]::GetFullPath($WebSampleDll)
if (-not (Test-Path -LiteralPath $WebSampleDll)) {
    throw "Web sample not found: $WebSampleDll. Run Test-LocalPackages.ps1 first."
}
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker CLI is required; this script does not install it.'
}

function Get-AvailableTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint] $listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

$captureRoot = Join-Path $root ("artifacts/live-collector/{0}" -f [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmssfff'))
$null = New-Item -ItemType Directory -Path $captureRoot
if (-not $IsWindows) { & chmod 0777 $captureRoot }
$captureFile = Join-Path $captureRoot 'telemetry.jsonl'
$collectorLogFile = Join-Path $captureRoot 'collector.log'
$stdoutFile = Join-Path $captureRoot 'web.stdout.log'
$stderrFile = Join-Path $captureRoot 'web.stderr.log'
$projectName = "onu-web-test-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
$grpcPort = Get-AvailableTcpPort
$httpPort = Get-AvailableTcpPort
$healthPort = Get-AvailableTcpPort
$metricsPort = Get-AvailableTcpPort
$webPort = Get-AvailableTcpPort
$compose = @(
    'compose', '--project-name', $projectName, '--project-directory', $assets,
    '--file', (Join-Path $assets 'docker-compose.yml'),
    '--file', (Join-Path $assets 'docker-compose.test.yml')
)

$previous = @{
    OTEL_COLLECTOR_CONFIG = $env:OTEL_COLLECTOR_CONFIG
    OTEL_TEST_CAPTURE_PATH = $env:OTEL_TEST_CAPTURE_PATH
    OTEL_GRPC_PORT = $env:OTEL_GRPC_PORT
    OTEL_HTTP_PORT = $env:OTEL_HTTP_PORT
    OTEL_HEALTH_PORT = $env:OTEL_HEALTH_PORT
    OTEL_METRICS_PORT = $env:OTEL_METRICS_PORT
    OTEL_EXPORTER_OTLP_ENDPOINT = $env:OTEL_EXPORTER_OTLP_ENDPOINT
    OTEL_EXPORTER_OTLP_PROTOCOL = $env:OTEL_EXPORTER_OTLP_PROTOCOL
    OTEL_TRACES_EXPORTER = $env:OTEL_TRACES_EXPORTER
    OTEL_METRICS_EXPORTER = $env:OTEL_METRICS_EXPORTER
    OTEL_LOGS_EXPORTER = $env:OTEL_LOGS_EXPORTER
    ONU_TEST_OTLP_ENDPOINT = $env:ONU_TEST_OTLP_ENDPOINT
    ONU_WEB_SAMPLE_DLL = $env:ONU_WEB_SAMPLE_DLL
    ONU_WORKER_SAMPLE_DLL = $env:ONU_WORKER_SAMPLE_DLL
    ONU_MCP_SAMPLE_DLL = $env:ONU_MCP_SAMPLE_DLL
}
$web = $null
$webStdout = $null
$webStderr = $null
$collectorStarted = $false
try {
    $env:OTEL_COLLECTOR_CONFIG = 'collector.local.yaml'
    $env:OTEL_TEST_CAPTURE_PATH = $captureRoot
    $env:OTEL_GRPC_PORT = $grpcPort
    $env:OTEL_HTTP_PORT = $httpPort
    $env:OTEL_HEALTH_PORT = $healthPort
    $env:OTEL_METRICS_PORT = $metricsPort
    & docker @compose up --detach
    if ($LASTEXITCODE -ne 0) { throw 'The test collector failed to start.' }
    $collectorStarted = $true

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        try {
            $healthy = (Invoke-WebRequest "http://127.0.0.1:$healthPort/" -TimeoutSec 2).StatusCode -eq 200
        }
        catch {
            $healthy = $false
            Start-Sleep -Milliseconds 200
        }
    } until ($healthy -or [DateTimeOffset]::UtcNow -ge $deadline)
    if (-not $healthy) { throw 'The test collector did not become healthy within 30 seconds.' }
    $otlpProbe = Invoke-WebRequest "http://127.0.0.1:$httpPort/v1/traces" -Method Post `
        -ContentType 'application/x-protobuf' -Body ([byte[]]::new(0)) -SkipHttpErrorCheck
    if ($otlpProbe.StatusCode -notin 200, 400) {
        throw "The OTLP/HTTP receiver probe returned HTTP $($otlpProbe.StatusCode)."
    }

    $env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://127.0.0.1:$httpPort"
    $env:OTEL_EXPORTER_OTLP_PROTOCOL = 'http/protobuf'
    $env:OTEL_TRACES_EXPORTER = 'otlp'
    $env:OTEL_METRICS_EXPORTER = 'otlp'
    $env:OTEL_LOGS_EXPORTER = 'otlp'
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = Split-Path -Parent $WebSampleDll
    $null = $start.ArgumentList.Add($WebSampleDll)
    $null = $start.ArgumentList.Add('--urls')
    $null = $start.ArgumentList.Add("http://127.0.0.1:$webPort")
    $web = [Diagnostics.Process]::Start($start)
    if ($null -eq $web) { throw 'The Web sample process could not be started.' }
    $webStdout = $web.StandardOutput.ReadToEndAsync()
    $webStderr = $web.StandardError.ReadToEndAsync()

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        if ($web.HasExited) { throw "The Web sample exited during startup with code $($web.ExitCode)." }
        try {
            $ready = (Invoke-WebRequest "http://127.0.0.1:$webPort/" -TimeoutSec 2).StatusCode -eq 200
        }
        catch {
            $ready = $false
            Start-Sleep -Milliseconds 100
        }
    } until ($ready -or [DateTimeOffset]::UtcNow -ge $deadline)
    if (-not $ready) { throw 'The Web sample did not become ready within 15 seconds.' }

    $traceId = '4bf92f3577b34da6a3ce929d0e0e4736'
    $headers = @{ traceparent = "00-$traceId-00f067aa0ba902b7-01" }
    $correlation = Invoke-RestMethod "http://127.0.0.1:$webPort/correlation" -Headers $headers
    if ($correlation.traceId -ne $traceId) { throw 'W3C trace propagation was not preserved.' }
    $canary = 'onu-sensitive-canary-7f3d9a'
    $null = Invoke-WebRequest "http://127.0.0.1:$webPort/users/$canary"
    $failure = Invoke-WebRequest "http://127.0.0.1:$webPort/failure" -SkipHttpErrorCheck
    if ($failure.StatusCode -ne 500) { throw 'The deterministic failure route did not return HTTP 500.' }
    Start-Sleep -Seconds 6
    try { $null = Invoke-WebRequest "http://127.0.0.1:$webPort/shutdown" -Method Post -SkipHttpErrorCheck } catch { }
    $web | Wait-Process -Timeout $ShutdownBudgetSeconds -ErrorAction Stop
    if ($web.ExitCode -ne 0) { throw "The Web sample exited with code $($web.ExitCode)." }
    $webStdout.GetAwaiter().GetResult() | Set-Content -LiteralPath $stdoutFile
    $webStderr.GetAwaiter().GetResult() | Set-Content -LiteralPath $stderrFile

    $env:ONU_TEST_OTLP_ENDPOINT = "http://127.0.0.1:$httpPort"
    $env:ONU_WEB_SAMPLE_DLL = $WebSampleDll
    $env:ONU_WORKER_SAMPLE_DLL = Join-Path $root 'artifacts/consumer-bin/worker/OnuObservability.Sample.Worker.dll'
    $env:ONU_MCP_SAMPLE_DLL = Join-Path $root 'artifacts/consumer-bin/mcp/OnuObservability.Sample.Mcp.dll'
    $integrationProject = Join-Path $root 'tests/OnuObservability.Integration.Tests/OnuObservability.Integration.Tests.csproj'
    & dotnet test $integrationProject -c Release --no-build --nologo `
        --filter 'FullyQualifiedName~WorkerSampleProcessTests|FullyQualifiedName~McpSampleIntegrationTests'
    if ($LASTEXITCODE -ne 0) { throw 'The live Worker or MCP exact-package process test failed.' }

    Start-Sleep -Seconds 2
    if (-not (Test-Path -LiteralPath $captureFile)) { throw 'The collector produced no capture file.' }
    $capture = Get-Content -Raw -LiteralPath $captureFile
    if ([string]::IsNullOrWhiteSpace($capture)) { throw 'The collector capture file was empty.' }
    if ($capture -notmatch $traceId) { throw 'The collector capture did not contain the propagated trace.' }
    if ($capture -notmatch 'GET /correlation') { throw 'The collector capture did not contain the bounded route span.' }
    if ($capture -notmatch 'onu.http.server.request') { throw 'The collector capture did not contain Web request metrics.' }
    if ($capture -notmatch 'onu-worker-sample') { throw 'The collector capture did not contain Worker telemetry.' }
    if ($capture -notmatch 'onu-mcp-sample') { throw 'The collector capture did not contain MCP telemetry.' }
    if ($capture.Contains($canary, [StringComparison]::Ordinal)) { throw 'The raw route-value canary reached collector capture.' }

    [ordered]@{
        capture = $captureFile.Substring($root.Length + 1).Replace('\', '/')
        traceId = $traceId
        gracefulShutdown = $true
        canaryAbsent = $true
        exactPackageAdapters = @('Web', 'Worker', 'MCP')
    } | ConvertTo-Json
}
finally {
    if ($web -and -not $web.HasExited) {
        $web.Kill($true)
        $web.WaitForExit()
    }
    if ($web -and $web.HasExited) {
        if ($webStdout) { $webStdout.GetAwaiter().GetResult() | Set-Content -LiteralPath $stdoutFile }
        if ($webStderr) { $webStderr.GetAwaiter().GetResult() | Set-Content -LiteralPath $stderrFile }
    }
    if ($collectorStarted) {
        & docker @compose logs --no-color 2>&1 | Set-Content -LiteralPath $collectorLogFile
        & docker @compose down --remove-orphans
    }
    foreach ($entry in $previous.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            Remove-Item -Path "Env:$($entry.Key)" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -Path "Env:$($entry.Key)" -Value $entry.Value
        }
    }
}
