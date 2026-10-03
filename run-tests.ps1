# run-tests.ps1 — local build + test runner for Windows (issue #13).
#
# The Windows equivalent of run-tests.sh / `make test`. Instead of Mono's
# mcs it uses MSBuild (bundled with Visual Studio / the .NET Framework
# developer tools) to build the two net47.2 projects, then runs the same
# unit suite and smoke tests that CI runs on Linux, so a Windows
# contributor gets the identical coverage locally:
#
#   .\run-tests.ps1
#
# Exit code 0 = all green, non-zero = a step failed.
#
# The .NET Framework 4.7.2 target is part of Windows; no NuGet restore is
# needed (the projects are dependency-free).

$ErrorActionPreference = 'Stop'

# Run from the repo root no matter where it is invoked from.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Step($msg) { Write-Host "`n==> $msg" }
function Fail($msg) { Write-Error "FAIL: $msg"; exit 1 }

# ---------------------------------------------------------------------------
# Locate MSBuild (Developer Command Prompt sets it; otherwise search VS).
# ---------------------------------------------------------------------------
function Find-MSBuild {
    if ($env:MSBUILD_EXE_PATH -and (Test-Path $env:MSBUILD_EXE_PATH)) { return $env:MSBUILD_EXE_PATH }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path) { return $path }
    }
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\MSBuild.exe",
        "${env:windir}\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
    )
    foreach ($c in $candidates) {
        $m = Get-Item $c -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($m) { return $m.FullName }
    }
    return $null
}

$msbuild = Find-MSBuild
if (-not $msbuild) { Fail "MSBuild not found — open a Visual Studio Developer Command Prompt or install the .NET Framework 4.7.2 build tools" }
Write-Host "Using MSBuild: $msbuild"

# ---------------------------------------------------------------------------
# 1. Build (Release, AnyCPU) — both projects via the solution.
# ---------------------------------------------------------------------------
Step "Build solution (msbuild -m -p:Configuration=Release)"
& $msbuild BasicGuessingGame.sln /nologo /m /v:minimal /p:Configuration=Release /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) { Fail "build" }

$gameBin = Join-Path $root "BasicGuessingGame\bin\Release\BasicGuessingGame.exe"
$testBin = Join-Path $root "BasicGuessingGame.Tests\bin\Release\GameEngineTests.exe"
if (-not (Test-Path $gameBin)) { Fail "game exe missing after build ($gameBin)" }
if (-not (Test-Path $testBin)) { Fail "test exe missing after build ($testBin)" }

# Helper: run the game with a scripted stdin and return its stdout.
function Invoke-Game([string[]]$lines, [string[]]$args_) {
    $input = ($lines -join "`r`n") + ($lines.Length -gt 0 ? "`r`n" : "")
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo.FileName = $gameBin
    $proc.StartInfo.Arguments = ($args_ -join ' ')
    $proc.StartInfo.UseShellExecute = $false
    $proc.StartInfo.RedirectStandardInput = $true
    $proc.StartInfo.RedirectStandardOutput = $true
    $proc.StartInfo.RedirectStandardError = $true
    $proc.Start() | Out-Null
    if ($input.Length -gt 0) { $proc.StandardInput.Write($input) }
    $proc.StandardInput.Close()
    $out = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    return $out
}

# ---------------------------------------------------------------------------
# 2. Unit tests (the suite prints "N passed, M failed" and exits non-zero on failure).
# ---------------------------------------------------------------------------
Step "Unit tests (deterministic via injected seed)"
& $testBin
if ($LASTEXITCODE -ne 0) { Fail "unit tests" }

# ---------------------------------------------------------------------------
# 3. Smoke tests (mirror CI)
# ---------------------------------------------------------------------------
function Assert-Output([string]$name, [string]$output, [string]$mustMatch, [switch]$mustNotMatch) {
    if ($mustNotMatch) {
        if ($output -match $mustMatch) { Fail "$name : unexpectedly matched '$mustMatch'" }
    } else {
        if ($output -notmatch $mustMatch) { Fail "$name : did not match '$mustMatch'`n$output" }
    }
}

Step "Smoke: invalid input is not silently treated as 0"
Assert-Output "invalid input" (Invoke-Game @('abc') @()) 'Invalid number'

Step "Smoke: EOF exits cleanly"
Assert-Output "EOF" (Invoke-Game @() @()) 'Input closed. Goodbye!'

Step "Smoke: guaranteed win (0-99, limit 200)"
$seq = 0..99 | ForEach-Object { [string]$_ }
Assert-Output "guaranteed win" (Invoke-Game $seq @(200)) 'Correct! You have guessed the number.'

Step "Smoke: high out-of-range guess is rejected, not 'too high'"
$out = Invoke-Game @('150') @(50)
Assert-Output "high oob message" $out 'Please enter a number between 0 and 99'
Assert-Output "high oob not 'too high'" $out 'too high' -MustNotMatch

Step "Smoke: low out-of-range guess is rejected, not 'too low'"
$out = Invoke-Game @('-5') @(50)
Assert-Output "low oob message" $out 'Please enter a number between 0 and 99'
Assert-Output "low oob not 'too low'" $out 'too low' -MustNotMatch

Step "Smoke: player can lose when the guess limit is hit"
$found = $false
for ($i = 1; $i -le 20; $i++) {
    $out = Invoke-Game @('0','1','2') @(3)
    if ($out -match 'ran out of guesses') {
        Write-Host "Observed the loss path on attempt $i"
        Assert-Output "loss reveals number" $out 'The number was'
        $found = $true
        break
    }
}
if (-not $found) { Fail "loss path not observed in 20 attempts" }

Step "Smoke: seeded round is deterministic (guaranteed loss, secret 24)"
$out = Invoke-Game @('0','1','2') @(3,1)
Assert-Output "seeded loss" $out 'ran out of guesses'
Assert-Output "seeded secret is 24" $out 'The number was 24'

Step "Smoke: seeded round is deterministic (guaranteed win on one guess)"
$out = Invoke-Game @('24') @(10,1)
Assert-Output "seeded win" $out 'Correct! You have guessed the number.'

Step "Smoke: seeded custom range (issue #12: [min] [max], seed 1, 10-50, secret 20)"
$out = Invoke-Game @('20') @(10,1,10,50)
Assert-Output "custom range banner" $out 'between 10-50'
Assert-Output "custom range win" $out 'Correct! You have guessed the number.'

Step "Smoke: invalid guess limit falls back to 10 and says so"
$out = Invoke-Game @() @('abc')
Assert-Output "invalid limit message" $out 'is not a valid guess limit'
Assert-Output "invalid limit fell back to 10" $out 'You have 10 guesses'

Step "Smoke: guess limit above the cap is clamped to 100 and says so"
$out = Invoke-Game @() @(999999999)
Assert-Output "clamp message" $out 'above the maximum of 100'
Assert-Output "limit clamped to 100" $out 'You have 100 guesses'

Step "Smoke: play-again prompt appears after a win"
$seq = 0..99 | ForEach-Object { [string]$_ }
Assert-Output "play again" (Invoke-Game $seq @(200)) 'Play again? \(y/n\)'

Step "ALL GREEN"
Write-Host "Local runner finished: all checks passed."
exit 0
