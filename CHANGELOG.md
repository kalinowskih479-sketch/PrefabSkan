# Changelog

## 4.2.8 — 2026-09-11

- poprawiono rozpoznawanie studni na obróconych profilach OCR, gdy oznaczenia węzłów nie leżą na jednej linii;
- identyfikatory z tabliczki rysunkowej są odrzucane, jeśli nie mają danych inżynierskich w swojej kolumnie;
- zwykła analiza PDF pokazuje neutralne podsumowanie zamiast wyniku benchmarku Batorego;
- dodano test regresyjny oparty na układzie profilu Zawady.

## 4.2.7 — 2026-09-03

- ujednolicono wersję produktu, zestawów, diagnostyki i historii benchmarków;
- usunięto pobieranie modeli OCR podczas działania programu;
- przypięto modele polski i angielski do niezmiennego commita oraz sum SHA-256;
- rozdzielono wersję schematu cache OCR od wersji produktu;
- podniesiono EF Core do 8.0.30 i SQLitePCLRaw do 2.1.12 oraz dodano centralne, blokowane zależności;
- zastąpiono modyfikujące repozytorium workflow bezpiecznym pipeline'em tylko do odczytu;
- dodano automatyczną kontrolę czystości pakietu Windows x64;
- usunięto z aktywnej gałęzi duplikaty źródeł, wyniki kompilacji i stare archiwum.

Reguły parsera i semantyka eksportu pozostają bez zmian względem 4.2.6.
