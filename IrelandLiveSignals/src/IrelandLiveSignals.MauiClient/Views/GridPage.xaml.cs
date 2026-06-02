using IrelandLiveSignals.MauiClient.ViewModels;

namespace IrelandLiveSignals.MauiClient.Views;

public partial class GridPage : ContentPage
{
    private readonly GridViewModel _viewModel;

    public GridPage(GridViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.RefreshAsync();
    }
}
