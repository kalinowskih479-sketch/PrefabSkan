# PrefabScan 4.2.7

PrefabScan analizuje dokumentację kanalizacyjną PDF i eksportuje zestawienie elementów do XLSX. Wydanie 4.2.7 stabilizuje dystrybucję i OCR bez zmiany reguł biznesowych parsera.

## Wymagania i kompilacja

- Windows x64;
- .NET SDK 10;
- PowerShell 7 do skryptów wydania.

```powershell
dotnet restore PrefabScan.sln --locked-mode -r win-x64
dotnet test PrefabScan.sln -c Release --no-restore
dotnet build PrefabScan.sln -c Release --no-restore
```

## OCR offline

Program nigdy nie pobiera modeli podczas działania. Pakiet wydania zawiera zweryfikowane pliki `tessdata/eng.traineddata` i `tessdata/pol.traineddata`.

Jeżeli pojawi się komunikat `Brakuje modelu OCR: ...`, zamknij program i odtwórz oba modele w katalogu `tessdata` obok `SewerScan.UI.exe`:

```powershell
./tools/Get-OcrModels.ps1 -Destination ./tessdata
```

Skrypt pobiera modele z przypiętego commita oficjalnego repozytorium Tesseract i odrzuca pliki o niewłaściwej sumie SHA-256.

## Cache

Wyniki OCR są zapisywane w `%LOCALAPPDATA%\PrefabScan\ocr-cache`. Nazwa każdego wpisu zawiera wersję schematu i algorytmu. Usunięcie tego katalogu jest bezpieczne — wynik zostanie odtworzony przy następnej analizie.

## Uruchomienie deweloperskie

Otwórz `PrefabScan.sln`, ustaw `SewerScan.UI` jako projekt startowy, skompiluj rozwiązanie i uruchom bez debugowania. W aplikacji wybierz komplet dokumentów PZT i profili, a następnie uruchom analizę zestawu.

PrefabScan pozostawia niepewne dane puste; nie zgaduje typu studni, DN ani zwieńczenia bez wystarczającego lokalnego dowodu w dokumentacji.
