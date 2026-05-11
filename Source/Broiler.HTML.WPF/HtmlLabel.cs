using Broiler.HTML.Orchestration.Core;
using Broiler.HTML.WPF.Adapters;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using SizeF = System.Drawing.SizeF;

namespace Broiler.HTML.WPF;

public class HtmlLabel : HtmlControl
{
    public static readonly DependencyProperty AutoSizeProperty = DependencyProperty.Register("AutoSize", typeof(bool), typeof(HtmlLabel), new PropertyMetadata(true, OnDependencyProperty_valueChanged));
    public static readonly DependencyProperty AutoSizeHeightOnlyProperty = DependencyProperty.Register("AutoSizeHeightOnly", typeof(bool), typeof(HtmlLabel), new PropertyMetadata(false, OnDependencyProperty_valueChanged));

    static HtmlLabel() => BackgroundProperty.OverrideMetadata(typeof(HtmlLabel), new FrameworkPropertyMetadata(Brushes.Transparent));

    [Category("Layout")]
    [Description("Automatically sets the size of the label by content size.")]
    public bool AutoSize
    {
        get { return (bool)GetValue(AutoSizeProperty); }
        set { SetValue(AutoSizeProperty, value); }
    }

    [Category("Layout")]
    [Description("Automatically sets the height of the label by content height (width is not effected)")]
    public virtual bool AutoSizeHeightOnly
    {
        get { return (bool)GetValue(AutoSizeHeightOnlyProperty); }
        set { SetValue(AutoSizeHeightOnlyProperty, value); }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (_htmlContainer != null)
        {
            using var ig = new GraphicsAdapter();
            var horizontal = Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right;
            var vertical = Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;

            var size = new SizeF((float)(constraint.Width < double.PositiveInfinity ? constraint.Width - horizontal : 0), (float)(constraint.Height < double.PositiveInfinity ? constraint.Height - vertical : 0));
            var minSize = new SizeF((float)(MinWidth < double.PositiveInfinity ? MinWidth - horizontal : 0), (float)(MinHeight < double.PositiveInfinity ? MinHeight - vertical : 0));
            var maxSize = new SizeF((float)(MaxWidth < double.PositiveInfinity ? MaxWidth - horizontal : 0), (float)(MaxHeight < double.PositiveInfinity ? MaxHeight - vertical : 0));

            var newSize = HtmlRendererUtils.Layout(ig, _htmlContainer.HtmlContainerInt, size, minSize, maxSize, AutoSize, AutoSizeHeightOnly);

            constraint = new Size(newSize.Width + horizontal, newSize.Height + vertical);
        }

        if (double.IsPositiveInfinity(constraint.Width) || double.IsPositiveInfinity(constraint.Height))
            constraint = Size.Empty;

        return constraint;
    }

    /// <summary>
    /// Handle when dependency property value changes to update the underline HtmlContainer with the new value.
    /// </summary>
    private static void OnDependencyProperty_valueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not HtmlLabel control)
            return;

        if (e.Property == AutoSizeProperty)
        {
            if ((bool)e.NewValue)
            {
                dependencyObject.SetValue(AutoSizeHeightOnlyProperty, false);
                control.InvalidateMeasure();
                control.InvalidateVisual();
            }
        }
        else if (e.Property == AutoSizeHeightOnlyProperty)
        {
            if ((bool)e.NewValue)
            {
                dependencyObject.SetValue(AutoSizeProperty, false);
                control.InvalidateMeasure();
                control.InvalidateVisual();
            }
        }
    }
}