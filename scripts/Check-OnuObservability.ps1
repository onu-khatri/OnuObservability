[CmdletBinding()]
param(
    [ValidateRange(1, 65535)] [int] $HealthPort = 13133,
    [ValidateRange(1, 65535)] [int] $MetricsPort = 8888,
    [ValidatePattern('^[a-z0-9][a-z0-9-]*$')] [string] $ProjectName = 'onu-observability'
)

$ErrorActionPreference = 'Stop'
$messages = [Collections.Generic.List[string]]::new()
$dockerCli = $null -ne (Get-Command docker -ErrorAction SilentlyContinue)
$daemon = $false
$running = $false
if ($dockerCli) {
    & docker info *> $null
    $daemon = $LASTEXITCODE -eq 0
    if ($daemon) {
        $id = & docker ps --filter "label=com.docker.compose.project=$ProjectName" --filter 'label=com.docker.compose.service=collector' --format '{{.ID}}' | Select-Object -First 1
        $running = -not [string]::IsNullOrWhiteSpace($id)
    }
} else { $messages.Add('Docker CLI is unavailable.') }
if (-not $daemon) { $messages.Add('Docker daemon is unavailable.') }
if (-not $running) { $messages.Add("Collector for $ProjectName is not running.") }

try { $health = (Invoke-WebRequest -Uri "http://127.0.0.1:$HealthPort/" -TimeoutSec 3 -UseBasicParsing).StatusCode -eq 200 }
catch { $health = $false; $messages.Add('Collector health endpoint is unavailable.') }
try {
    $metrics = (Invoke-WebRequest -Uri "http://127.0.0.1:$MetricsPort/metrics" -TimeoutSec 3 -UseBasicParsing).Content
    $metricsReachable = $true
} catch { $metrics = ''; $metricsReachable = $false; $messages.Add('Collector self-metrics are unavailable.') }

$operational = $dockerCli -and $daemon -and $running -and $health -and $metricsReachable
[pscustomobject]@{
    dockerCli = $dockerCli
    daemonAvailable = $daemon
    containerRunning = $running
    receiverHealthy = $health
    selfMetricsReachable = $metricsReachable
    acceptedEvidence = $metrics -match 'otelcol_receiver_accepted'
    refusedEvidence = $metrics -match 'otelcol_receiver_refused'
    queueEvidence = $metrics -match 'otelcol_exporter_queue_(size|capacity)'
    exporterFailureEvidence = $metrics -match 'otelcol_exporter_(send_failed|enqueue_failed)'
    operational = $operational
    messages = $messages
} | ConvertTo-Json -Depth 4
if (-not $operational) { exit 2 }
