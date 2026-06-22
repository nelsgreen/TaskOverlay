using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskOverlay.App;

internal sealed record TreeMoveTarget(Guid Id, string Label);

internal static class TreeManagerDialogs
{
    public static string? PromptForText(
        Window owner,
        string title,
        string prompt,
        string initialValue = "")
    {
        var dialog = new TextPromptWindow(title, prompt, initialValue)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public static Guid? ChooseMoveTarget(
        Window owner,
        string nodeTitle,
        IReadOnlyList<TreeMoveTarget> targets)
    {
        var dialog = new MoveTargetWindow(nodeTitle, targets)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true ? dialog.SelectedTargetId : null;
    }

    private sealed class TextPromptWindow : Window
    {
        private readonly TextBox _textBox;
        private readonly TextBlock _validation;

        public TextPromptWindow(string title, string prompt, string initialValue)
        {
            Title = title;
            Width = 420;
            Height = 190;
            MinWidth = 340;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(24, 27, 34));

            _textBox = new TextBox
            {
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Text = initialValue
            };
            _validation = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                FontSize = 11
            };

            var okButton = new Button
            {
                MinWidth = 76,
                Padding = new Thickness(10, 6, 10, 6),
                Content = "OK",
                IsDefault = true
            };
            okButton.Click += (_, _) => Accept();
            var cancelButton = new Button
            {
                MinWidth = 76,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Content = "Cancel",
                IsCancel = true
            };

            var buttons = new StackPanel
            {
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Orientation = Orientation.Horizontal
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var content = new StackPanel
            {
                Margin = new Thickness(22)
            };
            content.Children.Add(new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245)),
                FontSize = 13,
                Text = prompt,
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(_textBox);
            content.Children.Add(_validation);
            content.Children.Add(buttons);
            Content = content;

            Loaded += (_, _) =>
            {
                _textBox.Focus();
                _textBox.SelectAll();
            };
        }

        public string Value => _textBox.Text.Trim();

        private void Accept()
        {
            if (string.IsNullOrWhiteSpace(_textBox.Text))
            {
                _validation.Text = "A name is required.";
                _textBox.Focus();
                return;
            }

            DialogResult = true;
        }
    }

    private sealed class MoveTargetWindow : Window
    {
        private readonly ComboBox _targets;

        public MoveTargetWindow(
            string nodeTitle,
            IReadOnlyList<TreeMoveTarget> targets)
        {
            Title = "Move to...";
            Width = 500;
            Height = 210;
            MinWidth = 400;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(24, 27, 34));

            _targets = new ComboBox
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(7, 5, 7, 5),
                ItemsSource = targets,
                DisplayMemberPath = nameof(TreeMoveTarget.Label),
                SelectedIndex = 0
            };

            var moveButton = new Button
            {
                MinWidth = 76,
                Padding = new Thickness(10, 6, 10, 6),
                Content = "Move",
                IsDefault = true
            };
            moveButton.Click += (_, _) => DialogResult = true;
            var cancelButton = new Button
            {
                MinWidth = 76,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Content = "Cancel",
                IsCancel = true
            };

            var buttons = new StackPanel
            {
                Margin = new Thickness(0, 18, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Orientation = Orientation.Horizontal
            };
            buttons.Children.Add(moveButton);
            buttons.Children.Add(cancelButton);

            var content = new StackPanel
            {
                Margin = new Thickness(22)
            };
            content.Children.Add(new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245)),
                FontSize = 13,
                Text = $"Move \"{nodeTitle}\" under:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(_targets);
            content.Children.Add(buttons);
            Content = content;
        }

        public Guid? SelectedTargetId =>
            (_targets.SelectedItem as TreeMoveTarget)?.Id;
    }
}
