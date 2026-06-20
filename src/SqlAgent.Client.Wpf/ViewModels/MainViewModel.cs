using SqlAgent.Client.Wpf.Services;

namespace SqlAgent.Client.Wpf.ViewModels;

/// <summary>Shell view model: owns the three tabs and keeps the chat and visibility tabs pointed at whichever
/// connection is selected on the connections tab.</summary>
public sealed class MainViewModel
{
    public ConnectionsViewModel Connections { get; }
    public TableVisibilityViewModel TableVisibility { get; }
    public ChatViewModel Chat { get; }

    public MainViewModel(LocalApiClient api, IVoiceInputService voice)
    {
        Connections = new ConnectionsViewModel(api);
        TableVisibility = new TableVisibilityViewModel(api);
        Chat = new ChatViewModel(api, voice);
        Connections.ActiveConnectionChanged += OnActiveConnectionChanged;
    }

    private async void OnActiveConnectionChanged(Guid? id)
    {
        Chat.SetConnection(id);
        await TableVisibility.SetConnectionAsync(id);
    }

    /// <summary>Loads the connection list on startup. Errors surface as the connections tab's status line.</summary>
    public Task InitializeAsync() => Connections.RefreshAsync();
}
