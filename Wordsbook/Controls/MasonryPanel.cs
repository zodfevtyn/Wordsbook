using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Wordsbook.Controls;

/// <summary>瀑布流面板：固定列宽、卡片高度自适应，每次放进当前最矮的一列。</summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(MasonryPanel),
        new PropertyMetadata(280d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(MasonryPanel),
        new PropertyMetadata(16d, OnLayoutPropertyChanged));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    readonly List<double> _itemHeights = [];
    readonly List<int> _itemColumns = [];
    int _columnCount = 1;
    double _itemWidth;
    double _usedSpacing;
    double _totalHeight;

    static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MasonryPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            _itemHeights.Clear();
            _itemColumns.Clear();
            _totalHeight = 0;
            return new Size(0, 0);
        }

        var spacing = Math.Max(0, Spacing);
        var minWidth = Math.Max(1, MinItemWidth);
        var width = availableSize.Width;
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0) width = minWidth;

        _columnCount = Math.Max(1, (int)Math.Floor((width + spacing) / (minWidth + spacing)));
        _itemWidth = Math.Max(1, (width - (_columnCount - 1) * spacing) / _columnCount);
        _usedSpacing = spacing;
        _itemHeights.Clear();
        _itemColumns.Clear();

        var heights = new double[_columnCount];
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(_itemWidth, double.PositiveInfinity));
            var height = child.DesiredSize.Height;

            var column = 0;
            for (var c = 1; c < _columnCount; c++)
                if (heights[c] < heights[column]) column = c;

            _itemColumns.Add(column);
            _itemHeights.Add(height);
            heights[column] += height + spacing;
        }

        _totalHeight = Math.Max(0, heights.Max() - spacing);
        return new Size(width, _totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_itemColumns.Count != Children.Count) return finalSize;

        var columnTops = new double[Math.Max(1, _columnCount)];
        for (var i = 0; i < Children.Count; i++)
        {
            var column = _itemColumns[i];
            var height = _itemHeights[i];
            var x = column * (_itemWidth + _usedSpacing);
            Children[i].Arrange(new Rect(x, columnTops[column], _itemWidth, height));
            columnTops[column] += height + _usedSpacing;
        }

        return new Size(finalSize.Width, _totalHeight);
    }
}
