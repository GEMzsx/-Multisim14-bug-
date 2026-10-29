$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $root 'dist\MultisimRetryLauncher.exe'
$probe = Join-Path $root 'dist\LauncherProbe.exe'
$testOutput = Join-Path $root 'dist\test-output'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$report = [System.Collections.Generic.List[object]]::new()

function Invoke-Launcher([string]$Name, [int]$Attempts = 3) {
    $outputFile = Join-Path $testOutput ('output-' + $Name + '.log')
    $errorFile = Join-Path $testOutput ('error-' + $Name + '.log')
    $arguments = @('--exe', ('"' + $probe + '"'), '--attempts', "$Attempts", '--timeout', '5', '--stable', '1', '--delay-ms', '300')
    $running = Start-Process -FilePath $launcher -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput $outputFile -RedirectStandardError $errorFile
    # Retain a native process handle so Windows PowerShell 5.1 can read the exit code.
    $null = $running.Handle
    if (-not $running.WaitForExit(30000)) {
        $running.Kill()
        throw "Launcher did not return within the test limit: $Name"
    }
    return $running.ExitCode
}

function Stop-Probes {
    Get-Process -Name 'LauncherProbe' -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Path -eq $probe) {
            Stop-Process -InputObject $_ -ErrorAction SilentlyContinue
            try { $null = $_.WaitForExit(5000) } catch [System.InvalidOperationException] { }
        }
    }
}

function Test-Scenario([string]$Mode, [int]$ExpectedCode, [int]$ExpectedCount, [int]$Attempts = 3) {
    Stop-Probes
    $counter = Join-Path $testOutput ('counter-' + $Mode + '.txt')
    [IO.File]::WriteAllText($counter, '0')
    $env:MULTISIM_PROBE_COUNTER = $counter
    $env:MULTISIM_PROBE_MODE = $Mode
    $code = Invoke-Launcher $Mode $Attempts
    $count = [int][IO.File]::ReadAllText($counter)
    $surviving = @(Get-Process -Name 'LauncherProbe' -ErrorAction SilentlyContinue).Count
    $expectedSurviving = if ($Mode -in @('ready','fail-twice','unknown','splash-ready','init-ready')) { 1 } else { 0 }
    $passed = $code -eq $ExpectedCode -and $count -eq $ExpectedCount -and $surviving -eq $expectedSurviving
    $report.Add([pscustomobject]@{ Scenario=$Mode; Passed=$passed; ExitCode=$code; Launches=$count; Remaining=$surviving })
    Stop-Probes
    if (-not $passed) { throw "Verification failed: $Mode; exit=$code; launches=$count; remaining=$surviving" }
}

try {
    Test-Scenario 'fail-twice' 0 3
    Test-Scenario 'always-fail' 2 3
    Test-Scenario 'chinese-fail' 2 2 2
    Test-Scenario 'unknown' 12 1
    Test-Scenario 'timeout' 2 2 2
    Test-Scenario 'ready' 0 1
    Test-Scenario 'splash-ready' 0 1
    Test-Scenario 'splash-fail' 2 2 2
    Test-Scenario 'init-ready' 0 1
    Test-Scenario 'init-fail' 2 2 2

    $counter = Join-Path $testOutput 'counter-existing.txt'
    [IO.File]::WriteAllText($counter, '0')
    $env:MULTISIM_PROBE_COUNTER = $counter
    $env:MULTISIM_PROBE_MODE = 'ready'
    $existing = Start-Process -FilePath $probe -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 1200
    $code = Invoke-Launcher 'protect-existing' 2
    $existing.Refresh()
    $passed = $code -eq 10 -and -not $existing.HasExited -and [IO.File]::ReadAllText($counter) -eq '1'
    $report.Add([pscustomobject]@{Scenario='protect-existing';Passed=$passed;ExitCode=$code;Launches=1;Remaining=1})
    if (-not $passed) { throw 'Existing instance was not protected.' }

    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testOutput 'verification-results.json') -Encoding utf8
    $report | Format-Table -AutoSize
}
finally {
    Stop-Probes
    Remove-Item Env:\MULTISIM_PROBE_COUNTER -ErrorAction SilentlyContinue
    Remove-Item Env:\MULTISIM_PROBE_MODE -ErrorAction SilentlyContinue
}
