using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;

namespace Quark;

public sealed class SimulatedUpdateDialog : Window
{
    public SimulatedUpdateDialog()
    {
        Title                 = "Quark - Simulated update";
        Width                 = 360;
        SizeToContent         = SizeToContent.Height;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = App.AppIcon;

        var ok = new Button
        {
            Content                    = "OK",
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background                 = new SolidColorBrush(Color.Parse("#42A5F5")),
            Foreground                 = Brushes.White,
        };
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin   = new Thickness(16),
            Spacing  = 12,
            Children =
            {
                new TextBlock
                {
                    Text         = "This is a simulated environment.\nNo actual update will be performed.",
                    TextWrapping = TextWrapping.Wrap,
                },
                ok,
            }
        };
    }
}
