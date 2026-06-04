using System.Text.Json;

namespace ELearnGamePlatform.API.Contracts;

public sealed class SlideEditorOperationMessage
{
    public int DeckId { get; set; }
    public int SlideId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string? ElementId { get; set; }
    public long Revision { get; set; }
    public JsonElement? Payload { get; set; }
}

public sealed class SlideEditorSelectionMessage
{
    public int DeckId { get; set; }
    public int? SlideId { get; set; }
    public string? ElementId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SlideEditorPresenceMessage
{
    public int DeckId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "online";
}
