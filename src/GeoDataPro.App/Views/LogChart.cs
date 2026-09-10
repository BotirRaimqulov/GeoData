using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GeoDataPro.App.ViewModels;

namespace GeoDataPro.App.Views;

public sealed class LogChart : FrameworkElement
{
    const double LeftGutter = 62;
    const double TopGutter = 48;
    const double RightGutter = 18;
    const double BottomGutter = 22;
    const double MinWindow = 0.2;
    const double MaxWindow = 5000;

    static readonly Typeface Face = new("Segoe UI");
    static readonly Brush AxisText = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85));
    static readonly Brush PlotBg = Brushes.White;
    static readonly Pen MajorGrid = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE9)), 1));
    static readonly Pen MinorGrid = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xEC, 0xF0, 0xF6)), 1));
    static readonly Pen Frame = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC3, 0xCB, 0xDA)), 1));
    static readonly Pen Curve = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.6));
    static readonly Pen Cross = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), 1));
    static readonly Brush Marker = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)));
    static readonly Brush TipBg = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x1F, 0x29, 0x37)));

    static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }

    public static readonly DependencyProperty PointsSourceProperty =
        DependencyProperty.Register(nameof(PointsSource), typeof(IEnumerable), typeof(LogChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

    public static readonly DependencyProperty WindowMetresProperty =
        DependencyProperty.Register(nameof(WindowMetres), typeof(double), typeof(LogChart),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnWindowChanged));

    public static readonly DependencyProperty UnitLabelProperty =
        DependencyProperty.Register(nameof(UnitLabel), typeof(string), typeof(LogChart),
            new FrameworkPropertyMetadata("mkR/soat", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DepthLabelProperty =
        DependencyProperty.Register(nameof(DepthLabel), typeof(string), typeof(LogChart),
            new FrameworkPropertyMetadata("Chuqurlik, MD (m)", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? PointsSource
    {
        get => (IEnumerable?)GetValue(PointsSourceProperty);
        set => SetValue(PointsSourceProperty, value);
    }

    public double WindowMetres
    {
        get => (double)GetValue(WindowMetresProperty);
        set => SetValue(WindowMetresProperty, value);
    }

    public string UnitLabel
    {
        get => (string)GetValue(UnitLabelProperty);
        set => SetValue(UnitLabelProperty, value);
    }

    public string DepthLabel
    {
        get => (string)GetValue(DepthLabelProperty);
        set => SetValue(DepthLabelProperty, value);
    }

    readonly List<(double Md, double Value)> _data = new();
    readonly List<System.ComponentModel.INotifyPropertyChanged> _watched = new();
    double _viewTop;
    double _viewSpan;
    bool _hasCursor;
    Point _cursor;
    Point _panOrigin;
    double _panTop;
    bool _panning;

    public LogChart()
    {
        ClipToBounds = true;
        Focusable = true;
        SnapsToDevicePixels = true;
    }

    static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LogChart)d;

        if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldSource)
            oldSource.CollectionChanged -= chart.OnCollectionChanged;

        if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newSource)
            newSource.CollectionChanged += chart.OnCollectionChanged;

        chart.Reload(true);
    }

    static void OnWindowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LogChart)d;
        var requested = (double)e.NewValue;
        if (requested <= 0) return;

        var centre = chart._viewTop + chart._viewSpan / 2;
        chart._viewSpan = Math.Clamp(requested, MinWindow, MaxWindow);
        chart._viewTop = centre - chart._viewSpan / 2;
        chart.ClampView();
        chart.InvalidateVisual();
    }

    void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Reload(false);

    void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "Md" or "CoreGk")) return;
        Dispatcher.BeginInvoke(new Action(() => Reload(false)), System.Windows.Threading.DispatcherPriority.Background);
    }

    public void ResetView()
    {
        Reload(true);
    }

    void Reload(bool resetView)
    {
        _data.Clear();

        foreach (var watched in _watched) watched.PropertyChanged -= OnItemChanged;
        _watched.Clear();

        if (PointsSource != null)
        {
            foreach (var item in PointsSource)
            {
                if (item is System.ComponentModel.INotifyPropertyChanged observable)
                {
                    observable.PropertyChanged += OnItemChanged;
                    _watched.Add(observable);
                }

                switch (item)
                {
                    case GkPoint gk when double.IsFinite(gk.Md) && double.IsFinite(gk.Value):
                        _data.Add((gk.Md, gk.Value));
                        break;
                    case Core.Data.SrpRow row when double.IsFinite(row.Md) && double.IsFinite(row.CoreGk):
                        _data.Add((row.Md, row.CoreGk));
                        break;
                }
            }
        }

        _data.Sort((a, b) => a.Md.CompareTo(b.Md));

        if (resetView || _viewSpan <= 0)
        {
            if (_data.Count > 0)
            {
                var min = _data[0].Md;
                var max = _data[^1].Md;
                var span = Math.Max(max - min, MinWindow);
                _viewTop = min;
                _viewSpan = WindowMetres > 0 ? Math.Clamp(WindowMetres, MinWindow, MaxWindow) : span;
            }
            else
            {
                _viewTop = 0;
                _viewSpan = WindowMetres > 0 ? WindowMetres : 10;
            }
        }

        ClampView();
        InvalidateVisual();
    }

    void ClampView()
    {
        if (_data.Count == 0) return;

        var min = _data[0].Md;
        var max = _data[^1].Md;
        var total = Math.Max(max - min, MinWindow);

        _viewSpan = Math.Clamp(_viewSpan, MinWindow, Math.Max(total, MinWindow));

        var slack = total - _viewSpan;
        if (slack <= 0)
        {
            _viewTop = min - (_viewSpan - total) / 2;
            return;
        }

        _viewTop = Math.Clamp(_viewTop, min, min + slack);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _cursor = e.GetPosition(this);
        _hasCursor = true;

        if (_panning && e.LeftButton == MouseButtonState.Pressed)
        {
            var plot = PlotRect();
            if (plot.Height > 0)
            {
                var metresPerPixel = _viewSpan / plot.Height;
                _viewTop = _panTop - (_cursor.Y - _panOrigin.Y) * metresPerPixel;
                ClampView();
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hasCursor = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _panOrigin = e.GetPosition(this);
        _panTop = _viewTop;
        _panning = true;
        CaptureMouse();
        Focus();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _panning = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var plot = PlotRect();
        if (plot.Height <= 0 || _data.Count == 0) return;

        var anchorRatio = Math.Clamp((_cursor.Y - plot.Top) / plot.Height, 0, 1);
        var anchorDepth = _viewTop + anchorRatio * _viewSpan;

        var factor = e.Delta > 0 ? 0.85 : 1 / 0.85;
        _viewSpan = Math.Clamp(_viewSpan * factor, MinWindow, MaxWindow);
        _viewTop = anchorDepth - anchorRatio * _viewSpan;

        ClampView();
        InvalidateVisual();
        e.Handled = true;
    }

    Rect PlotRect()
    {
        var width = Math.Max(ActualWidth - LeftGutter - RightGutter, 1);
        var height = Math.Max(ActualHeight - TopGutter - BottomGutter, 1);
        return new Rect(LeftGutter, TopGutter, width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var plot = PlotRect();

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(PlotBg, null, plot);

        if (_data.Count == 0)
        {
            dc.DrawRectangle(null, Frame, plot);
            Draw(dc, "Ma'lumot yo'q", 12, AxisText, plot.Left + 12, plot.Top + 12);
            return;
        }

        var (valueMin, valueMax, valueStep) = ValueAxis();
        DrawValueAxis(dc, plot, valueMin, valueMax, valueStep);
        DrawDepthAxis(dc, plot);
        DrawCurve(dc, plot, valueMin, valueMax);

        dc.DrawRectangle(null, Frame, plot);

        DrawCursor(dc, plot, valueMin, valueMax);
    }

    (double Min, double Max, double Step) ValueAxis()
    {
        var visible = _data.Where(x => x.Md >= _viewTop - _viewSpan && x.Md <= _viewTop + _viewSpan * 2).ToList();
        if (visible.Count == 0) visible = _data;

        var min = visible.Min(x => x.Value);
        var max = visible.Max(x => x.Value);

        if (min > 0) min = 0;
        if (Math.Abs(max - min) < 1e-6) max = min + 1;

        var step = NiceStep((max - min) / 4);
        var niceMin = Math.Floor(min / step) * step;
        var niceMax = Math.Ceiling(max / step) * step;
        if (Math.Abs(niceMax - niceMin) < 1e-9) niceMax = niceMin + step;

        return (niceMin, niceMax, step);
    }

    static double NiceStep(double raw)
    {
        if (raw <= 0 || !double.IsFinite(raw)) return 1;

        var exponent = Math.Floor(Math.Log10(raw));
        var magnitude = Math.Pow(10, exponent);
        var normalized = raw / magnitude;

        var nice = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        };

        return nice * magnitude;
    }

    void DrawValueAxis(DrawingContext dc, Rect plot, double min, double max, double step)
    {
        var range = max - min;
        if (range <= 0) return;

        for (double value = min; value <= max + step / 2; value += step)
        {
            var x = plot.Left + (value - min) / range * plot.Width;
            x = Math.Round(x) + 0.5;
            if (x < plot.Left - 1 || x > plot.Right + 1) continue;

            dc.DrawLine(MajorGrid, new Point(x, plot.Top), new Point(x, plot.Bottom));

            var text = value.ToString(step < 1 ? "0.0" : "0", CultureInfo.InvariantCulture);
            var formatted = Format(text, 11, AxisText);
            dc.DrawText(formatted, new Point(x - formatted.Width / 2, plot.Top - formatted.Height - 4));

            var half = x + step / range * plot.Width / 2;
            if (half < plot.Right)
                dc.DrawLine(MinorGrid, new Point(Math.Round(half) + 0.5, plot.Top),
                    new Point(Math.Round(half) + 0.5, plot.Bottom));
        }

        var unit = Format(UnitLabel, 11, AxisText);
        dc.DrawText(unit, new Point(plot.Left + (plot.Width - unit.Width) / 2, plot.Top - unit.Height - 22));
    }

    void DrawDepthAxis(DrawingContext dc, Rect plot)
    {
        var step = NiceStep(_viewSpan / 8);
        var first = Math.Ceiling(_viewTop / step) * step;

        for (double depth = first; depth <= _viewTop + _viewSpan + step / 2; depth += step)
        {
            var y = plot.Top + (depth - _viewTop) / _viewSpan * plot.Height;
            if (y < plot.Top - 1 || y > plot.Bottom + 1) continue;
            y = Math.Round(y) + 0.5;

            dc.DrawLine(MajorGrid, new Point(plot.Left, y), new Point(plot.Right, y));

            var digits = step < 0.1 ? "0.00" : step < 1 ? "0.0" : "0";
            var formatted = Format(depth.ToString(digits, CultureInfo.InvariantCulture), 11, AxisText);
            dc.DrawText(formatted, new Point(plot.Left - formatted.Width - 8, y - formatted.Height / 2));
        }

        var label = Format(DepthLabel, 11, AxisText);
        dc.PushTransform(new RotateTransform(-90, 14, plot.Top + plot.Height / 2));
        dc.DrawText(label, new Point(14 - label.Width / 2, plot.Top + plot.Height / 2 - label.Height / 2));
        dc.Pop();
    }

    void DrawCurve(DrawingContext dc, Rect plot, double min, double max)
    {
        var range = max - min;
        if (range <= 0 || _viewSpan <= 0) return;

        var geometry = new StreamGeometry();
        var markers = new List<Point>();

        using (var ctx = geometry.Open())
        {
            bool started = false;

            foreach (var (md, value) in _data)
            {
                var y = plot.Top + (md - _viewTop) / _viewSpan * plot.Height;
                if (y < plot.Top - 400 || y > plot.Bottom + 400) continue;

                var x = plot.Left + (value - min) / range * plot.Width;
                var point = new Point(Math.Clamp(x, plot.Left, plot.Right), y);

                if (!started)
                {
                    ctx.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    ctx.LineTo(point, true, false);
                }

                if (y >= plot.Top && y <= plot.Bottom) markers.Add(point);
            }
        }

        geometry.Freeze();

        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawGeometry(null, Curve, geometry);

        if (markers.Count <= 400)
            foreach (var marker in markers)
                dc.DrawEllipse(Marker, null, marker, 2.4, 2.4);

        dc.Pop();
    }

    void DrawCursor(DrawingContext dc, Rect plot, double min, double max)
    {
        if (!_hasCursor || _data.Count == 0) return;
        if (!plot.Contains(_cursor)) return;

        var depth = _viewTop + (_cursor.Y - plot.Top) / plot.Height * _viewSpan;

        var nearest = _data[0];
        var best = double.MaxValue;
        foreach (var candidate in _data)
        {
            var distance = Math.Abs(candidate.Md - depth);
            if (distance >= best) continue;
            best = distance;
            nearest = candidate;
        }

        var range = max - min;
        if (range <= 0) return;

        var y = Math.Round(plot.Top + (nearest.Md - _viewTop) / _viewSpan * plot.Height) + 0.5;
        var x = Math.Round(plot.Left + (nearest.Value - min) / range * plot.Width) + 0.5;

        if (y < plot.Top || y > plot.Bottom) return;

        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawLine(Cross, new Point(plot.Left, y), new Point(plot.Right, y));
        dc.DrawLine(Cross, new Point(x, plot.Top), new Point(x, plot.Bottom));
        dc.DrawEllipse(Brushes.White, Cross, new Point(x, y), 3.5, 3.5);
        dc.Pop();

        var text = string.Format(
            CultureInfo.InvariantCulture,
            "MD: {0:0.0} m  |  CoreGK: {1:0.00} {2}",
            nearest.Md, nearest.Value, UnitLabel);

        var formatted = Format(text, 11, Brushes.White);
        var box = new Rect(
            Math.Clamp(_cursor.X + 14, plot.Left, Math.Max(plot.Right - formatted.Width - 16, plot.Left)),
            Math.Clamp(y - formatted.Height - 12, plot.Top + 2, plot.Bottom - formatted.Height - 10),
            formatted.Width + 16,
            formatted.Height + 10);

        dc.DrawRoundedRectangle(TipBg, null, box, 5, 5);
        dc.DrawText(formatted, new Point(box.Left + 8, box.Top + 5));
    }

    void Draw(DrawingContext dc, string text, double size, Brush brush, double x, double y) =>
        dc.DrawText(Format(text, size, brush), new Point(x, y));

    FormattedText Format(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
