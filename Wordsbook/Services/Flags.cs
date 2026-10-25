using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Wordsbook.Services;

/// <summary>
/// Windows 的字体栈不会把 🇳🇱 这类区域指示符合成国旗图形，只会渲染成两个字母方块，
/// 所以这里按 ISO 码用矢量色块直接画小旗子；没有收录的国家退回字母徽标。
/// </summary>
public static class Flags
{
    enum Direction { Horizontal, Vertical }

    sealed record Spec(Direction Dir, string[] Colors, string Overlay = "", string OverlayColor = "");

    static readonly Dictionary<string, Spec> Table = new()
    {
        ["nl"] = H("#AE1C28", "#FFFFFF", "#21468B"),
        ["fr"] = V("#002395", "#FFFFFF", "#ED2939"),
        ["de"] = H("#000000", "#DD0000", "#FFCE00"),
        ["it"] = V("#009246", "#F1F2F1", "#CE2B37"),
        ["ru"] = H("#FFFFFF", "#0039A6", "#D52B1E"),
        ["es"] = H("#AA151B", "#F1BF00", "#AA151B"),
        ["pt"] = H("#006600", "#FF0000"),
        ["pl"] = H("#FFFFFF", "#DC143C"),
        ["id"] = H("#FFFFFF", "#CC0000"),
        ["hu"] = H("#CD2A3E", "#FFFFFF", "#437012"),
        ["ro"] = V("#002B7F", "#FCD116", "#CE1126"),
        ["bg"] = H("#FFFFFF", "#00966E", "#D62612"),
        ["at"] = H("#ED2939", "#FFFFFF", "#ED2939"),
        ["be"] = V("#000000", "#FAE042", "#ED2939"),
        ["ua"] = H("#0057B7", "#FFD700"),
        ["ee"] = H("#0072CE", "#000000", "#FFFFFF"),
        ["lv"] = H("#9E3039", "#FFFFFF", "#9E3039"),
        ["lt"] = H("#FDB913", "#006A44", "#C1272D"),
        ["lu"] = H("#ED2939", "#FFFFFF", "#00A1DE"),
        ["hr"] = H("#FF0000", "#FFFFFF", "#171796"),
        ["si"] = H("#FFFFFF", "#005DA4", "#ED1C24"),
        ["sk"] = H("#FFFFFF", "#0B4EA2", "#EE1C26"),
        ["rs"] = H("#C6363C", "#0C4076", "#FFFFFF"),
        ["ie"] = V("#169B62", "#FFFFFF", "#FF883E"),
        ["ng"] = V("#008751", "#FFFFFF", "#008751"),
        ["pe"] = V("#D91023", "#FFFFFF", "#D91023"),
        ["td"] = V("#002664", "#FECB00", "#C60C0C"),
        ["ca"] = V("#FF0000", "#FFFFFF", "#FF0000"),
        ["se"] = H("#005293", "#FECC00", "#005293"),
        ["jp"] = new(Direction.Horizontal, ["#FFFFFF"], "circle", "#BC002D"),
        ["bd"] = new(Direction.Horizontal, ["#006A4E"], "circle", "#F42A41"),
        ["ch"] = new(Direction.Vertical, ["#D52B1E"], "cross", "#FFFFFF"),
        ["se2"] = new(Direction.Horizontal, ["#005293"], "cross", "#FECB00"),
        ["fi"] = new(Direction.Horizontal, ["#FFFFFF"], "cross", "#003580"),
        ["no"] = new(Direction.Horizontal, ["#BA0C2F"], "cross", "#FFFFFF"),
        ["is"] = new(Direction.Horizontal, ["#02529C"], "cross", "#FFFFFF"),
        ["dk"] = new(Direction.Horizontal, ["#C60C30"], "cross", "#FFFFFF"),
        ["gb"] = new(Direction.Horizontal, ["#012169"], "cross", "#FFFFFF"),
        ["cn"] = new(Direction.Horizontal, ["#DE2910"], "star", "#FFDE00"),
        ["tr"] = new(Direction.Horizontal, ["#E30A17"], "star", "#FFFFFF"),
        ["vn"] = new(Direction.Horizontal, ["#DA251D"], "star", "#FFFF00"),
        ["kr"] = new(Direction.Horizontal, ["#FFFFFF"], "circle", "#CD2E3A"),
    };

    static Spec H(params string[] colors) => new(Direction.Horizontal, colors);
    static Spec V(params string[] colors) => new(Direction.Vertical, colors);

    public static FrameworkElement Create(string code, double width = 20)
    {
        var key = (code ?? "").Trim().ToLowerInvariant();
        var height = Math.Round(width * 0.66);
        var radius = new CornerRadius(Math.Max(2, height * 0.16));

        if (!Table.TryGetValue(key, out var spec)) return Chip(key, width, height);

        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = radius,
            Child = BuildBody(spec, width, height),
        };
    }

    static UIElement BuildBody(Spec spec, double width, double height)
    {
        var grid = new Grid();
        var n = spec.Colors.Length;

        for (var i = 0; i < n; i++)
        {
            var band = new Border { Background = Paint(spec.Colors[i]) };
            if (spec.Dir == Direction.Horizontal)
            {
                if (grid.RowDefinitions.Count == 0)
                    for (var r = 0; r < n; r++) grid.RowDefinitions.Add(new RowDefinition { Height = Star() });
                Grid.SetRow(band, i);
            }
            else
            {
                if (grid.ColumnDefinitions.Count == 0)
                    for (var c = 0; c < n; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Star() });
                Grid.SetColumn(band, i);
            }
            grid.Children.Add(band);
        }

        switch (spec.Overlay)
        {
            case "circle":
                grid.Children.Add(new Ellipse
                {
                    Width = height * 0.56,
                    Height = height * 0.56,
                    Fill = Paint(spec.OverlayColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                break;

            case "star":
                grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
                {
                    Data = StarPath(width * 0.30, height * 0.13, width * 0.30, height * 0.5),
                    Fill = Paint(spec.OverlayColor),
                });
                break;

            case "cross":
                var vertical = new Border
                {
                    Background = Paint(spec.OverlayColor),
                    Width = Math.Max(2, width * 0.18),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(width * 0.26, 0, 0, 0),
                };
                var horizontal = new Border
                {
                    Background = Paint(spec.OverlayColor),
                    Height = Math.Max(2, height * 0.22),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                grid.Children.Add(vertical);
                grid.Children.Add(horizontal);
                break;
        }

        return grid;
    }

    static GridLength Star() => new(1, GridUnitType.Star);

    static Geometry StarPath(double outer, double inner, double cx, double cy)
    {
        var fig = new PathFigure { StartPoint = new Windows.Foundation.Point(cx, cy - outer), IsClosed = true };
        for (var i = 1; i < 10; i++)
        {
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var radius = i % 2 == 0 ? outer : inner;
            fig.Segments.Add(new LineSegment
            {
                Point = new Windows.Foundation.Point(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle)),
            });
        }
        return new PathGeometry { Figures = [fig] };
    }

    static FrameworkElement Chip(string code, double width, double height)
    {
        var text = code.Length >= 2 ? code[..2].ToUpperInvariant() : "··";
        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    static SolidColorBrush Paint(string hex)
    {
        var s = hex.TrimStart('#');
        return new SolidColorBrush(ColorHelper.FromArgb(
            0xFF,
            Convert.ToByte(s.Substring(0, 2), 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16)));
    }
}
