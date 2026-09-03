param([Parameter(Mandatory)] [string] $Destination)

$ErrorActionPreference = 'Stop'
$commit = '65727574dfcd264acbb0c3e07860e4e9e9b22185'
$models = @(
    @{ Name = 'eng.traineddata'; Hash = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2' },
    @{ Name = 'pol.traineddata'; Hash = 'C4476CDBC0E33D898D32345122B7BE1CBF85ACE15F920F06C7714756E1EF79B2' }
)

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($model in $models) {
    $target = Join-Path $Destination $model.Name
    $temporary = "$target.download"
    try {
        $url = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$commit/$($model.Name)"
        Invoke-WebRequest -Uri $url -OutFile $temporary
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporary).Hash
        if ($actual -ne $model.Hash) {
            throw "Nieprawidłowa suma SHA-256 modelu $($model.Name): $actual"
        }
        Move-Item -LiteralPath $temporary -Destination $target -Force
    }
    finally {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}
