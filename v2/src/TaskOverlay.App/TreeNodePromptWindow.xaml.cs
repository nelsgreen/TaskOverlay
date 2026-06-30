using System.Windows;
using System.Windows.Input;

namespace TaskOverlay.App;

public partial class TreeNodePromptWindow : Window
{
    private TreeNodePromptWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        TitleInput.Text = initialValue;
        Loaded += (_, _) =>
        {
            TitleInput.Focus();
            TitleInput.SelectAll();
        };
    }

    public string Value => TitleInput.Text.Trim();

    public static bool TryShow(
        Window owner,
        string title,
        string prompt,
        string initialValue,
        out string value)
    {
        var dialog = new TreeNodePromptWindow(title, prompt, initialValue)
        {
            Owner = owner
        };
        var confirmed = dialog.ShowDialog() == true;
        value = dialog.Value;
        return confirmed && value.Length > 0;
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            return;
        }

        DialogResult = true;
    }

    private void TitleInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Value.Length > 0)
        {
            DialogResult = true;
        }
    }
}
