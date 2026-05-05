using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ImageLocker.ViewModels;

/// <summary>
/// Base class for view models.
/// Provides PropertyChanged support and a helper method for safe property updates.
/// </summary>

public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Updates a backing field only when the value actually changes.
    /// Returns true when a change happened so callers can trigger follow-up work only when needed.
    /// </summary>
    /// <typeparam name="T">Type of the property being updated.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">New value to store.</param>
    /// <param name="propertyName">Name of the property that changed. Filled automatically by <see cref="CallerMemberNameAttribute"/>.</param>
    /// <returns><c>true</c> if the value changed; otherwise <c>false</c>.</returns>

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>
    /// Raises PropertyChanged manually for a specific property.
    /// </summary>
    /// <param name="propertyName">Name of the property that changed. Filled automatically when omitted.</param>

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Simple ICommand implementation used by WPF buttons and menu actions.
/// </summary>

public class RelayCommand : ICommand
{
    private readonly Action executeAction;
    private readonly Func<bool>? canExecuteFunction;

    /// <summary>
    /// Creates a command with an action to run and an optional availability check.
    /// </summary>
    /// <param name="execute">Action to run when the command is executed.</param>
    /// <param name="canExecute">Optional function that decides whether the command is currently enabled.</param>

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        this.executeAction = execute;
        this.canExecuteFunction = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    
    /// <summary>
    /// Returns whether the command is currently allowed to run.
    /// </summary>
    /// <param name="parameter">Optional WPF command parameter. This implementation does not use it.</param>
    /// <returns><c>true</c> when the command is allowed to run; otherwise <c>false</c>.</returns>

    public bool CanExecute(object? parameter) => this.canExecuteFunction?.Invoke() ?? true;

    /// <summary>
    /// Runs the action associated with the command.
    /// </summary>
    /// <param name="parameter">Optional WPF command parameter. This implementation does not use it.</param>

    public void Execute(object? parameter) => this.executeAction();

    /// <summary>
    /// Notifies WPF that the command's enabled state may have changed.
    /// </summary>

    public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
