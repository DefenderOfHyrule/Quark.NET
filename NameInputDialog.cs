using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Quark;

public sealed class NameInputDialog : Window
{
    private readonly TextBox _input;

    public NameInputDialog(string prompt)
    {
        Title        = "Quark - Add path";
        Width        = 360;
        SizeToContent = SizeToContent.Height;
        CanResize    = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = App.AppIcon;

        _input = new TextBox { Watermark = "e.g. NSPs", Margin = new Thickness(0, 0, 0, 12) };

        var ok = new Button
        {
            Content             = "Add",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Avalonia.Media.Brushes.CornflowerBlue,
        };
        ok.Click += OnOk;

        var cancel = new Button
        {
            Content             = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin  = new Thickness(16),
            Spacing = 0,
            Children =
            {
                new Label { Content = prompt, Margin = new Thickness(0, 0, 0, 6) },
                _input,
                ok,
                cancel
            }
        };
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        string? name = _input.Text?.Trim();
        Close(string.IsNullOrEmpty(name) ? null : name);
    }
}
