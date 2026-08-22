using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Inventory.App;

internal sealed class UpdatePromptDialog : Window
{
    public bool ApplyNow { get; private set; }

    public UpdatePromptDialog(Window owner, string message)
    {
        Owner = owner;
        Title = "업데이트 안내";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 20, 24, 16),
            Foreground = Application.Current?.TryFindResource("ClinicTextBodyBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            FontSize = 14,
            LineHeight = 22
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 0, 24, 0)
        };

        var later = new Button
        {
            Content = "나중에",
            MinWidth = 96,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Application.Current?.TryFindResource("GhostButton") as Style
        };
        later.Click += (_, _) =>
        {
            ApplyNow = false;
            DialogResult = false;
            Close();
        };

        var now = new Button
        {
            Content = "지금 업데이트",
            MinWidth = 112,
            Style = Application.Current?.TryFindResource("PrimaryButton") as Style
        };
        now.Click += (_, _) =>
        {
            ApplyNow = true;
            DialogResult = true;
            Close();
        };

        buttons.Children.Add(later);
        buttons.Children.Add(now);
        root.Children.Add(buttons);
        Content = root;
    }
}
