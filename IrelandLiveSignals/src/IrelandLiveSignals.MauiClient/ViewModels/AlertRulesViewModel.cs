using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IrelandLiveSignals.MauiClient.Models;
using IrelandLiveSignals.MauiClient.Services;

namespace IrelandLiveSignals.MauiClient.ViewModels;

public partial class AlertRulesViewModel : ObservableObject
{
    private readonly ISignalEireApiClient _apiClient;
    private readonly ILocalCacheService _cache;

    [ObservableProperty]
    private ObservableCollection<AlertRuleDto> _rules = [];

    [ObservableProperty]
    private bool _isLoading;

    public AlertRulesViewModel(ISignalEireApiClient apiClient, ILocalCacheService cache)
    {
        _apiClient = apiClient;
        _cache = cache;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        const string cacheKey = "alert_rules";
        IsLoading = true;

        try
        {
            var cached = await _cache.GetAsync<List<AlertRuleDto>>(cacheKey);
            if (cached is not null)
                UpdateRules(cached);

            var fresh = await _apiClient.GetAlertRulesAsync();
            if (fresh is not null)
            {
                UpdateRules(fresh);
                await _cache.SetAsync(cacheKey, fresh, TimeSpan.FromMinutes(AppConfig.AlertRulesCacheMinutes));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteRuleAsync(AlertRuleDto rule)
    {
        var deleted = await _apiClient.DeleteAlertRuleAsync(rule.Id);
        if (deleted)
        {
            Rules.Remove(rule);
            await _cache.RemoveAsync("alert_rules");
        }
    }

    private void UpdateRules(List<AlertRuleDto> items)
    {
        Rules.Clear();
        foreach (var item in items)
            Rules.Add(item);
    }
}
