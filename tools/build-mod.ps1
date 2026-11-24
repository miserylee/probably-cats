[CmdletBinding()]
param(
    [string] $GameDirectory = $env:PROBABLY_STOLEN_DIRECTORY,
    [string] $Dotnet = 'dotnet',
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'src/DeepSpaceCattery'
$project = Join-Path $source 'DeepSpaceCattery.csproj'
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'dist/DeepSpaceCattery'
}

$buildArgs = @('build', $project, '--configuration', 'Release', '--nologo')
if ($GameDirectory) {
    $buildArgs += "-p:GameDirectory=$GameDirectory"
}
& $Dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "C# build failed (exit $LASTEXITCODE)."
}

New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null
$assembly = Join-Path $source 'bin/Release/netstandard2.1/DeepSpaceCattery.dll'
Copy-Item -LiteralPath $assembly, (Join-Path $source 'manifest.xml') -Destination $OutputDirectory -Force
$textures = Join-Path $source 'Textures'
if (Test-Path -LiteralPath $textures -PathType Container) {
    Copy-Item -LiteralPath $textures -Destination $OutputDirectory -Recurse -Force
}
Write-Output (Resolve-Path -LiteralPath $OutputDirectory).Path
