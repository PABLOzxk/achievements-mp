using System.Windows;
using AchievementsMP.App.Services;

namespace AchievementsMP.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }
}
