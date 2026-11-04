param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$ScratchDirectory)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testRoot = Join-Path ([IO.Path]::GetFullPath($ScratchDirectory)) ('updater-' + [Guid]::NewGuid().ToString('N'))
$targetDir = Join-Path $testRoot 'installed'
$sourceDir = Join-Path $testRoot 'state\updates\v1.0.1'
$stateDir = Join-Path $testRoot 'state'
[IO.Directory]::CreateDirectory($targetDir) | Out-Null
[IO.Directory]::CreateDirectory($sourceDir) | Out-Null
$target = Join-Path $targetDir 'AMZ.ScreenSize.exe'
$source = Join-Path $sourceDir 'AMZ.ScreenSize.exe'
$fixture = @'
using System; using System.IO; using System.Reflection; using System.Threading;
[assembly: AssemblyVersion("VERSION_TOKEN.0")]
internal static class Fixture {
    private static void Main(string[] args) {
        if (args.Length > 0 && args[0] == "--parent") { Thread.Sleep(3000); return; }
        int index = Array.IndexOf(args, "--state-dir");
        if (index >= 0) File.WriteAllText(Path.Combine(args[index + 1], "restarted-version.txt"), "VERSION_TOKEN");
    }
}
'@
foreach ($definition in @(@{Version='0.9.0';Path=$target},@{Version='1.0.1';Path=$source})) {
    $code = Join-Path ([IO.Path]::GetDirectoryName($definition.Path)) 'Fixture.cs'
    [IO.File]::WriteAllText($code, $fixture.Replace('VERSION_TOKEN',$definition.Version), (New-Object Text.UTF8Encoding $false))
    & $compiler /nologo /target:winexe "/out:$($definition.Path)" $code
    if ($LASTEXITCODE -ne 0) { throw 'Update fixture compilation failed.' }
}
$oldHash = (Get-FileHash -LiteralPath $target).Hash
$newHash = (Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
$parent = Start-Process -FilePath $target -ArgumentList '--parent' -WindowStyle Hidden -PassThru
$request = [ordered]@{ParentId=$parent.Id;Source=$source;Target=$target;Hash=$newHash;Version='1.0.1';StateDirectory=$stateDir;Background=$true}
function Invoke-UpdateFixture($requestValue) {
    $payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($requestValue | ConvertTo-Json -Compress)))
    $helper = Start-Process -FilePath $Executable -ArgumentList @('--apply-update',$payload) -WindowStyle Hidden -PassThru
    if (-not $helper.WaitForExit(15000)) { throw 'Update helper timeout.' }
    if ($helper.ExitCode -ne 0) { throw 'Update helper failed.' }
}
Invoke-UpdateFixture $request
if ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -ne $newHash) { throw 'Updated executable hash mismatch.' }
Write-Output 'PASS updater atomic executable replacement'
if ((Get-FileHash -LiteralPath ($target + '.previous')).Hash -ne $oldHash) { throw 'Update backup hash mismatch.' }
Write-Output 'PASS updater previous executable backup'
$marker = Join-Path $stateDir 'restarted-version.txt'
$deadline = [DateTime]::UtcNow.AddSeconds(5)
while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
if ((Get-Content -LiteralPath $marker -Raw) -ne '1.0.1') { throw 'Updated process did not restart.' }
Write-Output 'PASS updater new executable restart'
$request.ParentId = -1
$request.Hash = ('0' * 64)
Invoke-UpdateFixture $request
if ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -ne $newHash) { throw 'Invalid update altered the executable.' }
if ((Get-Content -LiteralPath (Join-Path $stateDir 'activity.log') -Raw) -notmatch 'UPDATE_INSTALL_FAILED') { throw 'Invalid update was not rejected.' }
if (-not (Test-Path -LiteralPath (Join-Path $stateDir 'updates\last-install-failure.txt'))) { throw 'Failed update cooldown was not persisted.' }
Write-Output 'PASS updater rejects invalid hash and preserves installed executable'
Write-Output 'PASS: 4 isolated updater integration checks; no user installation changed.'
