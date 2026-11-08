using System.Windows;
using System.Windows.Controls;

namespace AchievementsMP.App.Views;

/// <summary>
/// Mostra a dica (ToolTip) de um TextBlock só quando o texto não coube e foi cortado com "…".
/// Texto curto, que aparece inteiro, não ganha dica repetindo o que já está na tela.
/// </summary>
public static class TrimmedTextToolTip
{
    public static readonly DependencyProperty OnlyWhenTrimmedProperty = DependencyProperty.RegisterAttached(
        "OnlyWhenTrimmed", typeof(bool), typeof(TrimmedTextToolTip), new PropertyMetadata(false, OnChanged));

    public static bool GetOnlyWhenTrimmed(DependencyObject element) => (bool)element.GetValue(OnlyWhenTrimmedProperty);
    public static void SetOnlyWhenTrimmed(DependencyObject element, bool value) => element.SetValue(OnlyWhenTrimmedProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
            return;
        text.ToolTipOpening -= OnToolTipOpening;
        if ((bool)e.NewValue)
            text.ToolTipOpening += OnToolTipOpening;
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is TextBlock text && !IsTrimmed(text))
            e.Handled = true; // cancela a dica
    }

    /// <summary>Mede o texto inteiro com a mesma largura e compara com o espaço que ele ganhou na tela.</summary>
    internal static bool IsTrimmed(TextBlock text)
    {
        if (string.IsNullOrEmpty(text.Text) || text.ActualWidth <= 0)
            return false;
        var probe = new TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            FontStretch = text.FontStretch,
            Padding = text.Padding,
            TextWrapping = text.TextWrapping,
            LineHeight = text.LineHeight,
            LineStackingStrategy = text.LineStackingStrategy,
            FlowDirection = text.FlowDirection,
        };
        if (text.TextWrapping == TextWrapping.NoWrap)
        {
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width > text.ActualWidth + 0.5;
        }
        probe.Measure(new Size(text.ActualWidth, double.PositiveInfinity));
        return probe.DesiredSize.Height > text.ActualHeight + 0.5;
    }
}
