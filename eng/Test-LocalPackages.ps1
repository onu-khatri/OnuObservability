[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$')]
    [string] $PackageVersion
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = Join-Path $PSScriptRoot 'NuGet.local-consumer.config'
$artifacts = Join-Path $root 'artifacts'
$samples = [ordered]@{
    web = 'samples/OnuObservability.Sample.Web/OnuObservability.Sample.Web.csproj'
    worker = 'samples/OnuObservability.Sample.Worker/OnuObservability.Sample.Worker.csproj'
    mcp = 'samples/OnuObservability.Sample.Mcp/OnuObservability.Sample.Mcp.csproj'
}
$expected = @{
    web = 'OnuObservability.AspNetCore'
    worker = 'OnuObservability.Hosting'
    mcp = 'OnuObservability.Mcp'
}

foreach ($name in $samples.Keys) {
    $project = Join-Path $root $samples[$name]
    $bin = Join-Path $artifacts "consumer-bin/$name/"
    $lock = Join-Path $artifacts "consumer-locks/$name.packages.lock.json"
    & dotnet restore $project --configfile $config --force-evaluate `
        -p:UseLocalPackages=true `
        -p:OnuObservabilityPackageVersion=$PackageVersion `
        -p:RestoreLockedMode=false `
        -p:NuGetLockFilePath=$lock
    if ($LASTEXITCODE -ne 0) {
        throw "Package restore failed for $name."
    }

    & dotnet build $project -c Release --no-restore --nologo `
        -p:UseLocalPackages=true `
        -p:OnuObservabilityPackageVersion=$PackageVersion `
        -p:NuGetLockFilePath=$lock `
        -p:OutputPath=$bin
    if ($LASTEXITCODE -ne 0) {
        throw "Package build failed for $name."
    }

    $projectDirectory = Split-Path -Parent $project
    $assetsPath = Join-Path $projectDirectory 'obj/project.assets.json'
    $assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json -AsHashtable
    $adapter = "$($expected[$name])/$PackageVersion"
    if (-not $assets.libraries.ContainsKey($adapter) -or $assets.libraries[$adapter].type -ne 'package') {
        throw "$name did not resolve $adapter as a package."
    }
    if ($assets.project.frameworks.'net10.0'.projectReferences.Count -ne 0) {
        throw "$name retained a project reference during isolated package validation."
    }

    $forbidden = switch ($name) {
        web { 'OnuObservability.Mcp/' }
        worker { 'OnuObservability.AspNetCore/', 'OnuObservability.Mcp/' }
        mcp { 'OnuObservability.AspNetCore/' }
    }
    foreach ($prefix in $forbidden) {
        if ($assets.libraries.Keys.Where({ $_.StartsWith($prefix, [StringComparison]::Ordinal) }).Count -ne 0) {
            throw "$name restored forbidden adapter dependency $prefix."
        }
    }
}

$mcpDll = Join-Path $artifacts 'consumer-bin/mcp/OnuObservability.Sample.Mcp.dll'
$webDll = Join-Path $artifacts 'consumer-bin/web/OnuObservability.Sample.Web.dll'
$workerDll = Join-Path $artifacts 'consumer-bin/worker/OnuObservability.Sample.Worker.dll'
$previousMcpSample = $env:ONU_MCP_SAMPLE_DLL
$previousWebSample = $env:ONU_WEB_SAMPLE_DLL
$previousWorkerSample = $env:ONU_WORKER_SAMPLE_DLL
try {
    $env:ONU_MCP_SAMPLE_DLL = $mcpDll
    $env:ONU_WEB_SAMPLE_DLL = $webDll
    $env:ONU_WORKER_SAMPLE_DLL = $workerDll
    & dotnet test (Join-Path $root 'tests/OnuObservability.Integration.Tests/OnuObservability.Integration.Tests.csproj') `
        -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'The exact-package Web, Worker, or MCP sample integration test failed.'
    }
}
finally {
    $env:ONU_MCP_SAMPLE_DLL = $previousMcpSample
    $env:ONU_WEB_SAMPLE_DLL = $previousWebSample
    $env:ONU_WORKER_SAMPLE_DLL = $previousWorkerSample
}

$checksumManifest = Join-Path $artifacts "packages/checksums.$PackageVersion.json"
if (-not (Test-Path -LiteralPath $checksumManifest)) {
    throw "The checksum manifest for $PackageVersion is missing."
}
$validated = Get-Content -Raw -LiteralPath $checksumManifest | ConvertFrom-Json
foreach ($entry in $validated) {
    $artifact = Join-Path $root $entry.file
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifact).Hash.ToLowerInvariant()
    if ($actual -ne $entry.sha256) { throw "Validated artifact hash mismatch: $($entry.file)" }
}
$receipt = Join-Path $artifacts "consumer-locks/validated-packages.$PackageVersion.json"
[ordered]@{
    packageVersion = $PackageVersion
    validatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    artifacts = $validated
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receipt -Encoding utf8NoBOM

Write-Host "Validated exact-package Web, Worker, and MCP samples for package version $PackageVersion."
