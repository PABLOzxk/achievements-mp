using System.Windows;
using AchievementsMP.App.Services;
using AchievementsMP.App.ViewModels;

namespace AchievementsMP.App.Views;

/// <summary>Janela "Seu perfil". Fecha sozinha depois de salvar; Esc ou Cancelar fecham sem gravar.</summary>
public partial class ProfileEditorWindow : Window
{
    public ProfileEditorWindow(ProfileEditorViewModel editor)
    {
        InitializeComponent();
        DataContext = editor;
        editor.CloseRequested += Close;
        Closed += (_, _) => editor.CloseRequested -= Close;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }
}
