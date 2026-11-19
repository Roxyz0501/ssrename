param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$ACTPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dist = Join-Path $projectRoot 'dist'
$release = Join-Path $projectRoot 'release'

$buildProjects = @(
    (Join-Path $projectRoot 'src/SSRename/SSRename.csproj'),
    (Join-Path $projectRoot 'tools/SSRename.Updater/SSRename.Updater.csproj'),
    (Join-Path $projectRoot 'tests/SSRename.Tests/SSRename.Tests.csproj')
)
foreach ($buildProject in $buildProjects) {
    $buildArguments = @('msbuild', $buildProject, '-t:Rebuild', '-p:Configuration=Release', '-p:UseSharedCompilation=false', '-v:minimal')
    if (-not [string]::IsNullOrWhiteSpace($ACTPath) -and $buildProject.EndsWith('src/SSRename/SSRename.csproj', [StringComparison]::OrdinalIgnoreCase)) {
        $buildArguments += "-p:ACTPath=$ACTPath"
    }
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Release build failed for $buildProject." }
}
$dll = Join-Path $dist 'SSRename.dll'
$assembly = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
$actualVersion = '{0}.{1}.{2}' -f $assembly.Version.Major, $assembly.Version.Minor, $assembly.Version.Build
if ($actualVersion -ne $Version) { throw "Assembly version $actualVersion does not match requested release $Version." }
$updater = Join-Path $dist 'SSRename.Updater.exe'
if (-not (Test-Path -LiteralPath $updater)) { throw 'SSRename.Updater.exe is missing from the release build.' }
$updaterAssembly = [System.Reflection.AssemblyName]::GetAssemblyName($updater)
$updaterVersion = '{0}.{1}.{2}' -f $updaterAssembly.Version.Major, $updaterAssembly.Version.Minor, $updaterAssembly.Version.Build
if ($updaterAssembly.Name -ne 'SSRename.Updater' -or $updaterVersion -ne $Version) { throw "Updater identity/version $($updaterAssembly.Name) $updaterVersion does not match SSRename $Version." }

New-Item -ItemType Directory -Force -Path $release | Out-Null
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('SSRename-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    foreach ($name in @('SSRename.dll','SSRename.pdb','SSRename.Updater.exe','README.md','THIRD_PARTY_DATA.md')) {
        $source = Join-Path $dist $name
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $staging $name) }
    }
    $assetName = "SSRename-$Version.zip"
    $assetPath = Join-Path $release $assetName
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $assetPath -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $assetPath).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ASCII -Value "$hash  $assetName"
    & (Join-Path $projectRoot 'tests/SSRename.Tests/bin/Release/SSRename.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
    Write-Host "Created $assetPath"
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
