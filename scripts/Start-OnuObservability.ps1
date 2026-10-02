[CmdletBinding()]
param(
    [ValidateSet('Local', 'Production', 'Persistent')] [string] $Mode = 'Local',
    [ValidateRange(1, 65535)] [int] $GrpcPort = 4317,
    [ValidateRange(1, 65535)] [int] $HttpPort = 4318,
    [ValidateRange(1, 65535)] [int] $HealthPort = 13133,
    [ValidateRange(1, 65535)] [int] $MetricsPort = 8888,
    [ValidatePattern('^[a-z0-9][a-z0-9-]*$')] [string] $ProjectName = 'onu-observability'
)

$ErrorActionPreference = 'Stop'
$assets = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\observability'))
$compose = Join-Path $assets 'docker-compose.yml'
$persistent = Join-Path $assets 'docker-compose.persistent.yml'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker CLI is required; this script does not install it.' }
& docker compose version *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker Compose is unavailable.' }

$config = switch ($Mode) {
    Local { 'collector.local.yaml' }
    Production { 'collector.production.yaml' }
    Persistent { 'collector.persistent.yaml' }
}
if ($Mode -ne 'Local') {
    if ([string]::IsNullOrWhiteSpace($env:OTEL_BACKEND_ENDPOINT)) { throw 'OTEL_BACKEND_ENDPOINT is required.' }
    $backend = [uri] $env:OTEL_BACKEND_ENDPOINT
    if ($backend.Scheme -ne 'https') { throw 'OTEL_BACKEND_ENDPOINT must use HTTPS.' }
    if ([string]::IsNullOrWhiteSpace($env:OTEL_BACKEND_AUTHORIZATION)) { throw 'OTEL_BACKEND_AUTHORIZATION must come from the environment or a secret manager.' }
}
if ($Mode -eq 'Persistent') {
    Write-Warning 'Persistent telemetry can contain operationally sensitive data. Approve retention, access, encryption, incident response, and secure deletion before use.'
}

$env:OTEL_COLLECTOR_CONFIG = $config
$env:OTEL_GRPC_PORT = $GrpcPort.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:OTEL_HTTP_PORT = $HttpPort.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:OTEL_HEALTH_PORT = $HealthPort.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:OTEL_METRICS_PORT = $MetricsPort.ToString([Globalization.CultureInfo]::InvariantCulture)
$arguments = @('compose', '--project-name', $ProjectName, '--project-directory', $assets, '--file', $compose)
if ($Mode -eq 'Persistent') { $arguments += @('--file', $persistent) }
$arguments += @('up', '--detach')
& docker @arguments
if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed to start $ProjectName." }

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
do {
    try {
        $healthy = (Invoke-WebRequest -Uri "http://127.0.0.1:$HealthPort/" -TimeoutSec 2 -UseBasicParsing).StatusCode -eq 200
    } catch { $healthy = $false; Start-Sleep -Milliseconds 500 }
} until ($healthy -or [DateTimeOffset]::UtcNow -ge $deadline)
if (-not $healthy) { throw "Collector did not become healthy within 45 seconds. Run Check-OnuObservability.ps1." }

[pscustomobject]@{
    mode = $Mode
    otlpGrpc = "http://127.0.0.1:$GrpcPort"
    otlpHttp = "http://127.0.0.1:$HttpPort"
    health = "http://127.0.0.1:$HealthPort/"
    selfMetrics = "http://127.0.0.1:$MetricsPort/metrics"
    persistence = ($Mode -eq 'Persistent')
    readiness = 'collector_started_not_production_approved'
} | ConvertTo-Json
