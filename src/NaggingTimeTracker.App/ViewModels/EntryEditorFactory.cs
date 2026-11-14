using System.Windows;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>Opens the modal entry editor. The entry is always loaded fresh from the database.</summary>
public sealed class EntryEditorFactory(
    IEntryRepository entries,
    TrackingService tracking,
    CatalogService catalog,
    TimeProvider time)
{
    /// <summary>Edits <paramref name="entryId"/>, or creates a manual entry when it's null. True when saved.</summary>
    public bool Show(long? entryId, Window? owner)
    {
        var original = entryId is { } id ? entries.GetById(id) : null;
        if (entryId is not null && original is null)
        {
            Message(owner, "This entry no longer exists.");
            return false;
        }

        var vm = new EntryEditorViewModel(original, entries, tracking, catalog, time);
        var dialog = new EntryEditorDialog(vm, this);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    internal static void Message(Window? owner, string text)
    {
        if (owner is null)
        {
            MessageBox.Show(text, "Nagging Time Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(owner, text, "Nagging Time Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
