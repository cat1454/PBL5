using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

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

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SlideEditor connection {ConnectionId} established for user {UserId}", Context.ConnectionId, Context.User?.GetCurrentUserId());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning("SlideEditor connection {ConnectionId} disconnected with error: {Error}", Context.ConnectionId, exception.Message);
        }
        else
        {
            _logger.LogInformation("SlideEditor connection {ConnectionId} disconnected smoothly", Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinDeck(int deckId)
    {
        _logger.LogInformation("Connection {ConnectionId} attempting to join deck {DeckId}", Context.ConnectionId, deckId);
        try
        {
            await EnsureDeckAccessAsync(deckId);
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(deckId));
            Context.Items[$"JoinedDeck:{deckId}"] = true;
            _logger.LogInformation("Connection {ConnectionId} successfully joined deck {DeckId}", Context.ConnectionId, deckId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Connection {ConnectionId} failed to join deck {DeckId}: {Error}", Context.ConnectionId, deckId, ex.Message);
            throw;
        }
    }

    public async Task LeaveDeck(int deckId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(deckId));
        Context.Items.Remove($"JoinedDeck:{deckId}");
        _logger.LogInformation("Connection {ConnectionId} left deck {DeckId}", Context.ConnectionId, deckId);
    }

    public async Task BroadcastOperation(SlideEditorOperationMessage message)
    {
        VerifyConnectionJoinedDeck(message.DeckId);
        await Clients.OthersInGroup(GroupName(message.DeckId)).SendAsync("SlideEditorOperation", message);
    }

    public async Task BroadcastSelection(SlideEditorSelectionMessage message)
    {
        VerifyConnectionJoinedDeck(message.DeckId);
        message.DisplayName = CleanDisplayName(message.DisplayName);
        await Clients.OthersInGroup(GroupName(message.DeckId)).SendAsync("SlideEditorSelection", message);
    }

    public async Task BroadcastPresence(SlideEditorPresenceMessage message)
    {
        VerifyConnectionJoinedDeck(message.DeckId);
        message.DisplayName = CleanDisplayName(message.DisplayName);
        await Clients.OthersInGroup(GroupName(message.DeckId)).SendAsync("SlideEditorPresence", message);
    }

    private void VerifyConnectionJoinedDeck(int deckId)
    {
        if (!Context.Items.ContainsKey($"JoinedDeck:{deckId}"))
        {
            _logger.LogWarning("Connection {ConnectionId} attempted to access deck {DeckId} without joining first", Context.ConnectionId, deckId);
            throw new HubException("Deck access denied: not joined");
        }
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
            _logger.LogWarning("User {UserId} access denied for slide editor deck {DeckId}", currentUserId, deckId);
            throw new HubException("Deck access denied");
        }
    }

    private static string GroupName(int deckId) => $"deck:{deckId}";

    private static string CleanDisplayName(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? "Workspace collaborator" : displayName.Trim();
}
