[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$')]
    [string] $PackageVersion
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packages = Join-Path $root 'artifacts/packages'
$manifestPath = Join-Path $packages "checksums.$PackageVersion.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Checksum manifest not found: $manifestPath"
}
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
foreach ($entry in $manifest) {
    $artifact = Join-Path $root $entry.file
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifact).Hash.ToLowerInvariant()
    if ($actual -ne $entry.sha256) {
        throw "Artifact hash mismatch: $($entry.file)"
    }
}

$evidence = Join-Path $root 'artifacts/supply-chain'
$null = New-Item -ItemType Directory -Force -Path $evidence
$sbomPath = Join-Path $evidence "sbom.$PackageVersion.spdx.json"
$provenancePath = Join-Path $evidence "provenance.$PackageVersion.json"
foreach ($path in @($sbomPath, $provenancePath)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite immutable evidence: $path" }
}

$packageEntries = $manifest | Where-Object { $_.file.EndsWith('.nupkg', [StringComparison]::Ordinal) }
$spdxPackages = foreach ($entry in $packageEntries) {
    $fileName = Split-Path -Leaf $entry.file
    $id = $fileName.Substring(0, $fileName.Length - ".$PackageVersion.nupkg".Length)
    [ordered]@{
        SPDXID = "SPDXRef-Package-$($id.Replace('.', '-'))"
        name = $id
        versionInfo = $PackageVersion
        downloadLocation = 'NOASSERTION'
        filesAnalyzed = $false
        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $entry.sha256 })
        externalRefs = @([ordered]@{
            referenceCategory = 'PACKAGE-MANAGER'
            referenceType = 'purl'
            referenceLocator = "pkg:nuget/$id@$PackageVersion"
        })
    }
}
$sbom = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "OnuObservability-$PackageVersion"
    documentNamespace = "https://onu.invalid/spdx/OnuObservability/$PackageVersion/$([guid]::NewGuid())"
    creationInfo = [ordered]@{
        created = [DateTimeOffset]::UtcNow.ToString('O')
        creators = @('Tool: OnuObservability-eng-New-SupplyChainEvidence')
    }
    packages = @($spdxPackages)
}
$sbom | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sbomPath -Encoding utf8NoBOM

$materials = foreach ($path in @('global.json', 'Directory.Packages.props', 'Directory.Build.props', 'Directory.Build.targets')) {
    $file = Join-Path $root $path
    [ordered]@{
        uri = $path
        digest = [ordered]@{ sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant() }
    }
}
$subjects = foreach ($entry in $manifest) {
    [ordered]@{
        name = $entry.file
        digest = [ordered]@{ sha256 = $entry.sha256 }
    }
}
$provenance = [ordered]@{
    '_type' = 'https://in-toto.io/Statement/v1'
    subject = @($subjects)
    predicateType = 'https://slsa.dev/provenance/v1'
    predicate = [ordered]@{
        buildDefinition = [ordered]@{
            buildType = 'https://onu.invalid/buildtypes/dotnet-pack/v1'
            externalParameters = [ordered]@{ packageVersion = $PackageVersion; configuration = 'Release' }
            internalParameters = [ordered]@{}
            resolvedDependencies = @($materials)
        }
        runDetails = [ordered]@{
            builder = [ordered]@{ id = 'OnuObservability/eng/Pack-Local.ps1' }
            metadata = [ordered]@{ invocationId = [guid]::NewGuid().ToString(); startedOn = [DateTimeOffset]::UtcNow.ToString('O') }
        }
    }
}
$provenance | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $provenancePath -Encoding utf8NoBOM

[ordered]@{
    sbom = $sbomPath.Substring($root.Length + 1).Replace('\', '/')
    provenance = $provenancePath.Substring($root.Length + 1).Replace('\', '/')
    subjects = @($subjects).Count
} | ConvertTo-Json
