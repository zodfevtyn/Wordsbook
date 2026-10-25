using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Wordsbook.ViewModels;

namespace Wordsbook.Controls;

/// <summary>
/// 月度热力图。42 个格子只创建一次，之后仅改属性，避免每次切月都重建可视树。
/// </summary>
public sealed partial class HeatMapView : UserControl
{
    const int Rows = 6;
    const int Cols = 7;
    const double Cell = 15;
    const double Gap = 4;

    static readonly SolidColorBrush EmptyCell = new(Windows.UI.Color.FromArgb(0x3C, 0x8E, 0x8E, 0x93));
    static readonly SolidColorBrush HotCell = new(Windows.UI.Color.FromArgb(0xFF, 0x0A, 0x84, 0xFF));
    static readonly SolidColorBrush OutOfRange = new(Windows.UI.Color.FromArgb(0x12, 0x80, 0x80, 0x80));
    static readonly SolidColorBrush TodayRing = new(Windows.UI.Color.FromArgb(0xCC, 0x9E, 0x9E, 0xA4));

    readonly Border?[] _pool = new Border?[Rows * Cols];
    bool _built;

    /// <summary>切换月份时向外部分发增量，由主窗口接到 ViewModel。</summary>
    public Action<int>? StepRequested { get; set; }

    public HeatMapView() => InitializeComponent();

    public string Month
    {
        get => MonthLabel.Text;
        set => MonthLabel.Text = value;
    }

    public void Render(IReadOnlyList<HeatCell> cells, DateTime today)
    {
        EnsureBuilt();

        for (var i = 0; i < _pool.Length; i++)
        {
            var b = _pool[i]!;
            if (i >= cells.Count)
            {
                b.Visibility = Visibility.Collapsed;
                continue;
            }

            var c = cells[i];
            b.Visibility = Visibility.Visible;
            b.Opacity = c.InMonth ? 1d : 0.35d;
            b.Background = !c.InMonth ? EmptyCell : c.IsFuture ? OutOfRange : HotCell;
            b.BorderBrush = c.Date == today ? TodayRing : null;
            b.BorderThickness = c.Date == today ? new Thickness(1.5) : new Thickness(0);

            switch (c.Count)
            {
                case 0: b.Background = c.InMonth ? EmptyCell : OutOfRange; break;
                case <= 2: b.Opacity *= 0.3; break;
                case <= 5: b.Opacity *= 0.5; break;
                case <= 9: b.Opacity *= 0.75; break;
            }
        }
    }

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        for (var c = 0; c < Cols; c++)
        {
            WeekHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Cell + Gap) });
            CellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Cell + Gap) });
        }
        for (var r = 0; r < Rows; r++) CellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Cell + Gap) });

        string[] names = ["M", "T", "W", "T", "F", "S", "S"];
        for (var c = 0; c < Cols; c++)
        {
            var t = new TextBlock
            {
                Text = names[c],
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, Gap, 0),
                Foreground = TodayRing,
            };
            Grid.SetColumn(t, c);
            WeekHeader.Children.Add(t);
        }

        for (var i = 0; i < _pool.Length; i++)
        {
            var b = new Border
            {
                Width = Cell,
                Height = Cell,
                Margin = new Thickness(0, 0, Gap, Gap),
                CornerRadius = new CornerRadius(4),
                Background = EmptyCell,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(b, i / Cols);
            Grid.SetColumn(b, i % Cols);
            CellGrid.Children.Add(b);
            _pool[i] = b;
        }
    }

    void OnPrev(object sender, RoutedEventArgs e) => StepRequested?.Invoke(-1);

    void OnNext(object sender, RoutedEventArgs e) => StepRequested?.Invoke(1);
}
