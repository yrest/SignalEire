using IrelandLiveSignals.MauiClient.ViewModels;

namespace IrelandLiveSignals.MauiClient.Views;

public partial class TransitSearchPage : ContentPage
{
    public TransitSearchPage(TransitSearchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
