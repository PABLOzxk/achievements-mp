using System.Windows.Media;
using AchievementsMP.App.Services;

namespace AchievementsMP.UiTests;

/// <summary>Todo tema precisa ser legível: texto contra fundo, cartão e barra lateral (contraste WCAG).</summary>
public class ThemeTests
{
    public static TheoryData<string> Ids() => new(ThemeCatalog.All.Select(t => t.Id));

    [Theory]
    [MemberData(nameof(Ids))]
    public void Text_is_readable_on_every_background(string id)
    {
        var t = ThemeCatalog.Find(id);
        foreach (var background in new[] { t.Card, t.Surface, t.SurfaceEnd, t.Window })
        {
            Assert.True(Contrast(t.TextPrimary, background) >= 7, $"{id}: texto principal");
            Assert.True(Contrast(t.TextSecondary, background) >= 4.5, $"{id}: texto secundário");
            Assert.True(Contrast(t.TextMuted, background) >= 4.5, $"{id}: texto apagado");
            Assert.True(Contrast(t.Accent, background) >= 4.5, $"{id}: links");
            Assert.True(Contrast(t.Gold, background) >= 4.5, $"{id}: percentual dourado do 100%");
        }
        Assert.True(Contrast(t.TextPrimary, t.CardHover) >= 7, $"{id}: texto dos botões");
    }

    [Fact]
    public void Ids_are_unique_and_unknown_id_falls_back_to_the_default()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.Same(ThemeCatalog.Default, ThemeCatalog.Find(null));
        Assert.Same(ThemeCatalog.Default, ThemeCatalog.Find("tema-de-uma-versao-futura"));
        Assert.Equal("roxo", ThemeCatalog.Find("roxo").Id);
    }

    private static double Contrast(Color a, Color b)
    {
        double L(Color c)
        {
            static double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
        var (hi, lo) = (Math.Max(L(a), L(b)), Math.Min(L(a), L(b)));
        return (hi + 0.05) / (lo + 0.05);
    }
}
