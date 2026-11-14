using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Entries;
using NaggingTimeTracker.Core.Export;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.ViewModels;

public sealed partial class ExportViewModel : ObservableObject
{
    private readonly IEntryRepository _entries;
    private readonly TimeProvider _time;

    public ExportViewModel(IEntryRepository entries, CatalogService catalog, TimeProvider time)
    {
        _entries = entries;
        _time = time;
        Filter = new FilterBarViewModel(catalog, time, "ExportWindow");
        Filter.Changed += (_, _) => Refresh();
        Filter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilterBarViewModel.FilterError))
            {
                Refresh();
            }
        };
        Refresh();
    }

    /// <summary>The window, for owning the save dialog.</summary>
    public Window? Owner { get; set; }

    public FilterBarViewModel Filter { get; }

    [ObservableProperty]
    private bool _includeRunning;

    [ObservableProperty]
    private string _previewText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _canExport;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand), nameof(OpenFolderCommand))]
    private string? _lastExportPath;

    [ObservableProperty]
    private string? _resultText;

    public bool HasResult => LastExportPath is not null;

    /// <summary>Pre-fills the filters (from the Entries window); keeps IncludeRunning.</summary>
    public void ApplyPrefill(FilterState? state)
    {
        if (state is not null)
        {
            Filter.Apply(state);
        }
    }

    public void Detach() => Filter.Detach();

    partial void OnIncludeRunningChanged(bool value) => Refresh();

    /// <summary>Re-runs the query and updates the preview count.</summary>
    public void Refresh()
    {
        // An error from an earlier export attempt no longer applies once the selection changes.
        Error = null;
        Filter.RefreshOptions();
        var rows = Query(out var excludedRunning);
        if (rows is null)
        {
            PreviewText = "";
            CanExport = false;
            return;
        }

        PreviewText = rows.Count switch
        {
            0 => "No entries match these filters.",
            1 => "1 entry will be exported.",
            _ => string.Create(CultureInfo.CurrentCulture, $"{rows.Count} entries will be exported."),
        };
        if (rows.Count > 0 && excludedRunning > 0)
        {
            PreviewText = PreviewText.TrimEnd('.')
                + string.Create(CultureInfo.CurrentCulture, $" ({excludedRunning} running not included).");
        }

        CanExport = rows.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        Error = null;
        var rows = Query(out _);
        if (rows is null || rows.Count == 0 || Filter.Current is not { } state)
        {
            Refresh();
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = state.DefaultFileName(),
            DefaultExt = ".csv",
            AddExtension = true,
            Filter = "CSV files (*.csv)|*.csv",
            OverwritePrompt = true,
        };
        var ok = Owner is null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
        if (ok != true)
        {
            return;
        }

        var path = dialog.FileName;
        var temp = path + ".tmp";
        try
        {
            // Write next to the target, then replace it: a failure can't leave a truncated file.
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                CsvExporter.Write(stream, rows, _time.NowUtc(), _time.LocalTimeZone);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            Error = $"Couldn't write the file: {ex.Message}";
            return;
        }

        LastExportPath = path;
        ResultText = rows.Count == 1
            ? $"Exported 1 entry to {Path.GetFileName(path)}."
            : string.Create(CultureInfo.CurrentCulture, $"Exported {rows.Count} entries to {Path.GetFileName(path)}.");
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void OpenFile() =>
        Process.Start(new ProcessStartInfo(LastExportPath!) { UseShellExecute = true });

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void OpenFolder() =>
        Process.Start("explorer.exe", $"/select,\"{LastExportPath}\"");

    /// <summary>The rows to export, or null while the filters are invalid.</summary>
    private List<EntryView>? Query(out int excludedRunning)
    {
        excludedRunning = 0;
        if (Filter.Current is not { } state)
        {
            return null;
        }

        var all = _entries.QueryViews(state.ToEntryFilter(_time.LocalTimeZone));
        if (IncludeRunning)
        {
            return [.. all];
        }

        excludedRunning = all.Count(v => v.Entry.StopUtc is null);
        return all.Where(v => v.Entry.StopUtc is not null).ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
