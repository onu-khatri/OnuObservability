[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$')]
    [string] $PackageVersion,
    [switch] $AllowUnapprovedMetadata
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packageDirectory = Join-Path $root 'artifacts/packages'
$expected = @(
    'OnuObservability',
    'OnuObservability.Hosting',
    'OnuObservability.AspNetCore',
    'OnuObservability.Mcp'
)
$allowedOnuDependencies = @{
    'OnuObservability' = @()
    'OnuObservability.Hosting' = @('OnuObservability')
    'OnuObservability.AspNetCore' = @('OnuObservability.Hosting')
    'OnuObservability.Mcp' = @('OnuObservability.Hosting')
}
$testDependencyPrefixes = @('Microsoft.NET.Test.Sdk', 'xunit', 'coverlet', 'Moq', 'NSubstitute')
$expectedProjectUrl = 'https://github.com/onu-khatri/OnuObservability'
$expectedRepositoryUrl = 'https://github.com/onu-khatri/OnuObservability.git'

function Throw-Gate([string] $gate, [string] $message) {
    throw "[$gate] $message"
}

function Test-ManifestPolicy($manifest, [string[]] $entries, [hashtable] $knownVulnerable) {
    $id = [string] $manifest.Id
    if ($manifest.ProjectUrl -ne $expectedProjectUrl -or
        $manifest.RepositoryType -ne 'git' -or
        $manifest.RepositoryUrl -ne $expectedRepositoryUrl) {
        Throw-Gate 'REPOSITORY_METADATA' "Package $id does not contain the approved repository metadata."
    }
    $allowedEntries = @(
        '_rels/.rels',
        "$id.nuspec",
        'README.md',
        'LICENSE',
        "lib/net10.0/$id.dll",
        '[Content_Types].xml'
    )
    foreach ($entry in $entries) {
        if ($entry.StartsWith('package/services/metadata/core-properties/', [StringComparison]::Ordinal)) { continue }
        if ($allowedEntries -notcontains $entry) { Throw-Gate 'PACKAGE_CONTENT' "Unexpected file '$entry' in $id." }
    }
    foreach ($dependency in $manifest.Dependencies) {
        if ($testDependencyPrefixes.Where({ $dependency.Id.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) {
            Throw-Gate 'TEST_DEPENDENCY' "Test dependency '$($dependency.Id)' leaked into $id."
        }
        if ($dependency.Id.StartsWith('OnuObservability', [StringComparison]::Ordinal) -and
            $allowedOnuDependencies[$id] -notcontains $dependency.Id) {
            Throw-Gate 'DEPENDENCY_GRAPH' "Forbidden dependency '$($dependency.Id)' in $id."
        }
        if ($knownVulnerable.ContainsKey("$($dependency.Id)/$($dependency.Version)")) {
            Throw-Gate 'VULNERABILITY' "Known vulnerable dependency '$($dependency.Id)' in $id."
        }
    }
    if (-not $AllowUnapprovedMetadata -and [string]::IsNullOrWhiteSpace($manifest.License)) {
        Throw-Gate 'LICENSE_POLICY' "Package $id has no approved SPDX license expression."
    }
}

function Assert-SeedFails([string] $expectedGate, [scriptblock] $action) {
    try { & $action; throw "Seeded fixture did not fail gate $expectedGate." }
    catch {
        if (-not $_.Exception.Message.StartsWith("[$expectedGate]", [StringComparison]::Ordinal)) { throw }
    }
}

$seed = [pscustomobject]@{
    Id = 'OnuObservability'
    License = 'MIT'
    ProjectUrl = $expectedProjectUrl
    RepositoryType = 'git'
    RepositoryUrl = $expectedRepositoryUrl
    Dependencies = @()
}
$seed.RepositoryUrl = 'https://example.invalid/wrong.git'
Assert-SeedFails 'REPOSITORY_METADATA' { Test-ManifestPolicy $seed @('_rels/.rels', 'OnuObservability.nuspec', 'README.md', 'LICENSE', 'lib/net10.0/OnuObservability.dll', '[Content_Types].xml') @{} }
$seed.RepositoryUrl = $expectedRepositoryUrl
Assert-SeedFails 'PACKAGE_CONTENT' { Test-ManifestPolicy $seed @('tools/unexpected.exe') @{} }
$seed.Dependencies = @([pscustomobject]@{ Id = 'OnuObservability.Mcp'; Version = '1.0.0' })
Assert-SeedFails 'DEPENDENCY_GRAPH' { Test-ManifestPolicy $seed @('_rels/.rels', 'OnuObservability.nuspec', 'README.md', 'LICENSE', 'lib/net10.0/OnuObservability.dll', '[Content_Types].xml') @{} }
$seed.Dependencies = @([pscustomobject]@{ Id = 'xunit'; Version = '2.9.3' })
Assert-SeedFails 'TEST_DEPENDENCY' { Test-ManifestPolicy $seed @('_rels/.rels', 'OnuObservability.nuspec', 'README.md', 'LICENSE', 'lib/net10.0/OnuObservability.dll', '[Content_Types].xml') @{} }
$seed.Dependencies = @([pscustomobject]@{ Id = 'Example.Vulnerable'; Version = '1.0.0' })
Assert-SeedFails 'VULNERABILITY' { Test-ManifestPolicy $seed @('_rels/.rels', 'OnuObservability.nuspec', 'README.md', 'LICENSE', 'lib/net10.0/OnuObservability.dll', '[Content_Types].xml') @{ 'Example.Vulnerable/1.0.0' = $true } }

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($id in $expected) {
    $path = Join-Path $packageDirectory "$id.$PackageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $path)) { Throw-Gate 'PACKAGE_SET' "Missing package $path." }
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        $nuspecEntry = $archive.Entries | Where-Object FullName -eq "$id.nuspec" | Select-Object -First 1
        if ($null -eq $nuspecEntry) { Throw-Gate 'PACKAGE_CONTENT' "Missing nuspec for $id." }
        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata
        $dependencies = @($metadata.dependencies.group.dependency) | Where-Object { $null -ne $_ } | ForEach-Object {
            [pscustomobject]@{ Id = [string] $_.id; Version = [string] $_.version }
        }
        $manifest = [pscustomobject]@{
            Id = [string] $metadata.id
            License = [string] $metadata.license
            ProjectUrl = [string] $metadata.projectUrl
            RepositoryType = [string] $metadata.repository.type
            RepositoryUrl = [string] $metadata.repository.url
            Dependencies = @($dependencies)
        }
        if ($manifest.Id -ne $id) { Throw-Gate 'PACKAGE_ID' "Expected $id but found $($manifest.Id)." }
        Test-ManifestPolicy $manifest $entries @{}
        foreach ($textEntry in $archive.Entries | Where-Object { $_.FullName -in @("$id.nuspec", 'README.md', 'LICENSE') }) {
            $reader = [IO.StreamReader]::new($textEntry.Open())
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($text -match '(?i)(api[_-]?key|password|authorization)\s*[:=]\s*[^\s<]+') {
                Throw-Gate 'SECRET_SCAN' "Potential credential in $id/$($textEntry.FullName)."
            }
        }
    }
    finally { $archive.Dispose() }
}

[ordered]@{
    packageVersion = $PackageVersion
    packageCount = $expected.Count
    seededPolicyGates = @('REPOSITORY_METADATA', 'PACKAGE_CONTENT', 'DEPENDENCY_GRAPH', 'TEST_DEPENDENCY', 'VULNERABILITY')
    metadataPolicy = $(if ($AllowUnapprovedMetadata) { 'unverified-by-explicit-local-override' } else { 'passed' })
} | ConvertTo-Json
