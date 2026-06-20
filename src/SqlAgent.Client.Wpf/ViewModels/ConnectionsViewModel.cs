using System.Collections.ObjectModel;
using System.Windows.Input;
using SqlAgent.Api.Local;
using SqlAgent.Client.Wpf.Mvvm;
using SqlAgent.Client.Wpf.Services;

namespace SqlAgent.Client.Wpf.ViewModels;

/// <summary>
/// Connection configuration: list, add, edit, delete, test, and read-only toggle (CD-50 T9 DoD). The edit
/// fields double as the "new connection" form — <see cref="_editingId"/> null means create, set means update.
/// Raises <see cref="ActiveConnectionChanged"/> so the chat and visibility tabs follow the selection.
/// </summary>
public sealed class ConnectionsViewModel : ObservableObject
{
    private readonly LocalApiClient _api;

    public ConnectionsViewModel(LocalApiClient api)
    {
        _api = api;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewCommand = new RelayCommand(StartNew);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !string.IsNullOrWhiteSpace(EditName));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => _editingId is not null);
        TestCommand = new AsyncRelayCommand(TestAsync);
    }

    public ObservableCollection<DatabaseDto> Databases { get; } = [];

    /// <summary>Provider choices for the form combo.</summary>
    public IReadOnlyList<DatabaseProviderTypeDto> Providers { get; } =
        [DatabaseProviderTypeDto.SqlServer, DatabaseProviderTypeDto.Postgres];

    public ICommand RefreshCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand TestCommand { get; }

    /// <summary>Fired with the active connection id (or null) whenever the selection or list changes.</summary>
    public event Action<Guid?>? ActiveConnectionChanged;

    private DatabaseDto? _selected;
    public DatabaseDto? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            if (value is not null) LoadIntoForm(value);
            ActiveConnectionChanged?.Invoke(value?.Id);
        }
    }

    private Guid? _editingId;
    public bool IsEditingExisting => _editingId is not null;

    private string _editName = "";
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

    private DatabaseProviderTypeDto _editProvider = DatabaseProviderTypeDto.SqlServer;
    public DatabaseProviderTypeDto EditProvider { get => _editProvider; set => SetProperty(ref _editProvider, value); }

    private bool _editIsReadOnly = true;
    public bool EditIsReadOnly { get => _editIsReadOnly; set => SetProperty(ref _editIsReadOnly, value); }

    private string _editConnectionString = "";
    public string EditConnectionString { get => _editConnectionString; set => SetProperty(ref _editConnectionString, value); }

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public async Task RefreshAsync()
    {
        await Guard(async () =>
        {
            var list = await _api.ListDatabasesAsync();
            var keepId = _selected?.Id;
            Databases.Clear();
            foreach (var db in list) Databases.Add(db);
            Status = $"{Databases.Count} connection(s).";
            // Re-select the same row if it survived a refresh so the chat/visibility tabs stay put.
            Selected = Databases.FirstOrDefault(d => d.Id == keepId);
        });
    }

    private void StartNew()
    {
        _editingId = null;
        _selected = null;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsEditingExisting));
        EditName = "";
        EditProvider = DatabaseProviderTypeDto.SqlServer;
        EditIsReadOnly = true;
        EditConnectionString = "";
        Status = "Enter a new connection, then Save.";
        ActiveConnectionChanged?.Invoke(null);
    }

    private void LoadIntoForm(DatabaseDto db)
    {
        _editingId = db.Id;
        OnPropertyChanged(nameof(IsEditingExisting));
        EditName = db.Name;
        EditProvider = db.Provider;
        EditIsReadOnly = db.IsReadOnly;
        // The secret never comes back over the wire; an empty box on an existing row means "keep current".
        EditConnectionString = "";
        Status = db.HasSecret ? "Leave the connection string blank to keep the saved secret." : "";
    }

    private async Task SaveAsync()
    {
        await Guard(async () =>
        {
            var saved = await _api.SaveDatabaseAsync(new SaveDatabaseParams(
                _editingId, EditName.Trim(), EditProvider, EditIsReadOnly,
                string.IsNullOrWhiteSpace(EditConnectionString) ? null : EditConnectionString));
            Status = $"Saved '{saved.Name}'.";
            await RefreshAsync();
            Selected = Databases.FirstOrDefault(d => d.Id == saved.Id);
        });
    }

    private async Task DeleteAsync()
    {
        if (_editingId is not { } id) return;
        await Guard(async () =>
        {
            await _api.DeleteDatabaseAsync(id);
            Status = "Deleted.";
            StartNew();
            await RefreshAsync();
        });
    }

    private async Task TestAsync()
    {
        await Guard(async () =>
        {
            // Test the saved secret when editing an existing row with no new string typed; otherwise the draft.
            var p = _editingId is { } id && string.IsNullOrWhiteSpace(EditConnectionString)
                ? new TestConnectionParams(id, null, null)
                : new TestConnectionParams(null, EditProvider, EditConnectionString);
            var result = await _api.TestConnectionAsync(p);
            Status = result.Success
                ? $"Connection OK ({result.ServerVersion}, {result.ElapsedMs} ms)."
                : $"Connection failed: {result.Error}";
        });
    }

    /// <summary>Runs an API action, turning a <see cref="LocalApiException"/> into a status line instead of a crash.</summary>
    private async Task Guard(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (LocalApiException ex)
        {
            Status = $"Error ({ex.Code}): {ex.Message}";
        }
    }
}
