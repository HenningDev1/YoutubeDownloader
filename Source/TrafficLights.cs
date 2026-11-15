using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace YoutubeDownloader;

/// <summary>Apple-style red / yellow / green window buttons.</summary>
public sealed class TrafficLights : StackPanel
{
    public TrafficLights(MainWindow w)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        Background = Brushes.Transparent;
        var close = new Light("#FF5F57", "#4D0000", Light.Glyph.Close, () => w.Close(), "Close");
        var min = new Light("#FEBC2E", "#5A3A00", Light.Glyph.Minimize, () => w.WindowState = WindowState.Minimized, "Minimize");
        var max = new Light("#28C840", "#003A00", Light.Glyph.Zoom, w.ToggleFullScreen, "Full screen");
        Children.AddRange(new Control[] { close, min, max });
        PointerEntered += (_, _) => { foreach (var l in Children.OfType<Light>()) l.ShowGlyph(true); };
        PointerExited += (_, _) => { foreach (var l in Children.OfType<Light>()) l.ShowGlyph(false); };
    }

    private sealed class Light : Control
    {
        public enum Glyph { Close, Minimize, Zoom }
        private readonly IBrush _fill, _ink;
        private readonly Glyph _glyph;
        private readonly Action _click;
        private bool _show, _down;

        public Light(string fill, string ink, Glyph glyph, Action click, string tip)
        {
            _fill = new SolidColorBrush(Color.Parse(fill));
            _ink = new SolidColorBrush(Color.Parse(ink));
            _glyph = glyph; _click = click;
            Width = 13; Height = 13;
            ToolTip.SetTip(this, tip);
            ToolTip.SetShowDelay(this, 900);
        }

        public void ShowGlyph(bool on) { _show = on; InvalidateVisual(); }

        protected override void OnPointerPressed(PointerPressedEventArgs e) { _down = true; e.Handled = true; InvalidateVisual(); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            bool inside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
            bool wasDown = _down;
            _down = false; e.Handled = true; InvalidateVisual();
            if (wasDown && inside) _click();
        }

        public override void Render(DrawingContext dc)
        {
            double s = Bounds.Width, c = s / 2;
            dc.DrawEllipse(_fill, new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 0.6), new Point(c, c), c - 0.3, c - 0.3);
            if (_down) dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), null, new Point(c, c), c, c);
            if (!_show) return;
            var pen = new Pen(_ink, 1.3, lineCap: PenLineCap.Round);
            double a = s * 0.23;
            switch (_glyph)
            {
                case Glyph.Close:
                    dc.DrawLine(pen, new Point(c - a, c - a), new Point(c + a, c + a));
                    dc.DrawLine(pen, new Point(c - a, c + a), new Point(c + a, c - a));
                    break;
                case Glyph.Minimize:
                    dc.DrawLine(pen, new Point(c - a * 1.25, c), new Point(c + a * 1.25, c));
                    break;
                case Glyph.Zoom:
                {
                    double k = a * 1.15;
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(c - k, c - k), true);
                        ctx.LineTo(new Point(c + k * 0.35, c - k));
                        ctx.LineTo(new Point(c - k, c + k * 0.35));
                        ctx.EndFigure(true);
                        ctx.BeginFigure(new Point(c + k, c + k), true);
                        ctx.LineTo(new Point(c - k * 0.35, c + k));
                        ctx.LineTo(new Point(c + k, c - k * 0.35));
                        ctx.EndFigure(true);
                    }
                    dc.DrawGeometry(_ink, null, g);
                    break;
                }
            }
        }
    }
}
