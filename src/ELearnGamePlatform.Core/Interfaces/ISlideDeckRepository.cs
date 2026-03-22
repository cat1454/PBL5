using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface ISlideDeckRepository
{
    Task<SlideDeck> ReplaceForDocumentAsync(SlideDeck deck, IEnumerable<SlideItem>? items = null);
    Task<SlideDeck?> GetByIdAsync(int id);
    Task<SlideDeck?> GetLatestByDocumentIdAsync(int documentId);
    Task<bool> UpdateDeckAsync(SlideDeck deck);
    Task<bool> ReplaceItemsAsync(int deckId, IEnumerable<SlideItem> items);
    Task<SlideItem?> GetItemAsync(int deckId, int itemId);
    Task<bool> UpdateItemAsync(SlideItem item);
}
