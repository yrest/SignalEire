using IrelandLiveSignals.MauiClient.Views;

namespace IrelandLiveSignals.MauiClient;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register modal / detail routes
        Routing.RegisterRoute("Transit/StopBoard", typeof(StopBoardPage));
        Routing.RegisterRoute("Transit/Map", typeof(MapPage));
        Routing.RegisterRoute("Account/Alerts", typeof(AlertRulesPage));
        Routing.RegisterRoute("//Login", typeof(LoginPage));
    }
}
