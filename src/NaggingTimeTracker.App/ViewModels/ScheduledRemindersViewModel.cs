using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Reminders;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>A row of the Reminders tab; toggling <see cref="Enabled"/> saves at once.</summary>
public sealed partial class ReminderRowViewModel : ObservableObject
{
    private readonly Action<long, bool> _setEnabled;

    public ReminderRowViewModel(ScheduledReminder r, Action<long, bool> setEnabled)
    {
        Reminder = r;
        _setEnabled = setEnabled;
        _enabled = r.Enabled;
    }

    public ScheduledReminder Reminder { get; }

    public long Id => Reminder.Id;

    public string Time => Reminder.TimeLocal.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string Days => DaysMaskText.Format(Reminder.DaysMask);

    public string Title => Reminder.Title;

    public string AccessibleName => $"{Time} {Days}: {Title}{(Enabled ? "" : " (off)")}";

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value) => _setEnabled(Id, value);
}

/// <summary>The Reminders tab: custom reminders, saved through <see cref="ScheduledReminderService"/>.</summary>
public sealed partial class ScheduledRemindersViewModel : ObservableObject
{
    private readonly ScheduledReminderService _service;
    private readonly TimeProvider _time;

    public ScheduledRemindersViewModel(ScheduledReminderService service, TimeProvider time)
    {
        _service = service;
        _time = time;
        Reload();
    }

    /// <summary>The window, for owning dialogs.</summary>
    public Window? Owner { get; set; }

    public ObservableCollection<ReminderRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand))]
    private ReminderRowViewModel? _selected;

    public void Reload()
    {
        var selectedId = Selected?.Id;
        Rows.Clear();
        foreach (var r in _service.GetAll())
        {
            Rows.Add(new ReminderRowViewModel(r, (id, on) => _service.SetEnabled(id, on)));
        }

        Selected = Rows.FirstOrDefault(r => r.Id == selectedId);
    }

    [RelayCommand]
    private void Add() => OpenEditor(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        // Re-read the stored row: the row's snapshot goes stale when its On box is toggled (that
        // saves without reloading), and the editor writes back the fields it doesn't show.
        if (_service.GetById(Selected!.Id) is { } current)
        {
            OpenEditor(current);
        }
        else
        {
            Reload();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        var row = Selected!;
        var text = $"Delete the reminder '{row.Title}'?";
        var answer = Owner is null
            ? MessageBox.Show(text, "Delete reminder", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(Owner, text, "Delete reminder", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
        {
            _service.Delete(row.Id);
            Reload();
        }
    }

    private bool HasSelection() => Selected is not null;

    private void OpenEditor(ScheduledReminder? existing)
    {
        var dialog = new ReminderEditorDialog(existing, _service, _time);
        if (Owner is { IsVisible: true })
        {
            dialog.Owner = Owner;
        }

        if (dialog.ShowDialog() == true)
        {
            Reload();
            Selected = Rows.FirstOrDefault(r => r.Id == dialog.SavedId);
        }
    }
}
