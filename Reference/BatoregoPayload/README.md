# Batorego headless fixture

`storm.part*.b64` stores a split Base64 payload for the Batorego storm-profile raster fixture used by GitHub Actions.

The fixture is a rasterized visual equivalent of `Rys I_8 profil kanalizacja deszczowa.pdf`, intentionally sized close to the PDFium render used by PrefabScan OCR. It is not the byte-identical source PDF.

The workflow concatenates the parts, decodes the image, wraps it into a one-page PDF, then runs `SewerScan.BenchmarkRunner` against that PDF. This makes OCR/parser iteration reproducible without requiring a local user run.
