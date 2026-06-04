using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ELearnGamePlatform.API.Hubs;

[Authorize]
public sealed class SlideEditorHub : Hub
{
    private readonly ISlideDeckRepository _slideDeckRepository;
    private readonly ILogger<SlideEditorHub> _logger;

    public SlideEditorHub(ISlideDeckRepository slideDeckRepository, ILogger<SlideEditorHub> logger)
    {
        _slideDeckRepository = slideDeckRepository;
        _logger = logger;
    }

    public async Task JoinDeck(int deckId)
    {
        await EnsureDeckAccessAsync(deckId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(deckId));
    }

    public async Task LeaveDeck(int deckId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(deckId));
    }

    public async Task BroadcastOperation(SlideEditorOperationMessage message)
    {
        await EnsureDeckAccessAsync(message.DeckId);
        await Clients.OthersInGroup(GroupName(message.DeckId)).SendAsync("SlideEditorOperation", message);
    }

    public async Task BroadcastSelection(SlideEditorSelectionMessage message)
    {
        await EnsureDeckAccessAsync(message.DeckId);
        message.DisplayName = CleanDisplayName(message.DisplayName);
        await Clients.OthersInGroup(GroupName(message.DeckId)).SendAsync("SlideEditorSelection", message);
    }

    public async Task BroadcastPresence(SlideEditorPresenceMessage message)
    {
        await EnsureDeckAccessAsync(message.DeckId);
        message.DisplayName = CleanDisplayName(message.DisplayName);
        await Clients.OthersInGroup(GroupName(message.DeckId)).SendAsync("SlideEditorPresence", message);
    }

    private async Task EnsureDeckAccessAsync(int deckId)
    {
        var currentUserId = Context.User?.GetCurrentUserId()?.ToString();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            throw new HubException("Unauthorized");
        }

        var deck = await _slideDeckRepository.GetByIdAsync(deckId);
        var ownerId = deck?.FolderProject?.UploadedBy ?? deck?.Document?.UploadedBy;
        if (deck == null || !string.Equals(ownerId?.Trim(), currentUserId, StringComparison.Ordinal))
        {
            _logger.LogWarning("User {UserId} attempted to access slide editor deck {DeckId}", currentUserId, deckId);
            throw new HubException("Deck access denied");
        }
    }

    private static string GroupName(int deckId) => $"deck:{deckId}";

    private static string CleanDisplayName(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? "Workspace collaborator" : displayName.Trim();
}
