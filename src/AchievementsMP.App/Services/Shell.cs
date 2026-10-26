using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace AchievementsMP.App.Services;

/// <summary>Janelas do sistema e reinício do app, atrás de uma interface para as telas não dependerem do WPF direto.</summary>
public interface IShell
{
    string? PickFolder(string title, string? initialFolder);
    string? PickBackupFile(string? initialFolder);
    bool Confirm(string message, string title);
    void Inform(string message, string title);

    /// <summary>Abre uma nova instância (que espera esta fechar) e fecha esta. Falso se não deu para reabrir sozinho.</summary>
    bool Restart();
}

public sealed class WpfShell : IShell
{
    private const string Caption = "Achievements MP";

    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialFolder) && System.IO.Directory.Exists(initialFolder))
            dialog.InitialDirectory = initialFolder;
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public string? PickBackupFile(string? initialFolder)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha o backup para restaurar",
            Filter = "Backup do Achievements MP (*.db)|*.db|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder) && System.IO.Directory.Exists(initialFolder))
            dialog.InitialDirectory = initialFolder;
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Inform(string message, string title) =>
        MessageBox.Show(Application.Current.MainWindow!, message, title.Length > 0 ? title : Caption, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Restart()
    {
        if (Environment.ProcessPath is not { } exe)
            return false;
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add(App.WaitForPidArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Process.Start(start);
        // Fecha pela janela, e não por Shutdown(), para gravar edições pendentes antes de sair.
        Application.Current.MainWindow?.Close();
        return true;
    }
}
