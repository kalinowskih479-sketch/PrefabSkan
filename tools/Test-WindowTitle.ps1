param(
    [Parameter(Mandatory = $true)]
    [string]$ApplicationPath,

    [string]$ExpectedTitle = 'PrefabScan 4.2.7 — Geometry Table Resolver'
)

$resolvedApplication = (Resolve-Path -LiteralPath $ApplicationPath).Path
$process = Start-Process -FilePath $resolvedApplication -WorkingDirectory (Split-Path -Parent $resolvedApplication) -PassThru

try {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    } while (-not $process.HasExited -and
             [string]::IsNullOrWhiteSpace($process.MainWindowTitle) -and
             (Get-Date) -lt $deadline)

    if ($process.HasExited) {
        throw "PrefabScan zakończył działanie przed otwarciem okna (kod $($process.ExitCode))."
    }

    if ($process.MainWindowTitle -ne $ExpectedTitle) {
        throw "Nieprawidłowy tytuł okna. Oczekiwano '$ExpectedTitle', otrzymano '$($process.MainWindowTitle)'."
    }

    Write-Output "OK: $($process.MainWindowTitle)"
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    }
}
