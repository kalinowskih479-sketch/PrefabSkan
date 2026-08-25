# PrefabScan 2.1

Wersja 2.1 jest większym pakietem naprawczym po testach OCR 2.0/2.0.1.

## Najważniejsze zmiany

- normalizacja oznaczeń OCR, np. `S08 -> S8`;
- odrzucanie niepotwierdzonych wysokich oznaczeń OCR typowych dla szumu (`S95` itp.);
- zachowanie poprawionego odczytu rzędnych 2- i 3-cyfrowych oraz obliczania wysokości;
- odzyskiwanie DN studni z rozdzielonych przez OCR tokenów `Ø + 1200` i `DN + 1200` na profilach/detalach;
- przestrzenne łączenie materiału i średnicy rury, np. `PVC` + `200`, `PP` + `300`;
- normalizacja typowych błędów OCR `PYC/P¥C -> PVC`;
- przejścia szczelne są przypisywane lokalnie do studni, a nie globalnie do całej strony;
- nowa wersja cache OCR `2.1.0`, aby nie używać starszych wyników;
- dodatkowe testy regresyjne dla oznaczeń OCR, rzędnych i przejść.

## Uruchomienie

1. Otwórz `PrefabScan.sln`.
2. Ustaw `SewerScan.UI` jako projekt startowy.
3. `Ctrl+Shift+B`.
4. Przy 0 błędów: `Ctrl+F5`.
5. Wybierz PZT + profile i uruchom analizę zestawu.

## Ważne

PrefabScan nie zgaduje typu studni, DN ani zwieńczenia, jeśli dokumentacja nie daje wystarczającego lokalnego dowodu. Niepewne dane pozostają puste zamiast być przedstawione jako pewne.
