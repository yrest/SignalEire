using IrelandLiveSignals.MauiClient.ViewModels;

namespace IrelandLiveSignals.MauiClient.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
