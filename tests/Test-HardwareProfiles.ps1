param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$ScratchDirectory)
$ErrorActionPreference = 'Stop'
$Executable = [IO.Path]::GetFullPath($Executable)
$root = Join-Path ([IO.Path]::GetFullPath($ScratchDirectory)) ('hardware-profiles-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$diagnostic = Join-Path $root 'diagnostic.json'
function Read-Mode {
    $probe = Start-Process -FilePath $Executable -ArgumentList @('--diagnostic',('"' + $diagnostic + '"')) -PassThru -WindowStyle Hidden
    if (-not $probe.WaitForExit(5000)) { throw 'Monitor diagnostic timeout.' }
    $value = Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json
    if ($value.Error) { throw $value.Error }
    return [int]$value.Mode
}
function Wait-Mode([int]$Expected) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        try { if ((Read-Mode) -eq $Expected) { return } } catch { }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Monitor did not reach expected mode $Expected"
}
$original = Read-Mode
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixture = Join-Path $root 'Game.cs'
[IO.File]::WriteAllText($fixture,'internal static class Game { private static void Main() { System.Threading.Thread.Sleep(120000); } }')
$alphaExe = Join-Path $root 'AMZGameAlpha.exe'
$betaExe = Join-Path $root 'AMZGameBeta.exe'
foreach ($target in @($alphaExe,$betaExe)) {
    & $compiler /nologo /target:winexe "/out:$target" $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Game fixture compilation failed.' }
}
$settings = [ordered]@{
    Enabled=$true; AutoUpdate=$false; TargetMode=7; SelectedMode=7; RestorePending=$false; OriginalMode=$original
    Profiles=@(
        [ordered]@{Id='fixture-alpha';Name='Test Alpha';Processes=@('AMZGameAlpha');TargetMode=2;Enabled=$true},
        [ordered]@{Id='fixture-beta';Name='Test Beta';Processes=@('AMZGameBeta');TargetMode=5;Enabled=$true}
    )
}
[IO.File]::WriteAllText((Join-Path $root 'settings.json'),($settings | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding $false))
$app=$null; $alpha=$null; $beta=$null
try {
    $app = Start-Process -FilePath $Executable -ArgumentList @('--state-dir',('"' + $root + '"'),'--background') -WindowStyle Hidden -PassThru
    $alpha = Start-Process -FilePath $alphaExe -WindowStyle Hidden -PassThru
    Wait-Mode 2
    Write-Output 'PASS live Alpha launch selects 17 inches'
    $beta = Start-Process -FilePath $betaExe -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3
    if ((Read-Mode) -ne 2) { throw 'Second game stole priority.' }
    Write-Output 'PASS live Alpha retains priority while Beta runs'
    Stop-Process -Id $alpha.Id; $alpha.WaitForExit(); $alpha=$null
    Wait-Mode 5
    Write-Output 'PASS live Alpha exit hands control to Beta at 21.5 inches'
    Stop-Process -Id $beta.Id; $beta.WaitForExit(); $beta=$null
    Wait-Mode $original
    Write-Output "PASS live final game exit restores original mode $original"
} finally {
    foreach ($process in @($alpha,$beta)) { if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id } }
    if ($app -and -not $app.HasExited) {
        $stop = Start-Process -FilePath $Executable -ArgumentList '--quit' -WindowStyle Hidden -PassThru
        if (-not $stop.WaitForExit(5000) -or -not $app.WaitForExit(15000)) { throw 'Test application did not shut down cleanly.' }
    }
}
if ((Read-Mode) -ne $original) { throw 'Final monitor restore verification failed.' }
Write-Output "PASS hardware process cycle complete; original mode $original verified."
