# Document Parsing with Docling

Verified from source: 2026-06-06.

## Why Docling Was Added

The legacy extraction pipeline remains the reliable baseline:

- PdfPig for text-layer PDFs.
- OpenXML for DOCX files.
- Tesseract OCR for images and scanned PDF pages.

Docling was added as an optional structure-preserving parser before AI analysis. When successful, it can provide Markdown headings, lists, and tables that improve downstream topic detection, grounded question generation, and slide outlining.

Docling does not replace the legacy processors. The feature is disabled by default and the legacy extraction result is always produced first.

## Pipeline Position

```text
Uploaded document
  -> legacy extraction with PdfPig/OpenXML/Tesseract
  -> optional Docling CLI parse
  -> deterministically repair likely Vietnamese mojibake
  -> select clean/repaired Docling Markdown, otherwise legacy text
  -> input quality gate and token planning
  -> AI content analysis
  -> question and slide generation
```

When Docling succeeds, `Document.ExtractedText` receives its Markdown output. Processing metadata records:

- `ExtractionProvider`
- `ExternalParsingSucceeded`
- `ExternalParsingElapsedMs`
- `ExternalParsingError`

No additional table or migration is required.

Vietnamese mojibake is treated as encoding corruption, not as a translation
problem. The parser attempts a deterministic Windows-1252/Latin-1 byte recovery
back to UTF-8 while preserving Markdown headings, lists, tables, and line
breaks. Translation is deliberately not used because translated text can change
meaning and break evidence grounding against the uploaded document.

Successfully repaired Markdown is recorded with
`ExtractionProvider=docling-repaired` and
`ExternalParsingSucceeded=true`. The legacy extractor remains the final safety
net when deterministic repair cannot produce sufficiently long, readable
Vietnamese with fewer corruption markers.

## Install Docling

Install Docling in the Python environment used to launch the API:

```powershell
pip install docling
```

Verify that the command is visible from the same terminal:

```powershell
docling --help
```

If a virtual environment is used, activate it before starting the .NET API.

## Enable Document Parsing

Update `DocumentParsing` in `src/ELearnGamePlatform.API/appsettings.json` or provide equivalent environment-specific configuration:

```json
{
  "DocumentParsing": {
    "Enabled": true,
    "Provider": "docling",
    "DoclingCommand": "docling",
    "TimeoutSeconds": 180,
    "MinMarkdownLength": 500,
    "FallbackToLegacy": true,
    "PreferMarkdownForGeneration": true,
    "OutputDirectory": "uploads/parsed"
  }
}
```

The minimum required change is:

```text
DocumentParsing.Enabled=true
```

`FallbackToLegacy` and `PreferMarkdownForGeneration` are active behavior switches:

- `FallbackToLegacy=true`: continue with legacy text when Docling fails.
- `FallbackToLegacy=false`: fail document processing when enabled Docling parsing fails.
- `PreferMarkdownForGeneration=true`: select valid Docling Markdown for analysis and generation.
- `PreferMarkdownForGeneration=false`: still run Docling and record its result, but keep legacy text as `Document.ExtractedText`.

For an environment variable override:

```powershell
$env:DocumentParsing__Enabled = "true"
dotnet run --project src\ELearnGamePlatform.API
```

Restart the API after changing configuration.

## Fallback Behavior

Legacy extraction runs before Docling. The legacy text remains selected when:

- Document parsing is disabled.
- The `docling` command cannot be started.
- Docling exits with a non-zero code.
- Parsing exceeds `TimeoutSeconds`.
- No Markdown file is produced.
- Markdown is shorter than `MinMarkdownLength`.
- Vietnamese mojibake remains after deterministic repair or the repair does not
  satisfy the quality checks.
- The parser returns another ordinary failure or throws an exception.

With `FallbackToLegacy=true`, these failures are logged and stored in processing metadata, then analysis continues with legacy text. With `FallbackToLegacy=false`, document processing stops and the document/job is marked failed with a clear parsing error.

## Recommended Configuration

For a workstation with 48 GB RAM and 8 GB VRAM, start with:

```json
{
  "DocumentParsing": {
    "Enabled": true,
    "Provider": "docling",
    "DoclingCommand": "docling",
    "TimeoutSeconds": 300,
    "MinMarkdownLength": 500,
    "FallbackToLegacy": true,
    "PreferMarkdownForGeneration": true,
    "OutputDirectory": "uploads/parsed"
  }
}
```

Operational recommendations:

- Process one large document at a time when Docling and Ollama share the machine.
- Preserve the current legacy fallback behavior so model loading, memory pressure, or difficult PDFs do not block ingestion.
- Use a 300-second timeout initially for large or image-heavy documents; reduce it after observing local timings.
- Keep `MinMarkdownLength` at 500 unless short legitimate documents are being rejected.
- Ensure the output drive has enough free space and periodically remove obsolete parser output after confirming it is no longer needed.

The current integration invokes the Docling CLI and does not configure a Docling GPU backend. Actual CPU/GPU use depends on the installed Docling version and its dependencies. The values above are a conservative operating baseline, not a hardware benchmark.

## Troubleshooting

### Command Not Found

Symptoms:

- Log contains `Docling command could not be started`.
- Metadata records external parsing failure and legacy extraction is selected.

Checks:

```powershell
Get-Command docling
docling --help
python -m pip show docling
```

Install or reinstall with:

```powershell
python -m pip install docling
```

If `docling` is not on `PATH`, set `DocumentParsing.DoclingCommand` to its executable path or start the API from the activated Python environment.

### Timeout

Symptoms:

- Log contains `Docling timed out after ... seconds`.
- The Docling process tree is terminated and legacy text is used.

Increase the timeout for large or scanned documents:

```json
{
  "DocumentParsing": {
    "TimeoutSeconds": 300
  }
}
```

Also avoid running multiple memory-heavy parses alongside Ollama generation on the same GPU.

### Markdown Too Short

Symptoms:

- Error contains `Parsed markdown too short`.
- Markdown length is below `DocumentParsing.MinMarkdownLength`.

Inspect the generated Markdown and the source document. For genuinely short documents, lower the threshold carefully:

```json
{
  "DocumentParsing": {
    "MinMarkdownLength": 100
  }
}
```

Do not lower the threshold only to accept empty, corrupted, or low-signal parser output. Legacy extraction is safer in that case.

### Parser Output Folder

The default output root is:

```text
uploads/parsed
```

Each parse creates a unique subdirectory based on the input filename and a short random suffix. Docling Markdown files remain there for inspection. A relative `OutputDirectory` is resolved from the API process working directory; an absolute path is used as-is.

If output cannot be created:

- Confirm the API process has write permission.
- Confirm the disk has free space.
- Use an absolute `OutputDirectory` when the working directory is uncertain.

## Scope of This Integration

This pass integrates only Docling as the optional external parser.

MinerU, PaddleOCR, Marker, and olmOCR are not integrated in this pass. The existing PdfPig, OpenXML, and Tesseract paths remain the supported fallback pipeline.
