namespace AchievementsMP.App.ViewModels;

/// <summary>Páginas cujas linhas de jogo abrem a tela de detalhes ao clicar.</summary>
public interface IGameOpener
{
    void OpenGame(long appId);
}
