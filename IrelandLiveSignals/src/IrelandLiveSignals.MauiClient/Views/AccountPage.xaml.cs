using IrelandLiveSignals.MauiClient.ViewModels;

namespace IrelandLiveSignals.MauiClient.Views;

public partial class AccountPage : ContentPage
{
    public AccountViewModel ViewModel => (AccountViewModel)BindingContext;

    public AccountPage(AccountViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ViewModel.LoadTariffPlansCommand.Execute(null);
    }
}
