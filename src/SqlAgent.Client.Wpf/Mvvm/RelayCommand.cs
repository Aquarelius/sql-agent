using System.Windows.Input;

namespace SqlAgent.Client.Wpf.Mvvm;

/// <summary>Synchronous <see cref="ICommand"/> over a delegate. WPF calls <see cref="RaiseCanExecuteChanged"/>
/// indirectly through <see cref="CommandManager.RequerySuggested"/>; we also expose it for explicit refreshes.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute is null || canExecute();
    public void Execute(object? parameter) => execute();
    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// Async <see cref="ICommand"/> that disables itself while running, so a slow pipe call can't be double-fired
/// and the UI thread is never blocked (CD-50 DoD: "never block the UI thread", "no UI freezes").
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (canExecute is null || canExecute());

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute();
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
