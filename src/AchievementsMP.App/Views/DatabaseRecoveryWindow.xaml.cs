using System.Windows;

namespace AchievementsMP.App.Views;

public enum RecoveryChoice { Close, Restore, StartOver }

/// <summary>Aparece no lugar da janela principal quando o banco local não abre. Só pergunta; quem age é o App.</summary>
public partial class DatabaseRecoveryWindow : Window
{
    public DatabaseRecoveryWindow(string detail, string backupsFolder)
    {
        InitializeComponent();
        DetailText.Text = $"Detalhe técnico: {detail}";
        StartOverText.Text = $"O app abre vazio e busca de novo suas conquistas na Steam. Classificações, notas, metas e listas só voltam com um backup. O arquivo danificado fica guardado em {backupsFolder}.";
    }

    public RecoveryChoice Choice { get; private set; } = RecoveryChoice.Close;

    private void OnRestore(object sender, RoutedEventArgs e) => Finish(RecoveryChoice.Restore);

    private void OnStartOver(object sender, RoutedEventArgs e) => Finish(RecoveryChoice.StartOver);

    private void OnClose(object sender, RoutedEventArgs e) => Finish(RecoveryChoice.Close);

    private void Finish(RecoveryChoice choice)
    {
        Choice = choice;
        Close();
    }
}
