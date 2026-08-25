using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Inventory.App;

/// <summary>시작 시 완전 강제 업데이트 — 취소·닫기 불가, 진행만 표시.</summary>
internal sealed class ForceUpdateProgressDialog : Window
{
    private readonly TextBlock _status;
    private bool _allowClose;

    public ForceUpdateProgressDialog(Window owner, string introMessage)
    {
        Owner = owner;
        Title = "필수 업데이트";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Closing += OnClosing;

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        root.Children.Add(new TextBlock
        {
            Text = introMessage,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
            Foreground = Application.Current?.TryFindResource("ClinicTextBodyBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            FontSize = 14,
            LineHeight = 22
        });

        _status = new TextBlock
        {
            Text = "업데이트 중...",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = Application.Current?.TryFindResource("ClinicAccentBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(31, 111, 115)),
            FontSize = 14,
            LineHeight = 22
        };
        root.Children.Add(_status);

        root.Children.Add(new TextBlock
        {
            Text = "창을 닫거나 취소할 수 없습니다.",
            Margin = new Thickness(0, 16, 0, 0),
            Foreground = Application.Current?.TryFindResource("ClinicTextMutedBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            FontSize = 12
        });

        Content = root;
    }

    public void SetProgress(string text)
    {
        var first = (text ?? "").Split('\n')[0];
        _status.Text = string.IsNullOrWhiteSpace(first) ? "업데이트 중..." : first;
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }
    }
}
