param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PublishDirectory).Path
$errors = [System.Collections.Generic.List[string]]::new()

foreach ($required in @(
    'SewerScan.UI.exe',
    'SewerScan.UI.dll',
    'tessdata\eng.traineddata',
    'tessdata\pol.traineddata'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required))) {
        $errors.Add("Brakuje wymaganego pliku: $required")
    }
}

$files = Get-ChildItem -LiteralPath $root -Recurse -File
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
    if ($file.Extension -in '.pdb', '.zip') { $errors.Add("Zabroniony plik: $relative") }
    if ($file.Name -match '(?i)\.Tests\.dll$') { $errors.Add("Zabronione testy: $relative") }
    if ($file.Extension -eq '.pdf') { $errors.Add("Zabroniony PDF referencyjny: $relative") }
}

foreach ($directory in Get-ChildItem -LiteralPath $root -Recurse -Directory) {
    if ($directory.Name -ieq 'x86') {
        $errors.Add("Zabroniony katalog x86: $([IO.Path]::GetRelativePath($root, $directory.FullName))")
    }
}

$exe = Join-Path $root 'SewerScan.UI.exe'
if (Test-Path -LiteralPath $exe) {
    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
    if ($actualVersion -ne "$ExpectedVersion.0") {
        $errors.Add("Nieprawidłowa wersja pliku: $actualVersion; oczekiwano $ExpectedVersion.0")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

Write-Host "Pakiet PrefabScan $ExpectedVersion przeszedł kontrolę zawartości."
