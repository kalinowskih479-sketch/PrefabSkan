# Headless Batorego Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the real Toruń/Batorego PDFs through PrefabScan OCR, parsing, merge, and benchmark automatically in GitHub Actions without WPF or manual user interaction.

**Architecture:** Add a small `net10.0` console runner that calls the same `PdfTextExtractor`, `SewerProjectParser`, `PdfAnalyzer`, `ProjectMerger`, and `BatoregoBenchmark` used by the desktop application. Store the three real reference PDFs under `Reference/Batorego`. A Windows GitHub Actions job runs unit tests, runs the real end-to-end benchmark, uploads diagnostics/score artifacts, and blocks regressions below the current baseline.

**Tech Stack:** .NET 10, C#, PdfPig, Docnet/PDFium, Tesseract, ImageMagick, GitHub Actions Windows runner.

**Spec:** User-approved unattended development loop in the PrefabScan conversation.

## Global Constraints

- Use the real Batorego PDFs, not synthetic replacements.
- Benchmark code may evaluate output but must not fill parser fields.
- Initial non-regression gate: expected IDs >= 12/15 and false IDs <= 8.
- Always upload full diagnostics even when the benchmark gate fails.
- Keep the WPF application independent from the benchmark runner.

---

### Task 1: Headless runner

**Files:**
- Create: `src/SewerScan.BenchmarkRunner/SewerScan.BenchmarkRunner.csproj`
- Create: `src/SewerScan.BenchmarkRunner/Program.cs`

- [ ] Build a console entry point using the production extractor/parser/merger.
- [ ] Save `Batorego_LATEST.txt`, `Batorego_SCORE.json`, and `Batorego_SUMMARY.md`.
- [ ] Return a non-zero exit code only for runtime failure or regression below the current baseline.

### Task 2: Real reference inputs

**Files:**
- Create: `Reference/Batorego/Rys I_1 PZT.pdf`
- Create: `Reference/Batorego/Rys I_4 profil kanalizacja sanitarna.pdf`
- Create: `Reference/Batorego/Rys I_8 profil kanalizacja deszczowa.pdf`

- [ ] Commit byte-identical PDFs from the existing PrefabScan 4.2.2 reference package.

### Task 3: CI loop

**Files:**
- Create: `.github/workflows/batorego-e2e.yml`

- [ ] Run application/domain tests on Windows.
- [ ] Run the headless benchmark on the three real PDFs.
- [ ] Upload diagnostics and score artifacts with `if: always()`.
- [ ] Publish the compact score to the GitHub job summary.
