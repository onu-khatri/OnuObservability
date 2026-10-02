[CmdletBinding()]
param(
    [ValidateSet('Local', 'Production', 'Persistent')] [string] $Mode = 'Local',
    [ValidatePattern('^[a-z0-9][a-z0-9-]*$')] [string] $ProjectName = 'onu-observability'
)

$ErrorActionPreference = 'Stop'
$assets = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\observability'))
$compose = Join-Path $assets 'docker-compose.yml'
$persistent = Join-Path $assets 'docker-compose.persistent.yml'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker CLI is required; this script does not install it.' }
$env:OTEL_COLLECTOR_CONFIG = switch ($Mode) {
    Local { 'collector.local.yaml' }
    Production { 'collector.production.yaml' }
    Persistent { 'collector.persistent.yaml' }
}
$arguments = @('compose', '--project-name', $ProjectName, '--project-directory', $assets, '--file', $compose)
if ($Mode -eq 'Persistent') { $arguments += @('--file', $persistent) }
$arguments += 'down'
& docker @arguments
if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed to stop $ProjectName." }
if ($Mode -eq 'Persistent') {
    Write-Warning 'The persistent queue volume was retained. Remove it only under an approved secure-deletion procedure.'
}
