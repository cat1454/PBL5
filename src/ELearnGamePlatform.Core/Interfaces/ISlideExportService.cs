using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface ISlideExportService
{
    string RenderHtmlFile(SlideDeck deck, IReadOnlyList<SlideItem> items);
    string RenderPrintHtml(SlideDeck deck, IReadOnlyList<SlideItem> items);
    byte[] BuildPptx(SlideDeck deck, IReadOnlyList<SlideItem> items);
}
