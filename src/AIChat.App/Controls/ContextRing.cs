using System.Windows;
using System.Windows.Media;

namespace AIChat.App.Controls;

// Lightweight custom WPF control that draws the context usage ring directly.
public sealed class ContextRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(ContextRing),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(34, 34);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(1, size / 2 - 3);
        var backgroundPen = new Pen(new SolidColorBrush(Color.FromRgb(225, 229, 235)), 4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var foregroundPen = new Pen(new SolidColorBrush(Color.FromRgb(70, 137, 128)), 4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        drawingContext.DrawEllipse(null, backgroundPen, center, radius, radius);

        var progress = Math.Clamp(Progress / 100d, 0, 1);
        if (progress <= 0)
        {
            return;
        }

        var startAngle = -90d;
        // Use 359.9 instead of 360 so WPF draws a visible arc instead of treating
        // the start and end points as the same point.
        var endAngle = startAngle + progress * 359.9d;
        var startPoint = PointOnCircle(center, radius, startAngle);
        var endPoint = PointOnCircle(center, radius, endAngle);
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(startPoint, false, false);
            context.ArcTo(
                endPoint,
                new Size(radius, radius),
                0,
                progress > 0.5,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, foregroundPen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        // WPF uses Cartesian coordinates; this converts polar arc positions to x/y.
        var angle = angleDegrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }
}
