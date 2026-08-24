using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Inventory.App;

internal sealed class ExcelExportScopeDialog : Window
{
    public enum ScopeKind
    {
        All,
        Selected
    }

    private sealed class ChoiceRow
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
    }

    public ScopeKind Scope { get; private set; } = ScopeKind.All;

    public IReadOnlyList<string> SelectedKeys { get; private set; } = [];

    public ExcelExportScopeDialog(
        Window owner,
        string title,
        string allCaption,
        string selectCaption,
        IReadOnlyList<(string Key, string Label)> choices,
        IReadOnlyList<string>? preselectedKeys = null)
    {
        Owner = owner;
        Title = title;
        Width = 460;
        Height = Math.Min(560, 220 + choices.Count * 22);
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Background = Brushes.White;

        var initialSelection = new HashSet<string>(preselectedKeys ?? [], StringComparer.OrdinalIgnoreCase);
        var preferSelected = initialSelection.Count > 0;
        var allRows = choices
            .Select(c => new ChoiceRow { Key = c.Key, Label = c.Label })
            .OrderBy(c => c.Label, StringComparer.Ordinal)
            .ToList();

        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        root.Children.Add(new TextBlock
        {
            Text = "내보낼 범위를 선택하세요.",
            Margin = new Thickness(24, 20, 24, 12),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });

        var allRadio = new RadioButton
        {
            Content = allCaption,
            IsChecked = !preferSelected,
            Margin = new Thickness(24, 0, 24, 6)
        };
        var selectRadio = new RadioButton
        {
            Content = selectCaption,
            IsChecked = preferSelected,
            Margin = new Thickness(24, 0, 24, 8)
        };
        root.Children.Add(allRadio);
        root.Children.Add(selectRadio);

        var search = new TextBox
        {
            Margin = new Thickness(24, 0, 24, 6),
            MinWidth = 200
        };
        var searchHint = new TextBlock
        {
            Text = "목록 검색",
            FontSize = 12,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(24, 0, 24, 4)
        };

        var list = new ListBox
        {
            SelectionMode = SelectionMode.Extended,
            DisplayMemberPath = nameof(ChoiceRow.Label),
            Margin = new Thickness(24, 0, 24, 8),
            MinHeight = 180,
            MaxHeight = 280,
            IsEnabled = false
        };

        var selectActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(24, 0, 24, 12),
            IsEnabled = false
        };
        var selectAll = new Button
        {
            Content = "전체 선택",
            MinWidth = 88,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Application.Current?.TryFindResource("GhostButton") as Style
        };
        var clearAll = new Button
        {
            Content = "선택 해제",
            MinWidth = 88,
            Style = Application.Current?.TryFindResource("GhostButton") as Style
        };
        selectActions.Children.Add(selectAll);
        selectActions.Children.Add(clearAll);

        var countHint = new TextBlock
        {
            Margin = new Thickness(24, 0, 24, 12),
            FontSize = 12,
            Foreground = Brushes.DimGray,
            Text = $"선택 가능 {allRows.Count:N0}건"
        };

        void RenderList()
        {
            var keepSelected = list.SelectedItems.OfType<ChoiceRow>()
                .Select(r => r.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (keepSelected.Count == 0)
            {
                keepSelected = initialSelection;
            }

            var q = search.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(q)
                ? allRows
                : allRows.Where(r =>
                        r.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            list.ItemsSource = filtered;
            list.UnselectAll();
            foreach (var row in filtered.Where(r => keepSelected.Contains(r.Key)))
            {
                list.SelectedItems.Add(row);
            }
        }

        void SetSelectMode(bool enabled)
        {
            list.IsEnabled = enabled;
            search.IsEnabled = enabled;
            searchHint.IsEnabled = enabled;
            selectActions.IsEnabled = enabled;
            countHint.Text = enabled
                ? $"선택 {list.SelectedItems.Count:N0}건 · 목록 {allRows.Count:N0}건"
                : $"선택 가능 {allRows.Count:N0}건";
        }

        allRadio.Checked += (_, _) => SetSelectMode(false);
        selectRadio.Checked += (_, _) => SetSelectMode(true);
        search.TextChanged += (_, _) => RenderList();
        list.SelectionChanged += (_, _) =>
        {
            if (selectRadio.IsChecked == true)
            {
                countHint.Text = $"선택 {list.SelectedItems.Count:N0}건 · 목록 {allRows.Count:N0}건";
            }
        };
        selectAll.Click += (_, _) =>
        {
            list.SelectAll();
        };
        clearAll.Click += (_, _) =>
        {
            list.UnselectAll();
        };

        root.Children.Add(searchHint);
        root.Children.Add(search);
        root.Children.Add(list);
        root.Children.Add(selectActions);
        root.Children.Add(countHint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 0, 24, 0)
        };
        var cancel = new Button
        {
            Content = "취소",
            MinWidth = 96,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Application.Current?.TryFindResource("GhostButton") as Style
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var confirm = new Button
        {
            Content = "다음",
            MinWidth = 96,
            Style = Application.Current?.TryFindResource("PrimaryButton") as Style
        };
        confirm.Click += (_, _) =>
        {
            if (selectRadio.IsChecked == true && list.SelectedItems.Count == 0)
            {
                MessageBox.Show("내보낼 항목을 하나 이상 선택하세요.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Scope = selectRadio.IsChecked == true ? ScopeKind.Selected : ScopeKind.All;
            SelectedKeys = list.SelectedItems.OfType<ChoiceRow>().Select(r => r.Key).Distinct(StringComparer.Ordinal).ToList();
            DialogResult = true;
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        root.Children.Add(buttons);
        Content = root;

        RenderList();
        SetSelectMode(preferSelected);
    }
}
