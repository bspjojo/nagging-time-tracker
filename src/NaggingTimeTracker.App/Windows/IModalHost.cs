using System.Windows;

namespace NaggingTimeTracker.App.Windows;

/// <summary>
/// A window that hides itself on deactivation. Dialogs it opens must run inside
/// <see cref="BeginModal"/> so opening them doesn't hide it.
/// </summary>
public interface IModalHost
{
    Window Owner { get; }

    IDisposable BeginModal();
}
