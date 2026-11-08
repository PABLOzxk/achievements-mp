using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AchievementsMP.App.Services;

/// <summary>
/// Cores de um tema. O padrão (<see cref="ThemeCatalog.Default"/>) é o que está em Theme/Colors.xaml; os outros trocam
/// os mesmos recursos antes de a primeira janela abrir. Raridade (roxo), desbloqueada (verde) e 100% (dourado) mantêm o
/// significado em todos.
/// </summary>
public sealed record ThemePalette(
    string Id,
    string Name,
    bool IsLight,
    Color Window,
    Color Surface,
    Color SurfaceEnd,
    Color Card,
    Color CardHover,
    Color Input,
    Color Border,
    Color Accent,
    Color AccentDark,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color Success,
    Color Warning,
    Color Danger,
    Color Rare,
    Color Gold,
    Color PrimaryStart,
    Color PrimaryEnd,
    Color ProgressStart,
    Color ProgressEnd,
    bool DiagonalSurface = false);

public static class ThemeCatalog
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // Os mesmos valores de Theme/Colors.xaml.
    public static readonly ThemePalette Default = new("padrao", "Padrão", false,
        C("#171A21"), C("#1B2838"), C("#171D25"), C("#202D39"), C("#2A3F5A"), C("#101822"), C("#2A475E"),
        C("#66C0F4"), C("#1A9FFF"), C("#E5EEF5"), C("#C7D5E0"), C("#8F98A0"),
        C("#A4D007"), C("#E8B339"), C("#E0523D"), C("#B07CFF"), C("#F2C14E"),
        C("#47BFFF"), C("#1A44C2"), C("#66C0F4"), C("#A4D007"));

    public static readonly IReadOnlyList<ThemePalette> All =
    [
        Default,
        new("preto", "Preto", false,
            C("#000000"), C("#070707"), C("#000000"), C("#121212"), C("#232323"), C("#050505"), C("#2C2C2C"),
            C("#66C0F4"), C("#1A7FD6"), C("#EDEDED"), C("#CACACA"), C("#8E8E8E"),
            C("#A4D007"), C("#E8B339"), C("#E0523D"), C("#B07CFF"), C("#F2C14E"),
            C("#47BFFF"), C("#1A44C2"), C("#66C0F4"), C("#A4D007")),
        new("grafite", "Grafite", false,
            C("#1A1B1E"), C("#2B2D31"), C("#232428"), C("#313338"), C("#404249"), C("#1E1F22"), C("#45474D"),
            C("#8AB4F8"), C("#4E5BD8"), C("#F2F3F5"), C("#D2D5DA"), C("#AEB2B9"),
            C("#A4D007"), C("#E8B339"), C("#F27B67"), C("#C29AFF"), C("#F2C14E"),
            C("#6A78F0"), C("#4752C4"), C("#8AB4F8"), C("#A4D007")),
        new("roxo", "Roxo", false,
            C("#130A1F"), C("#2D1250"), C("#0F0819"), C("#241638"), C("#36224F"), C("#140B22"), C("#43305F"),
            C("#C9A0FF"), C("#7B3FE4"), C("#F3ECFC"), C("#DACDEC"), C("#A797BE"),
            C("#A4D007"), C("#E8B339"), C("#F0645A"), C("#B07CFF"), C("#F2C14E"),
            C("#B57BFF"), C("#6A2BD9"), C("#8B4DFF"), C("#E07CFF"), DiagonalSurface: true),
        new("meia-noite", "Meia-noite", false,
            C("#070B1A"), C("#14205A"), C("#060A18"), C("#111A3A"), C("#1C2A57"), C("#080D20"), C("#26366B"),
            C("#7FA8FF"), C("#3D5AFE"), C("#ECF0FC"), C("#CBD3EE"), C("#929CC0"),
            C("#A4D007"), C("#E8B339"), C("#F0645A"), C("#B07CFF"), C("#F2C14E"),
            C("#5C7CFF"), C("#2A3FD1"), C("#3D5AFE"), C("#66C0F4"), DiagonalSurface: true),
        new("carmesim", "Carmesim", false,
            C("#14080A"), C("#431019"), C("#110608"), C("#2A1317"), C("#3E1D23"), C("#12070A"), C("#55262F"),
            C("#FF9B9B"), C("#C2283F"), C("#FBEFEF"), C("#E8D2D2"), C("#B4979B"),
            C("#A4D007"), C("#E8B339"), C("#FF7A6B"), C("#C08CFF"), C("#F2C14E"),
            C("#FF6B7A"), C("#B3203A"), C("#FF6B6B"), C("#F2C14E"), DiagonalSurface: true),
        new("claro", "Claro", true,
            C("#E6EBF1"), C("#F6F8FB"), C("#EDF1F6"), C("#FFFFFF"), C("#E3E9F0"), C("#E9EEF4"), C("#C9D3DE"),
            C("#155FAA"), C("#1A6FC4"), C("#17212C"), C("#33424F"), C("#5A6774"),
            C("#3F6A00"), C("#8A5700"), C("#B3301F"), C("#7440CF"), C("#875C00"),
            C("#2D8CE8"), C("#1A54C2"), C("#1A9FFF"), C("#5E9E00")),
    ];

    /// <summary>Tema pelo id gravado. Id desconhecido (de uma versão futura, ou nulo) = padrão.</summary>
    public static ThemePalette Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Default;
}

/// <summary>
/// Aplica um tema trocando os recursos de Theme/Colors.xaml. Só funciona antes de qualquer janela ou estilo ser criado:
/// os estilos guardam as cores que encontram na primeira vez. Por isso trocar de tema pede reiniciar o app.
/// </summary>
public static class ThemeManager
{
    public static ThemePalette Current { get; private set; } = ThemeCatalog.Default;

    public static void Apply(ResourceDictionary root, ThemePalette palette)
    {
        Current = palette;
        if (palette == ThemeCatalog.Default)
            return; // o XAML já tem as cores do padrão
        var values = Resources(palette);
        foreach (var colors in ColorDictionaries(root))
            foreach (var (key, value) in values)
                colors[key] = value;
    }

    /// <summary>Chaves que cada tema troca. Todas existem em Theme/Colors.xaml (o teste de interface confere).</summary>
    public static IReadOnlyDictionary<string, object> Resources(ThemePalette p)
    {
        var overlay = p.IsLight ? Colors.Black : Colors.White;
        var r = new Dictionary<string, object>
        {
            ["WindowColor"] = p.Window,
            ["SurfaceColor"] = p.Surface,
            ["CardColor"] = p.Card,
            ["CardHoverColor"] = p.CardHover,
            ["InputDarkColor"] = p.Input,
            ["BorderColor"] = p.Border,
            ["AccentColor"] = p.Accent,
            ["AccentDarkColor"] = p.AccentDark,
            ["TextPrimaryColor"] = p.TextPrimary,
            ["TextSecondaryColor"] = p.TextSecondary,
            ["TextMutedColor"] = p.TextMuted,
            ["SuccessColor"] = p.Success,
            ["WarningColor"] = p.Warning,
            ["DangerColor"] = p.Danger,
            ["RareColor"] = p.Rare,
            ["GoldColor"] = p.Gold,

            ["WindowBrush"] = Solid(p.Window),
            ["SurfaceBrush"] = Solid(p.Surface),
            ["CardBrush"] = Solid(p.Card),
            ["CardHoverBrush"] = Solid(p.CardHover),
            ["InputBrush"] = Solid(p.Input),
            ["BorderBrush"] = Solid(p.Border),
            ["AccentBrush"] = Solid(p.Accent),
            ["AccentDarkBrush"] = Solid(p.AccentDark),
            ["TextPrimaryBrush"] = Solid(p.TextPrimary),
            ["TextSecondaryBrush"] = Solid(p.TextSecondary),
            ["TextMutedBrush"] = Solid(p.TextMuted),
            ["SuccessBrush"] = Solid(p.Success),
            ["WarningBrush"] = Solid(p.Warning),
            ["DangerBrush"] = Solid(p.Danger),
            ["RareBrush"] = Solid(p.Rare),
            ["GoldBrush"] = Solid(p.Gold),

            ["HoverOverlayBrush"] = Solid(overlay, 0x14),
            ["PressedOverlayBrush"] = Solid(overlay, 0x1F),
            ["NeutralChipBrush"] = Solid(overlay, 0x22),
            ["ScrollThumbBrush"] = Solid(overlay, 0x33),
            ["ScrollThumbHoverBrush"] = Solid(overlay, 0x66),
            ["SelectedNavBrush"] = Solid(p.Accent, 0x1F),
            ["PrimaryButtonForegroundBrush"] = Solid(Colors.White),

            ["UnlockedRowBrush"] = Horizontal(WithAlpha(p.Success, 0x40), WithAlpha(p.Success, 0x14)),
            ["RareRowBrush"] = Horizontal(WithAlpha(p.Rare, 0x5A), WithAlpha(p.Rare, 0x1C)),
            ["SurfaceGradientBrush"] = SurfaceGradient(p),
            ["PrimaryButtonBrush"] = Horizontal(p.PrimaryStart, p.PrimaryEnd),
            ["ProgressBrush"] = Horizontal(p.ProgressStart, p.ProgressEnd),
        };
        if (p.IsLight)
        {
            // O texto do logo é claro; no tema claro entra a versão escura.
            var logo = new BitmapImage(new Uri("pack://application:,,,/AchievementsMP;component/Assets/logo-escuro.png"));
            logo.Freeze();
            r["LogoImage"] = logo;
        }
        return r;
    }

    public static LinearGradientBrush SurfaceGradient(ThemePalette p) => Freeze(new LinearGradientBrush(p.Surface, p.SurfaceEnd,
        new Point(0, 0), p.DiagonalSurface ? new Point(1, 1) : new Point(0, 1)));

    public static SolidColorBrush Solid(Color color, byte alpha = 0xFF) => Freeze(new SolidColorBrush(WithAlpha(color, alpha)));

    public static LinearGradientBrush Horizontal(Color start, Color end) => Freeze(new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 0)));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    private static IEnumerable<ResourceDictionary> ColorDictionaries(ResourceDictionary dictionary)
    {
        if (dictionary.Source?.OriginalString.EndsWith("Colors.xaml", StringComparison.OrdinalIgnoreCase) == true)
            yield return dictionary;
        foreach (var merged in dictionary.MergedDictionaries)
            foreach (var found in ColorDictionaries(merged))
                yield return found;
    }
}

/// <summary>Uma opção na escolha de tema ("Seu perfil"): a prévia em miniatura usa as cores do próprio tema.</summary>
public sealed class ThemeOption(ThemePalette palette)
{
    public ThemePalette Palette { get; } = palette;
    public string Id => Palette.Id;
    public string Name => Palette.Name;
    public Brush Background { get; } = ThemeManager.SurfaceGradient(palette);
    public Brush Sidebar { get; } = ThemeManager.Solid(palette.Window);
    public Brush Card { get; } = ThemeManager.Solid(palette.Card);
    public Brush Text { get; } = ThemeManager.Solid(palette.TextPrimary);
    public Brush Muted { get; } = ThemeManager.Solid(palette.TextMuted);
    public Brush Progress { get; } = ThemeManager.Horizontal(palette.ProgressStart, palette.ProgressEnd);
    public Brush Accent { get; } = ThemeManager.Solid(palette.Accent);

    public static IReadOnlyList<ThemeOption> All { get; } = ThemeCatalog.All.Select(p => new ThemeOption(p)).ToList();
}
