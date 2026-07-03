using System;
using System.Linq;
using System.Windows.Controls;
using TaskOverlay.Core;

namespace TaskOverlay.App;

public partial class ReminderPresetSelector : UserControl
{
    private bool _suppressSelectionChanged;

    public ReminderPresetSelector()
    {
        InitializeComponent();
        PresetListBox.ItemsSource = TaskAttentionUiOptions.CompactReminderPresets;
        SelectPreset(ReminderPreset.None);
    }

    public event EventHandler? SelectedPresetChanged;

    public ReminderPreset? SelectedPreset =>
        PresetListBox.SelectedItem is ReminderPresetOption option
            ? option.Value
            : null;

    public void SelectPreset(ReminderPreset preset)
    {
        var option = TaskAttentionUiOptions.CompactReminderPresets
            .FirstOrDefault(item => item.Value == preset);
        SetSelectedItem(option);
    }

    public void ClearSelection()
    {
        SetSelectedItem(null);
    }

    private void SetSelectedItem(ReminderPresetOption? option)
    {
        _suppressSelectionChanged = true;
        try
        {
            PresetListBox.SelectedItem = option;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void PresetListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionChanged)
        {
            SelectedPresetChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
