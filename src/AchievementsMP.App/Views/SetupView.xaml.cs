using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AchievementsMP.App.ViewModels;

namespace AchievementsMP.App.Views;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // PasswordBox não permite binding de Password (de propósito); repassamos manualmente.
    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel vm)
            vm.NewApiKey = ApiKeyBox.Password;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SetupViewModel old) old.PropertyChanged -= OnViewModelChanged;
        if (e.NewValue is SetupViewModel vm) vm.PropertyChanged += OnViewModelChanged;
    }

    // Depois de salvar, o VM limpa NewApiKey; limpamos o campo também.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupViewModel.NewApiKey) && sender is SetupViewModel { NewApiKey: "" } && ApiKeyBox.Password.Length > 0)
            ApiKeyBox.Clear();
    }
}
