using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SqlAgent.Client.Wpf.Mvvm;

/// <summary>Minimal INotifyPropertyChanged base. Hand-rolled to avoid an MVVM-framework dependency for what
/// is a handful of lines (CD-50 keeps dependencies lean).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Sets the backing field and raises <see cref="PropertyChanged"/> only when the value changes.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
