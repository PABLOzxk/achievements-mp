using AchievementsMP.App.ViewModels;

namespace AchievementsMP.UiTests;

/// <summary>"100%" só aparece com o jogo (ou a meta) completo de verdade.</summary>
public class ProgressTextTests
{
    [Theory]
    [InlineData(99.6, false, "99%")]  // 249 de 250: arredondar mostraria um 100% falso
    [InlineData(99.5, false, "99%")]
    [InlineData(100.0, false, "99%")] // percentual arredondado na origem, mas ainda falta conquista
    [InlineData(100.0, true, "100%")]
    [InlineData(42.9, false, "42%")]
    [InlineData(0.0, false, "0%")]
    public void Whole_percent_never_shows_100_before_it_is_complete(double percent, bool complete, string expected) =>
        Assert.Equal(expected, LibraryItem.WholePercent(percent, complete));
}
