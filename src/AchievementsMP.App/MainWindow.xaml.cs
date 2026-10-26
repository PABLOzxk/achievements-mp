using System.ComponentModel;
using System.Windows;
using AchievementsMP.App.Services;
using AchievementsMP.App.ViewModels;

namespace AchievementsMP.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private bool _readyToClose;

    // Fechar a janela: grava edições pendentes e faz o backup do dia antes de sair.
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_readyToClose || e.Cancel || DataContext is not MainViewModel vm) return;
        e.Cancel = true;
        _readyToClose = true;
        IsEnabled = false;
        try
        {
            await vm.BeforeExitAsync();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao preparar o fechamento", ex);
        }
        Close();
    }
}
