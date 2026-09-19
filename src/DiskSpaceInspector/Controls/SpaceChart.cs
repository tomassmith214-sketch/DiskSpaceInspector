using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DiskSpaceInspector.Controls;

public sealed class SpaceChart : FrameworkElement
{
    public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(nameof(Entries), typeof(IReadOnlyList<ChartEntry>), typeof(SpaceChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ChartEntry>? Entries { get => (IReadOnlyList<ChartEntry>?)GetValue(EntriesProperty); set => SetValue(EntriesProperty, value); }
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(string), typeof(SpaceChart), new FrameworkPropertyMetadata("Treemap", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public static readonly DependencyProperty FocusTextProperty = DependencyProperty.Register(nameof(FocusText), typeof(string), typeof(SpaceChart), new PropertyMetadata("Наведите на цвет, чтобы увидеть его название и размер."));
    public string FocusText { get => (string)GetValue(FocusTextProperty); private set => SetValue(FocusTextProperty, value); }
    private readonly List<(Geometry Shape, ChartEntry Entry)> hits = [];
    private ChartEntry? hover;
    public event Action<ChartEntry>? EntryClicked;
    public SpaceChart()
    {
        SnapsToDevicePixels = true;
        MouseMove += (_, e) =>
        {
            var point = e.GetPosition(this);
            var entry = hits.LastOrDefault(x => x.Shape.FillContains(point)).Entry;
            if (ReferenceEquals(entry, hover)) return;
            hover = entry; ToolTip = entry?.Tooltip;
            FocusText = entry is null ? "Наведите на цвет, чтобы увидеть его название и размер." : $"{entry.Name} · {Core.SizeFormatter.Format(entry.Size)}";
            Cursor = entry?.Item is null ? Cursors.Arrow : Cursors.Hand;
            InvalidateVisual();
        };
        MouseLeave += (_, _) => { hover = null; ToolTip = null; FocusText = "Наведите на цвет, чтобы увидеть его название и размер."; Cursor = Cursors.Arrow; InvalidateVisual(); };
        MouseLeftButtonUp += (_, e) =>
        {
            var entry = hits.LastOrDefault(x => x.Shape.FillContains(e.GetPosition(this))).Entry;
            if (entry is not null) EntryClicked?.Invoke(entry);
        };
    }
    private Brush ForegroundBrush => (Brush?)TryFindResource("TextBrush") ?? Brushes.White;
    private Brush MutedBrush => (Brush?)TryFindResource("MutedBrush") ?? Brushes.LightGray;
    private static Brush ColorBrush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private void Text(DrawingContext dc, string text, Point origin, double width, double size, Brush brush, bool bold = false)
    {
        if (width < 8) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, origin);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); hits.Clear(); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var data = Entries?.Where(x => x.Size > 0).OrderByDescending(x => x.Size).ToList() ?? [];
        if (data.Count == 0)
        {
            Text(dc, "Здесь появится карта ваших данных", new(18, Math.Max(16, ActualHeight / 2 - 22)), ActualWidth - 36, 16, ForegroundBrush, true);
            Text(dc, "Запустите сканирование · только чтение", new(18, Math.Max(42, ActualHeight / 2 + 8)), ActualWidth - 36, 12, MutedBrush);
            return;
        }
        if (ActualWidth < 10 || ActualHeight < 10) return;
        if (Mode == "Donut") DrawDonut(dc, data);
        else if (Mode == "Bars") DrawBars(dc, data);
        else Layout(dc, data, new Rect(0, 0, ActualWidth, ActualHeight));
    }
    private void Layout(DrawingContext dc, List<ChartEntry> items, Rect bounds)
    {
        if (items.Count == 0 || bounds.Width < 1 || bounds.Height < 1) return;
        if (items.Count == 1)
        {
            var item = items[0]; var rect = new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4));
            var shape = new RectangleGeometry(rect, 7, 7);
            DrawChartGeometry(dc, shape, item);
            hits.Add((shape, item));
            dc.PushClip(shape);
            if (rect.Width > 65 && rect.Height > 35)
            {
                Text(dc, item.Name, new(rect.X + 10, rect.Y + 8), rect.Width - 20, 13, Brushes.Black, true);
                if (rect.Height > 54) Text(dc, Core.SizeFormatter.Format(item.Size), new(rect.X + 10, rect.Y + 29), rect.Width - 20, 12, Brushes.Black);
            }
            dc.Pop(); return;
        }
        double total = items.Sum(x => (double)x.Size), partial = 0; int split = 0;
        do { partial += items[split++].Size; } while (split < items.Count - 1 && partial < total / 2);
        double ratio = partial / total;
        if (bounds.Width >= bounds.Height)
        {
            double width = bounds.Width * ratio;
            Layout(dc, items.Take(split).ToList(), new(bounds.X, bounds.Y, width, bounds.Height));
            Layout(dc, items.Skip(split).ToList(), new(bounds.X + width, bounds.Y, bounds.Width - width, bounds.Height));
        }
        else
        {
            double height = bounds.Height * ratio;
            Layout(dc, items.Take(split).ToList(), new(bounds.X, bounds.Y, bounds.Width, height));
            Layout(dc, items.Skip(split).ToList(), new(bounds.X, bounds.Y + height, bounds.Width, bounds.Height - height));
        }
    }
    private void DrawBars(DrawingContext dc, List<ChartEntry> data)
    {
        double row = Math.Min(33, ActualHeight / Math.Max(1, data.Count)), maximum = data[0].Size;
        for (int i = 0; i < data.Count; i++)
        {
            var item = data[i]; double y = row * i;
            Text(dc, $"{i + 1:00}   {item.Name}", new(0, y), ActualWidth - 106, 12, ForegroundBrush);
            Text(dc, Core.SizeFormatter.Format(item.Size), new(Math.Max(0, ActualWidth - 98), y), 98, 12, MutedBrush);
            var bar = new Rect(0, y + 21, Math.Max(2, ActualWidth * item.Size / maximum), 5);
            var brush = ColorBrush(item.Color);
            brush.Opacity = hover is null || ReferenceEquals(item, hover) ? 1 : .34;
            dc.DrawRoundedRectangle(brush, null, bar, 2, 2);
            if (ReferenceEquals(item, hover)) dc.DrawRoundedRectangle(null, new Pen((Brush?)TryFindResource("AccentBrush") ?? Brushes.White, 2), new Rect(bar.X, bar.Y - 1, bar.Width, bar.Height + 2), 3, 3);
            hits.Add((new RectangleGeometry(new(0, y, ActualWidth, row)), item));
        }
    }
    private void DrawDonut(DrawingContext dc, List<ChartEntry> data)
    {
        double radius = Math.Max(2, Math.Min(ActualWidth, ActualHeight) / 2 - 12), inner = radius * .69;
        var center = new Point(ActualWidth / 2, ActualHeight / 2); double angle = -Math.PI / 2, total = data.Sum(x => (double)x.Size);
        Point P(double a, double r) => new(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r);
        (Geometry Geometry, ChartEntry Entry)? focused = null;
        foreach (var item in data)
        {
            double sweep = item.Size / total * Math.PI * 2, end = angle + Math.Min(sweep, Math.PI * 2 - .00001);
            var geometry = DonutSlice(angle, end, radius, inner, sweep > Math.PI);
            if (ReferenceEquals(item, hover)) focused = (DonutSlice(angle, end, radius + 7, Math.Max(1, inner - 3), sweep > Math.PI), item);
            else DrawChartGeometry(dc, geometry, item);
            hits.Add((geometry, item)); angle += sweep;
        }
        if (focused is { } f) DrawChartGeometry(dc, f.Geometry, f.Entry, true);
        Text(dc, Core.SizeFormatter.Format((long)total), new(center.X - inner + 10, center.Y - 13), inner * 2 - 20, 20, ForegroundBrush, true);
        Text(dc, "прочитано", new(center.X - inner + 10, center.Y + 16), inner * 2 - 20, 12, MutedBrush);

        StreamGeometry DonutSlice(double start, double end, double outerRadius, double innerRadius, bool largeArc)
        {
            var geometry = new StreamGeometry();
            using var ctx = geometry.Open();
            ctx.BeginFigure(P(start, outerRadius), true, true);
            ctx.ArcTo(P(end, outerRadius), new(outerRadius, outerRadius), 0, largeArc, SweepDirection.Clockwise, true, false);
            ctx.LineTo(P(end, innerRadius), true, false);
            ctx.ArcTo(P(start, innerRadius), new(innerRadius, innerRadius), 0, largeArc, SweepDirection.Counterclockwise, true, false);
            return geometry;
        }
    }
    private void DrawChartGeometry(DrawingContext dc, Geometry geometry, ChartEntry item, bool focused = false)
    {
        var brush = ColorBrush(item.Color);
        brush.Opacity = hover is null || ReferenceEquals(item, hover) ? 1 : .28;
        dc.DrawGeometry(brush, focused ? new Pen((Brush?)TryFindResource("AccentBrush") ?? Brushes.White, 2) : null, geometry);
    }
}
