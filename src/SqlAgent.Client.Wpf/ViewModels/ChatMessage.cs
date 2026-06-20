namespace SqlAgent.Client.Wpf.ViewModels;

public enum ChatRole { User, Agent, Error }

/// <summary>One entry in the chat transcript. Immutable: messages are appended, never edited, so a plain
/// record with no change notification is enough.</summary>
public record ChatMessage(ChatRole Role, string Text)
{
    public bool IsUser => Role == ChatRole.User;
    public bool IsError => Role == ChatRole.Error;
}
