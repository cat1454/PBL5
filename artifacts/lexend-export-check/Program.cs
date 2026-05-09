using System.IO.Compression;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Services.AI;
using ELearnGamePlatform.Services.Slides;

var outputDir = Path.Combine(AppContext.BaseDirectory, "generated");
Directory.CreateDirectory(outputDir);

var deck = new SlideDeck
{
    Id = 9001,
    Title = "Lexend verification deck",
    Subtitle = "Frontend and slide export font check",
    ThemeKey = "editorial-sunrise",
    Status = SlideDeckStatus.Completed
};

var items = new List<SlideItem>
{
    new()
    {
        Id = 1,
        SlideDeckId = deck.Id,
        SlideIndex = 1,
        SlideType = SlideItemType.Title,
        Status = SlideItemStatus.Completed,
        Heading = "Lexend title slide",
        Subheading = "Generated locally for export verification",
        Goal = "Confirm title, body, and notes share one font",
        SpeakerNotes = "Speaker notes should use Lexend in HTML and PPTX."
    },
    new()
    {
        Id = 2,
        SlideDeckId = deck.Id,
        SlideIndex = 2,
        SlideType = SlideItemType.Quote,
        Status = SlideItemStatus.Completed,
        Heading = "Quote slide uses the same family",
        Subheading = "No legacy serif stack",
        Goal = "Check inherited quote typography",
        SpeakerNotes = "Quote slides should remain italic but inherit Lexend."
    }
};

items[0].SetBodyBlocks(new List<string> { "Body text uses Lexend first.", "Fallback stays clean when Lexend is unavailable." });
items[1].SetBodyBlocks(new List<string> { "A quote can be italic without switching to a serif font." });
deck.Items = items;

var generator = new SlideGeneratorService(null!, null!);
var exportService = new SlideExportService(generator);

var previewHtml = exportService.RenderHtmlFile(deck, items);
var printHtml = exportService.RenderPrintHtml(deck, items);
var pptx = exportService.BuildPptx(deck, items);

var previewPath = Path.Combine(outputDir, "sample-preview.html");
var printPath = Path.Combine(outputDir, "sample-print.html");
var pptxPath = Path.Combine(outputDir, "sample.pptx");

File.WriteAllText(previewPath, previewHtml);
File.WriteAllText(printPath, printHtml);
File.WriteAllBytes(pptxPath, pptx);

var htmlHasLexendStack = previewHtml.Contains("'Lexend','Noto Sans'", StringComparison.Ordinal)
    && printHtml.Contains("'Lexend','Noto Sans'", StringComparison.Ordinal);
var htmlHasLegacySerifStack = previewHtml.Contains("Georgia", StringComparison.OrdinalIgnoreCase)
    || previewHtml.Contains("Times", StringComparison.OrdinalIgnoreCase)
    || printHtml.Contains("Georgia", StringComparison.OrdinalIgnoreCase)
    || printHtml.Contains("Times", StringComparison.OrdinalIgnoreCase);

var pptxHasLexend = false;
var pptxHasLegacyFonts = false;
using (var zip = ZipFile.OpenRead(pptxPath))
{
    foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
    {
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        pptxHasLexend |= xml.Contains("typeface=\"Lexend\"", StringComparison.Ordinal);
        pptxHasLegacyFonts |= xml.Contains("typeface=\"Arial\"", StringComparison.Ordinal)
            || xml.Contains("typeface=\"Georgia\"", StringComparison.Ordinal);
    }
}

Console.WriteLine($"previewHtml={previewPath}");
Console.WriteLine($"printHtml={printPath}");
Console.WriteLine($"pptx={pptxPath}");
Console.WriteLine($"htmlHasLexendStack={htmlHasLexendStack}");
Console.WriteLine($"htmlHasLegacySerifStack={htmlHasLegacySerifStack}");
Console.WriteLine($"pptxHasLexend={pptxHasLexend}");
Console.WriteLine($"pptxHasLegacyFonts={pptxHasLegacyFonts}");

return htmlHasLexendStack && !htmlHasLegacySerifStack && pptxHasLexend && !pptxHasLegacyFonts ? 0 : 1;
