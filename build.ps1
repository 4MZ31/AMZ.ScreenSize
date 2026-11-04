[CmdletBinding()]
param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler not found. Use a 64-bit Windows machine with .NET Framework 4.8.' }
$root = $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION file.' }
$output = Join-Path $root 'bin\Release'
$intermediate = Join-Path $root 'obj'
[IO.Directory]::CreateDirectory($output) | Out-Null
[IO.Directory]::CreateDirectory($intermediate) | Out-Null
$versionSource = Join-Path $intermediate 'Version.g.cs'
$attributes = 'using System.Reflection; [assembly: AssemblyTitle("AMZ.ScreenSize")] [assembly: AssemblyProduct("AMZ.ScreenSize")] [assembly: AssemblyCompany("4MZ31")] [assembly: AssemblyDescription("Samsung Odyssey screen-size presets and Counter-Strike automation")] [assembly: AssemblyVersion("' + $version + '.0")] [assembly: AssemblyFileVersion("' + $version + '.0")]', ''
[IO.File]::WriteAllLines($versionSource, $attributes, (New-Object Text.UTF8Encoding $false))
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\AMZ.ScreenSize') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$exe = Join-Path $output 'AMZ.ScreenSize.exe'
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Runtime.Serialization.dll','/r:System.Xml.dll')
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/debug-','/codepage:65001',('/out:' + $exe),('/win32icon:' + (Join-Path $root 'assets\app.ico')),('/win32manifest:' + (Join-Path $root 'src\AMZ.ScreenSize\app.manifest'))) + $references + $sources + $versionSource
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output "Built AMZ.ScreenSize $version"
if ($Test) {
    $testExe = Join-Path $output 'AMZ.ScreenSize.Tests.exe'
    & $compiler /nologo /target:exe /codepage:65001 "/out:$testExe" "/r:$exe" @references (Join-Path $root 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Package) {
    $dist = Join-Path $root 'dist'
    [IO.Directory]::CreateDirectory($dist) | Out-Null
    $releaseExe = Join-Path $dist 'AMZ.ScreenSize.exe'
    Copy-Item -LiteralPath $exe -Destination $releaseExe -Force
    $zip = Join-Path $dist "AMZ.ScreenSize-v$version-win.zip"
    Compress-Archive -LiteralPath $releaseExe,(Join-Path $root 'README.md'),(Join-Path $root 'LICENSE'),(Join-Path $root 'CHANGELOG.md') -DestinationPath $zip -Force
    $hashes = @($releaseExe,$zip) | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) }
    [IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $hashes, (New-Object Text.UTF8Encoding $false))
    Write-Output "Packaged $zip"
}
