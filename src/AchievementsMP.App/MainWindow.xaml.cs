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

    private bool _flushed;

    // Fechar a janela no meio de uma edição do planejador: grava antes de sair.
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_flushed || e.Cancel || DataContext is not MainViewModel { HasPendingEdits: true } vm) return;
        e.Cancel = true;
        _flushed = true;
        try
        {
            await vm.FlushAsync();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao gravar edições pendentes ao fechar", ex);
        }
        Close();
    }
}
