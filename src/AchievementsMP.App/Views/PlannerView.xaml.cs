using System.Windows.Controls;

namespace AchievementsMP.App.Views;

public partial class PlannerView : UserControl
{
    public PlannerView() => InitializeComponent();

    // A lista é refeita ao salvar (a conquista pode mudar de grupo): mantém a selecionada visível.
    private void OnPlanSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlanList.SelectedItem is { } item)
            PlanList.ScrollIntoView(item);
    }
}
