param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$ACTPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $projectRoot 'SSRename.sln'
$dist = Join-Path $projectRoot 'dist'
$release = Join-Path $projectRoot 'release'

$buildArguments = @('msbuild', $solution, '-t:Rebuild', '-p:Configuration=Release', '-v:minimal')
if (-not [string]::IsNullOrWhiteSpace($ACTPath)) {
    $buildArguments += "-p:ACTPath=$ACTPath"
}
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$dll = Join-Path $dist 'SSRename.dll'
$assembly = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
$actualVersion = '{0}.{1}.{2}' -f $assembly.Version.Major, $assembly.Version.Minor, $assembly.Version.Build
if ($actualVersion -ne $Version) { throw "Assembly version $actualVersion does not match requested release $Version." }

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
    Write-Host "Created $assetPath"
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
