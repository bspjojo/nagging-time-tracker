using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.App.ViewModels;

public sealed record CatalogItemRow(long Id, string Name, bool Archived)
{
    public string Label => Archived ? $"{Name} (archived)" : Name;
}

/// <summary>The Customers or Task types list in Settings: rename and archive/unarchive.</summary>
public sealed partial class CatalogListViewModel : ObservableObject
{
    private readonly string _kind;
    private readonly Func<IEnumerable<ICatalogItem>> _getAll;
    private readonly Func<long, string, RenameResult> _rename;
    private readonly Func<long, bool, bool> _setArchived;

    public CatalogListViewModel(
        string kind,
        string automationPrefix,
        Func<IEnumerable<ICatalogItem>> getAll,
        Func<long, string, RenameResult> rename,
        Func<long, bool, bool> setArchived)
    {
        _kind = kind;
        AutomationPrefix = automationPrefix;
        _getAll = getAll;
        _rename = rename;
        _setArchived = setArchived;
        Reload();
    }

    /// <summary>"Customer" or "TaskType": the stem of this tab's AutomationIds.</summary>
    public string AutomationPrefix { get; }

    /// <summary>The window, for owning dialogs.</summary>
    public Window? Owner { get; set; }

    public ObservableCollection<CatalogItemRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(ToggleArchiveCommand))]
    [NotifyPropertyChangedFor(nameof(ArchiveLabel))]
    private CatalogItemRow? _selected;

    [ObservableProperty]
    private bool _showArchived = true;

    public string ArchiveLabel => Selected is { Archived: true } ? "_Unarchive" : "_Archive";

    partial void OnShowArchivedChanged(bool value) => Reload();

    public void Reload()
    {
        var selectedId = Selected?.Id;
        Items.Clear();
        foreach (var item in _getAll().Where(i => ShowArchived || !i.Archived))
        {
            Items.Add(new CatalogItemRow(item.Id, item.Name, item.Archived));
        }

        Selected = Items.FirstOrDefault(i => i.Id == selectedId);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rename()
    {
        var row = Selected!;
        var dialog = new RenameDialog($"Rename {_kind}", row.Name, newName => _rename(row.Id, newName) switch
        {
            RenameResult.NameTaken => $"A {_kind} named '{newName.Trim()}' already exists.",
            _ => null,
        });
        if (Owner is { IsVisible: true })
        {
            dialog.Owner = Owner;
        }

        dialog.ShowDialog();
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleArchive()
    {
        var row = Selected!;
        _setArchived(row.Id, !row.Archived);
        Reload();
    }

    private bool HasSelection() => Selected is not null;
}
