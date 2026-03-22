using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.Infrastructure.Repositories;

public class SlideDeckRepository : ISlideDeckRepository
{
    private readonly ApplicationDbContext _context;

    public SlideDeckRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SlideDeck> ReplaceForDocumentAsync(SlideDeck deck, IEnumerable<SlideItem>? items = null)
    {
        var itemList = (items ?? Array.Empty<SlideItem>()).ToList();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var existingDecks = await _context.SlideDecks
            .Include(existing => existing.Items)
            .Where(existing => existing.DocumentId == deck.DocumentId)
            .ToListAsync();

        if (existingDecks.Any())
        {
            _context.SlideItems.RemoveRange(existingDecks.SelectMany(existing => existing.Items));
            _context.SlideDecks.RemoveRange(existingDecks);
            await _context.SaveChangesAsync();
        }

        _context.SlideDecks.Add(deck);
        await _context.SaveChangesAsync();

        if (itemList.Any())
        {
            foreach (var item in itemList)
            {
                item.SlideDeckId = deck.Id;
            }

            _context.SlideItems.AddRange(itemList);
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        return await GetByIdAsync(deck.Id) ?? deck;
    }

    public async Task<SlideDeck?> GetByIdAsync(int id)
    {
        var deck = await _context.SlideDecks
            .Include(deck => deck.Document)
            .Include(deck => deck.Items)
            .FirstOrDefaultAsync(deck => deck.Id == id);

        if (deck?.Items != null)
        {
            deck.Items = deck.Items.OrderBy(item => item.SlideIndex).ToList();
        }

        return deck;
    }

    public async Task<SlideDeck?> GetLatestByDocumentIdAsync(int documentId)
    {
        var deck = await _context.SlideDecks
            .Include(deck => deck.Document)
            .Include(deck => deck.Items)
            .Where(deck => deck.DocumentId == documentId)
            .OrderByDescending(deck => deck.CreatedAt)
            .FirstOrDefaultAsync();

        if (deck?.Items != null)
        {
            deck.Items = deck.Items.OrderBy(item => item.SlideIndex).ToList();
        }

        return deck;
    }

    public async Task<bool> UpdateDeckAsync(SlideDeck deck)
    {
        var existing = await _context.SlideDecks.FindAsync(deck.Id);
        if (existing == null)
        {
            return false;
        }

        deck.UpdatedAt = DateTime.UtcNow;
        _context.Entry(existing).CurrentValues.SetValues(deck);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReplaceItemsAsync(int deckId, IEnumerable<SlideItem> items)
    {
        var existingDeck = await _context.SlideDecks.FindAsync(deckId);
        if (existingDeck == null)
        {
            return false;
        }

        var itemList = items.ToList();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var existingItems = await _context.SlideItems
            .Where(item => item.SlideDeckId == deckId)
            .ToListAsync();

        _context.SlideItems.RemoveRange(existingItems);
        await _context.SaveChangesAsync();

        foreach (var item in itemList)
        {
            item.SlideDeckId = deckId;
        }

        if (itemList.Any())
        {
            _context.SlideItems.AddRange(itemList);
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return true;
    }

    public async Task<SlideItem?> GetItemAsync(int deckId, int itemId)
    {
        return await _context.SlideItems
            .FirstOrDefaultAsync(item => item.SlideDeckId == deckId && item.Id == itemId);
    }

    public async Task<bool> UpdateItemAsync(SlideItem item)
    {
        var existing = await _context.SlideItems.FindAsync(item.Id);
        if (existing == null)
        {
            return false;
        }

        item.UpdatedAt = DateTime.UtcNow;
        _context.Entry(existing).CurrentValues.SetValues(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
