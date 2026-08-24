using Inventory.Infrastructure;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Inventory.App.Views;

public sealed class ItemSearchBox
{
    public sealed record Suggestion(string Code, string Name, string StockLabel)
    {
        public string Display => $"{Code} · {Name} · 재고 {StockLabel}";
    }

    private readonly Func<string, IReadOnlyList<Suggestion>> _search;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly TextBox _input;
    private bool _suppressTextChanged;
    private bool _suppressListSelection;

    public ItemSearchBox(Func<string, IReadOnlyList<Suggestion>> search, double width = 220)
    {
        _search = search;
        SelectedCode = "";
        _input = new TextBox { Width = width, MinWidth = width };
        _list = new ListBox
        {
            MaxHeight = 220,
            BorderThickness = new Thickness(0),
            DisplayMemberPath = nameof(Suggestion.Display)
        };
        _popup = new Popup
        {
            PlacementTarget = _input,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = Application.Current?.TryFindResource("ClinicLineBrush") as Brush
                             ?? new SolidColorBrush(Color.FromRgb(226, 232, 234)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = _list
            }
        };

        _input.TextChanged += (_, _) =>
        {
            if (_suppressTextChanged)
            {
                return;
            }

            SelectedCode = "";
            ShowSuggestions();
        };
        _input.PreviewKeyDown += OnPreviewKeyDown;
        _input.LostFocus += (_, _) =>
        {
            if (!_popup.IsOpen)
            {
                CommitFirstOrClear();
            }
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_suppressListSelection)
            {
                return;
            }

            if (_list.SelectedItem is Suggestion picked)
            {
                ApplySuggestion(picked);
            }
        };
        _list.MouseDoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is Suggestion picked)
            {
                ApplySuggestion(picked);
                _popup.IsOpen = false;
            }
        };
    }

    public TextBox Input => _input;
    public string SelectedCode { get; private set; }
    public string TypedText => _input.Text.Trim();
    public event Action<Suggestion?>? SelectionChanged;

    /// <summary>검색 결과에서 코드/이름 정확 일치를 우선하고, 없으면 첫 항목.</summary>
    public static Suggestion? PreferExactMatch(IReadOnlyList<Suggestion> hits, string query)
    {
        if (hits.Count == 0)
        {
            return null;
        }

        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return hits[0];
        }

        var exactCode = hits.FirstOrDefault(s => s.Code.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (exactCode is not null)
        {
            return exactCode;
        }

        var exactName = hits.FirstOrDefault(s => s.Name.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (exactName is not null)
        {
            return exactName;
        }

        return hits[0];
    }

    private void ShowSuggestions()
    {
        var q = _input.Text.Trim();
        if (q.Length == 0)
        {
            _popup.IsOpen = false;
            return;
        }

        var hits = _search(q).Take(12).ToList();
        if (hits.Count == 0)
        {
            _popup.IsOpen = false;
            return;
        }

        _suppressListSelection = true;
        _list.ItemsSource = hits;
        _list.SelectedIndex = 0;
        _suppressListSelection = false;
        _popup.IsOpen = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_popup.IsOpen)
        {
            if (e.Key == Key.Down && _input.Text.Trim().Length > 0)
            {
                ShowSuggestions();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, Math.Max(0, _list.SelectedIndex + 1));
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_list.SelectedItem is Suggestion picked)
                {
                    ApplySuggestion(picked);
                }
                else
                {
                    CommitFirstOrClear();
                }

                _popup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void CommitFirstOrClear()
    {
        var q = _input.Text.Trim();
        if (q.Length == 0)
        {
            SelectedCode = "";
            SelectionChanged?.Invoke(null);
            return;
        }

        var hit = PreferExactMatch(_search(q).ToList(), q);
        if (hit is null)
        {
            SelectedCode = "";
            SelectionChanged?.Invoke(null);
            return;
        }

        ApplySuggestion(hit);
    }

    private void ApplySuggestion(Suggestion suggestion)
    {
        _suppressTextChanged = true;
        _input.Text = suggestion.Name;
        _suppressTextChanged = false;
        SelectedCode = suggestion.Code;
        SelectionChanged?.Invoke(suggestion);
    }

    public bool TryGetSelection(out Suggestion? suggestion)
    {
        if (string.IsNullOrWhiteSpace(SelectedCode))
        {
            CommitFirstOrClear();
        }

        if (string.IsNullOrWhiteSpace(SelectedCode))
        {
            suggestion = null;
            return false;
        }

        suggestion = _search(_input.Text.Trim()).FirstOrDefault(s => s.Code == SelectedCode)
                     ?? new Suggestion(SelectedCode, _input.Text.Trim(), "");
        return true;
    }

    public void SetSelection(string code, string name, string stockLabel = "")
    {
        _suppressTextChanged = true;
        _input.Text = name;
        _suppressTextChanged = false;
        SelectedCode = code;
        SelectionChanged?.Invoke(new Suggestion(code, name, stockLabel));
    }
}

public static class UiComponents
{
    public static Border PageStatusBanner(TextBlock target) => new()
    {
        Background = Application.Current?.TryFindResource("ClinicSelectBrush") as Brush
                     ?? new SolidColorBrush(Color.FromRgb(215, 232, 232)),
        BorderBrush = Application.Current?.TryFindResource("ClinicAccentBrush") as Brush
                      ?? new SolidColorBrush(Color.FromRgb(31, 111, 115)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 12),
        Visibility = Visibility.Collapsed,
        Child = target
    };

    public static void SetPageStatus(Border banner, TextBlock label, string? message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            banner.Visibility = Visibility.Collapsed;
            label.Text = "";
            return;
        }

        banner.Visibility = Visibility.Visible;
        label.Text = message;
        label.Foreground = isError
            ? Application.Current?.TryFindResource("ClinicDangerBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(192, 57, 43))
            : Application.Current?.TryFindResource("ClinicAccentBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(31, 111, 115));
        banner.Background = isError
            ? Application.Current?.TryFindResource("ClinicWarnSoftBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(255, 244, 224))
            : Application.Current?.TryFindResource("ClinicSelectBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(215, 232, 232));
        banner.BorderBrush = isError
            ? Application.Current?.TryFindResource("ClinicDangerBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(192, 57, 43))
            : Application.Current?.TryFindResource("ClinicAccentBrush") as Brush
              ?? new SolidColorBrush(Color.FromRgb(31, 111, 115));
    }

    public static StackPanel EmptyState(string title, string detail, Button? cta = null)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        if (cta is not null)
        {
            cta.Margin = new Thickness(0, 12, 0, 0);
            cta.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(cta);
        }

        return stack;
    }

    public static StackPanel LoadingHost(out ProgressBar bar, out TextBlock label)
    {
        bar = new ProgressBar { Height = 6, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 8) };
        label = new TextBlock { Text = "불러오는 중…", Foreground = Brushes.DimGray };
        return new StackPanel { Children = { bar, label } };
    }

    public static Button FilterChip(string label, bool active, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 56,
            Cursor = Cursors.Hand
        };
        var key = active ? "FilterChipActive" : "FilterChip";
        if (Application.Current?.TryFindResource(key) is Style style)
        {
            button.Style = style;
        }

        button.Click += click;
        return button;
    }

    public static ItemSearchBox.Suggestion StockSuggestion(StockSnapshot snap) =>
        new(snap.Code, snap.Name, snap.OnHand?.ToString("N3", CultureInfo.CurrentCulture) ?? "미설정");

    public static ItemSearchBox.Suggestion ItemSuggestion(Item item, decimal? onHand = null) =>
        new(item.Code, item.Name, onHand?.ToString("N3", CultureInfo.CurrentCulture) ?? "—");
}
