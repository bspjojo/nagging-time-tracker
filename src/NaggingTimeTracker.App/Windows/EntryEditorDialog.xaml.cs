using System.Windows;
using NaggingTimeTracker.App.ViewModels;

namespace NaggingTimeTracker.App.Windows;

/// <summary>Modal editor for one entry. Save stays enabled; blocking errors keep it open.</summary>
public partial class EntryEditorDialog : Window
{
    private readonly EntryEditorViewModel _vm;
    private readonly EntryEditorFactory _factory;

    public EntryEditorDialog(EntryEditorViewModel vm, EntryEditorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(vm);
        InitializeComponent();
        _vm = vm;
        _factory = factory;
        DataContext = vm;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        switch (_vm.Save())
        {
            case EditorSaveOutcome.Saved:
                DialogResult = true;
                break;
            case EditorSaveOutcome.EntryGone:
                EntryEditorFactory.Message(this, "This entry was deleted while you were editing it.");
                DialogResult = false;
                break;
        }
    }

    private void ContinuedFrom_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ContinuedFromId is { } sourceId)
        {
            _factory.Show(sourceId, this);
            _vm.Revalidate();
        }
    }
}
