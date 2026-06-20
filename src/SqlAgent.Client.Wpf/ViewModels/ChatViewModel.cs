using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Input;
using SqlAgent.Api.Local;
using SqlAgent.Client.Wpf.Mvvm;
using SqlAgent.Client.Wpf.Services;

namespace SqlAgent.Client.Wpf.ViewModels;

/// <summary>
/// The chat/query workflow (CD-50 T9 DoD): a transcript of messages, the executed SQL, the result grid, error
/// surfacing, a copy-SQL action, and a voice-input hook. The local API has no NL→SQL step, so the input is the
/// SQL itself — sent via <c>execute_sql</c>, with policy denials and execution errors shown inline as messages.
/// </summary>
public sealed class ChatViewModel : ObservableObject
{
    private readonly LocalApiClient _api;
    private readonly IVoiceInputService _voice;
    private Guid? _connectionId;

    public ChatViewModel(LocalApiClient api, IVoiceInputService voice)
    {
        _api = api;
        _voice = voice;
        SendCommand = new AsyncRelayCommand(SendAsync,
            () => _connectionId is not null && !string.IsNullOrWhiteSpace(Input));
        CopySqlCommand = new RelayCommand(CopySql, () => !string.IsNullOrEmpty(LastSql));
        VoiceCommand = new AsyncRelayCommand(CaptureVoiceAsync, () => _voice.IsSupported);
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public ICommand SendCommand { get; }
    public ICommand CopySqlCommand { get; }
    public ICommand VoiceCommand { get; }

    public bool IsVoiceSupported => _voice.IsSupported;

    private string _input = "";
    public string Input { get => _input; set => SetProperty(ref _input, value); }

    private string _status = "Select a connection to start querying.";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private string? _lastSql;
    public string? LastSql { get => _lastSql; private set { if (SetProperty(ref _lastSql, value)) OnPropertyChanged(nameof(HasSql)); } }
    public bool HasSql => !string.IsNullOrEmpty(LastSql);

    private DataView? _results;
    public DataView? Results { get => _results; private set { if (SetProperty(ref _results, value)) OnPropertyChanged(nameof(HasResults)); } }
    public bool HasResults => _results is not null;

    /// <summary>Points the chat at a connection (or clears it). Called by the shell on selection change.</summary>
    public void SetConnection(Guid? connectionId)
    {
        _connectionId = connectionId;
        Status = connectionId is null ? "Select a connection to start querying." : "Type SQL and press Send.";
    }

    private async Task SendAsync()
    {
        if (_connectionId is not { } id) return;
        var sql = Input.Trim();
        Messages.Add(new ChatMessage(ChatRole.User, sql));
        Input = "";
        try
        {
            var result = await _api.ExecuteSqlAsync(id, sql);
            LastSql = result.Sql;
            Results = BuildTable(result).DefaultView;
            var note = result.Truncated ? " (results truncated)" : "";
            Messages.Add(new ChatMessage(ChatRole.Agent,
                $"{result.RowCount} row(s) in {result.ElapsedMs} ms{note}."));
            Status = $"{result.RowCount} row(s){note}.";
        }
        catch (LocalApiException ex)
        {
            // Policy denials (policy_denied_*), timeouts, and connection errors all land here as one message.
            Results = null;
            Messages.Add(new ChatMessage(ChatRole.Error, $"{ex.Code}: {ex.Message}"));
            Status = $"Error: {ex.Code}";
        }
    }

    private void CopySql()
    {
        if (!string.IsNullOrEmpty(LastSql))
            Clipboard.SetText(LastSql);
    }

    private async Task CaptureVoiceAsync()
    {
        var text = await _voice.CaptureAsync();
        if (!string.IsNullOrWhiteSpace(text))
            Input = text;
    }

    /// <summary>Projects a query result into a <see cref="DataTable"/> for the grid. Column names are made unique
    /// because a DataTable rejects duplicates, which a SQL projection can legitimately produce.</summary>
    public static DataTable BuildTable(QueryResultDto result)
    {
        var table = new DataTable();
        var seen = new Dictionary<string, int>();
        foreach (var raw in result.Columns)
        {
            var name = string.IsNullOrEmpty(raw) ? "(column)" : raw;
            if (seen.TryGetValue(name, out var n))
            {
                seen[name] = n + 1;
                name = $"{name} ({n + 1})";
            }
            else
            {
                seen[name] = 1;
            }
            table.Columns.Add(name, typeof(object));
        }
        foreach (var row in result.Rows)
            table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        return table;
    }
}
