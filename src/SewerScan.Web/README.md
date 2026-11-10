# PrefabScan Web — lokalny prototyp

Ta gałąź dodaje **nowy, samodzielny prototyp interfejsu Web**, korzystający z istniejącego silnika `SewerScan.Application` i `SewerScan.Infrastructure`. Nie zastępuje ani nie modyfikuje istniejącej kopii projektu Web przechowywanej wyłącznie na komputerze użytkownika.

## Funkcje

- Wgrywanie maksymalnie 10 plików PDF (25 MB na plik, 80 MB łącznie).
- Wspólna analiza PZT i profili oraz łączenie informacji o obiektach z wielu rysunków.
- Podgląd studni, wpustów i przewodów; wyszukiwanie, wskazanie kompletności.
- Eksport zestawienia do trzech arkuszy XLSX przez istniejący `XlsxProjectExporter`.
- Bez bazy danych: pliki wejściowe są usuwane po zakończeniu analiz, wyniki przechowywane w RAM maksymalnie 30 minut.
- Serwer jest **celowo dostępny tylko lokalnie**: `http://127.0.0.1:5000`.

## Uruchomienie na Windows

Wymagany jest .NET SDK 10. Aby uruchomić prototyp w wersji źródłowej:

```powershell
dotnet run --project src/SewerScan.Web/SewerScan.Web.csproj
```

Następnie otwórz w przeglądarce `http://127.0.0.1:5000`. Zestaw testowy powinien zawierać np. PZT i odpowiedni profil podłużny. Modele `pol.traineddata` i `eng.traineddata` są wymagane w katalogu `tessdata` przy pliku wykonywalnym podczas OCR, ale nie są konieczne dla PDF z poprawną warstwą tekstową. Workflow GitHub Actions publikuje pakiet Windows z modelami pobranymi i zweryfikowanymi skryptem projektu.

## Ograniczenia

- Przeglądarka i serwer muszą działać na tym samym komputerze.
- Brak kont użytkowników, bazy danych i historii analiz; nie udostępniać na publicznym adresie IP.
- W tej wersji nie ma edytora wyników ani trwałej listy inwestycji.
- Raport automatyczny nie stanowi zatwierdzonego zestawienia projektowego: pola niepewne należy potwierdzić w dokumentacji.
- Prototyp działa niezależnie od niezatwierdzonej gałęzi poprawek PDF w PR #9.
- Nie należy podmieniać lokalnego folderu `PrefabSkan-fix-inlet` bez wykonania kopii zapasowej i porównania zmian.
