# Wydawanie PrefabScan

Workflow `.github/workflows/build-427.yml` wykonuje locked restore, testy Release, pobranie i weryfikację modeli OCR, samodzielną publikację `win-x64`, kontrolę zawartości, utworzenie ZIP-a i sumy SHA-256. Uprawnienia workflow są ograniczone do `contents: read`; pipeline nie wykonuje commitów ani pushów.

Pakiet musi zawierać aplikację oraz dwa modele w `tessdata`. Nie może zawierać PDB, zagnieżdżonych ZIP-ów, bibliotek x86, zestawów testowych ani referencyjnych PDF-ów. Warunki sprawdza `tools/Test-ReleaseContents.ps1`.

## Weryfikacja pobranego artefaktu

```powershell
$expected = (Get-Content PrefabScan_4.2.7_Windows_x64.zip.sha256).Split()[0]
$actual = (Get-FileHash -Algorithm SHA256 PrefabScan_4.2.7_Windows_x64.zip).Hash
if ($actual -ne $expected) { throw 'Suma SHA-256 jest nieprawidłowa.' }
```

## Podpis cyfrowy

Authenticode jest osobnym etapem właścicielskim po kontroli zawartości, a przed utworzeniem sumy końcowej. Wymaga certyfikatu dostarczonego przez właściciela i sekretu GitHub. Repozytorium nie zawiera certyfikatu ani klucza prywatnego; bez nich artefakt pozostaje jawnie niepodpisany.
