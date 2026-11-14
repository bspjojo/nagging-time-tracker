using System.Windows;
using System.Windows.Controls;

namespace NaggingTimeTracker.App.Windows;

/// <summary>
/// Asks for a new name. <c>tryRename</c> returns null on success (the dialog closes) or an
/// error to show (the dialog stays open).
/// </summary>
public partial class RenameDialog : Window
{
    private readonly Func<string, string?> _tryRename;

    public RenameDialog(string title, string currentName, Func<string, string?> tryRename)
    {
        InitializeComponent();
        Title = title;
        _tryRename = tryRename;
        NameBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OkButton.IsEnabled = NameBox.Text.Trim().Length > 0;
        ErrorText.Text = "";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var error = _tryRename(NameBox.Text);
        if (error is null)
        {
            DialogResult = true;
        }
        else
        {
            ErrorText.Text = error;
        }
    }
}
