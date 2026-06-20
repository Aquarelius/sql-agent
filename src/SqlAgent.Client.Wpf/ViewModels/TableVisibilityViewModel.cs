using System.Collections.ObjectModel;
using System.Windows.Input;
using SqlAgent.Client.Wpf.Mvvm;
using SqlAgent.Client.Wpf.Services;

namespace SqlAgent.Client.Wpf.ViewModels;

/// <summary>One toggleable table row. Flipping <see cref="IsVisible"/> in the UI persists immediately through
/// <paramref name="persist"/>; the load path sets <see cref="SetWithoutPersist"/> so populating the list
/// doesn't fire a write back for every row.</summary>
public sealed class TableVisibilityItem(string schema, string table, bool isVisible, Func<TableVisibilityItem, bool, Task> persist)
    : ObservableObject
{
    public string Schema { get; } = schema;
    public string Table { get; } = table;
    public string Display => string.IsNullOrEmpty(Schema) ? Table : $"{Schema}.{Table}";

    private bool _isVisible = isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
                _ = persist(this, value);
        }
    }

    public void SetWithoutPersist(bool value) => SetProperty(ref _isVisible, value, nameof(IsVisible));
}

/// <summary>
/// Per-table visibility configuration (CD-50 T9 DoD: "configure table visibility"). Lists every live table —
/// including already-hidden ones, which <c>describe_schema</c> would omit — and persists each toggle through
/// the <c>set_table_policy</c> op. A hidden table is excluded from the LLM schema and blocked by query policy.
/// </summary>
public sealed class TableVisibilityViewModel : ObservableObject
{
    private readonly LocalApiClient _api;
    private Guid? _connectionId;

    public TableVisibilityViewModel(LocalApiClient api)
    {
        _api = api;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _connectionId is not null);
    }

    public ObservableCollection<TableVisibilityItem> Tables { get; } = [];
    public ICommand RefreshCommand { get; }

    private string _status = "Select a connection to configure table visibility.";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    /// <summary>Points the tab at a connection (or clears it) and reloads. Called by the shell on selection change.</summary>
    public async Task SetConnectionAsync(Guid? connectionId)
    {
        _connectionId = connectionId;
        Tables.Clear();
        if (connectionId is null)
        {
            Status = "Select a connection to configure table visibility.";
            return;
        }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_connectionId is not { } id) return;
        try
        {
            var result = await _api.ListTablePoliciesAsync(id);
            Tables.Clear();
            foreach (var t in result.Tables)
                Tables.Add(new TableVisibilityItem(t.Schema, t.Table, t.IsVisible, PersistAsync));
            Status = Tables.Count == 0 ? "No tables found for this connection." : $"{Tables.Count} table(s).";
        }
        catch (LocalApiException ex)
        {
            Status = $"Error ({ex.Code}): {ex.Message}";
        }
    }

    private async Task PersistAsync(TableVisibilityItem item, bool isVisible)
    {
        if (_connectionId is not { } id) return;
        try
        {
            await _api.SetTablePolicyAsync(id, item.Schema, item.Table, isVisible);
            Status = $"{item.Display} is now {(isVisible ? "visible" : "hidden")}.";
        }
        catch (LocalApiException ex)
        {
            // Roll the checkbox back to match the server, without re-triggering a persist.
            item.SetWithoutPersist(!isVisible);
            Status = $"Error ({ex.Code}): {ex.Message}";
        }
    }
}
