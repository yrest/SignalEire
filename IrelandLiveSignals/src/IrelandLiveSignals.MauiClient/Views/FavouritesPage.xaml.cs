using IrelandLiveSignals.MauiClient.ViewModels;

namespace IrelandLiveSignals.MauiClient.Views;

public partial class FavouritesPage : ContentPage
{
    private readonly FavouritesViewModel _viewModel;

    public FavouritesPage(FavouritesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//Login");
}
