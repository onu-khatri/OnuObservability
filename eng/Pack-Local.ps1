[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$')]
    [string] $PackageVersion,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts/packages'
$projects = @(
    'src/OnuObservability/OnuObservability.csproj',
    'src/OnuObservability.Hosting/OnuObservability.Hosting.csproj',
    'src/OnuObservability.AspNetCore/OnuObservability.AspNetCore.csproj',
    'src/OnuObservability.Mcp/OnuObservability.Mcp.csproj'
)
$packageIds = @(
    'OnuObservability',
    'OnuObservability.Hosting',
    'OnuObservability.AspNetCore',
    'OnuObservability.Mcp'
)
$normalizedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

function Normalize-PackageArchive([string] $Path) {
    Add-Type -AssemblyName System.IO.Compression
    $directory = Split-Path -Parent $Path
    $temporaryPath = Join-Path $directory ".normalize-$([Guid]::NewGuid().ToString('N')).tmp"
    $source = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $destinationStream = [IO.File]::Open(
            $temporaryPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
        try {
            $destination = [IO.Compression.ZipArchive]::new(
                $destinationStream,
                [IO.Compression.ZipArchiveMode]::Create,
                $false)
            try {
                foreach ($entry in @($source.Entries | Sort-Object FullName)) {
                    $normalized = $destination.CreateEntry(
                        $entry.FullName,
                        [IO.Compression.CompressionLevel]::Optimal)
                    $normalized.LastWriteTime = $normalizedTimestamp
                    $normalized.ExternalAttributes = $entry.ExternalAttributes
                    $inputStream = $entry.Open()
                    try {
                        $outputStream = $normalized.Open()
                        try { $inputStream.CopyTo($outputStream) }
                        finally { $outputStream.Dispose() }
                    }
                    finally { $inputStream.Dispose() }
                }
            }
            finally { $destination.Dispose() }
        }
        finally { $destinationStream.Dispose() }
    }
    catch {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        throw
    }
    finally { $source.Dispose() }

    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach ($packageId in $packageIds) {
    foreach ($extension in @('nupkg', 'snupkg')) {
        $target = Join-Path $output "$packageId.$PackageVersion.$extension"
        if (Test-Path -LiteralPath $target) {
            throw "Refusing to overwrite immutable local artifact: $target"
        }
    }
}

foreach ($project in $projects) {
    $packArguments = @(
        'pack',
        (Join-Path $root $project),
        '-c', 'Release',
        '--no-restore',
        '--nologo',
        "-p:PackageVersion=$PackageVersion",
        "-p:PackageOutputPath=$output"
    )
    if ($NoBuild) {
        $packArguments += '--no-build'
    }
    & dotnet @packArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing failed for $project."
    }
}

foreach ($packageId in $packageIds) {
    foreach ($extension in @('nupkg', 'snupkg')) {
        Normalize-PackageArchive (Join-Path $output "$packageId.$PackageVersion.$extension")
    }
}

$hashes = foreach ($packageId in $packageIds) {
    foreach ($extension in @('nupkg', 'snupkg')) {
        $target = Join-Path $output "$packageId.$PackageVersion.$extension"
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $target
        [ordered]@{
            file = $hash.Path.Substring($root.Length + 1).Replace('\', '/')
            sha256 = $hash.Hash.ToLowerInvariant()
        }
    }
}
$manifest = Join-Path $output "checksums.$PackageVersion.json"
$hashes | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
Write-Host "Packed $($packageIds.Count) packages and symbols. Manifest: $manifest"
