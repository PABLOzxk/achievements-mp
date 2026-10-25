namespace AchievementsMP.App.ViewModels;

/// <summary>Item de ComboBox: valor + texto exibido.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
