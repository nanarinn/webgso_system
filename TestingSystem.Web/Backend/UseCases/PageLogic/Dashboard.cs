using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TestingSystem.Web.Connectors.UseCases;
namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class Dashboard : ComponentBase
{
    private const string PlantMetricTitle = "Total Plants";
    private const string PlantMetricDescription = "All registered Large Scale Solar plants in the repository.";
    private const string SuccessMetricTitle = "DDQ Successful Requests";
    private const string SuccessMetricDescription = "DDQ submissions that completed validation successfully.";
    private const string FailedMetricTitle = "DDQ Failed Requests";
    private const string FailedMetricDescription = "DDQ submissions that require correction or resubmission.";
    private const string MmfMetricTitle = "Active MMF";
    private const string MmfMetricDescription = "Plants with an active MMF data stream.";

    private int TotalPlants { get; set; }
    private int DdqSuccessfulRequests { get; set; }
    private int DdqFailedRequests { get; set; }
    private int ActiveMmf { get; set; }
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;

    private IReadOnlyList<DashboardRecentRecord> RecentRecords { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> PlantItems { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> SuccessfulItems { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> FailedItems { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> MmfItems { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> SelectedMetricItems { get; set; } = [];
    private string SelectedMetricTitle { get; set; } = string.Empty;
    private string SelectedMetricDescription { get; set; } = string.Empty;
    private string SelectedMetricCode { get; set; } = string.Empty;
    private bool IsMetricOpen { get; set; }
    private bool IsHealthStatusOpen { get; set; }
    private bool IsDashboardLoading { get; set; } = true;
    private string DashboardLoadError { get; set; } = string.Empty;
    private string DashboardStatusError { get; set; } = string.Empty;
    private RepositoryStatusResponse? RepositoryStatus { get; set; }
    private bool DashboardDataAvailable => !IsDashboardLoading && string.IsNullOrWhiteSpace(DashboardLoadError);

    private string MetricValue(int value) => DashboardDataAvailable ? value.ToString() : "—";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var statusTask = LoadRepositoryStatusAsync();
        await LoadDashboardDataAsync();
        await statusTask;
    }

    private async Task LoadRepositoryStatusAsync()
    {
        try
        {
            RepositoryStatus = await JavaScript.InvokeAsync<RepositoryStatusResponse>(
                "sblssBackendApi.get", "/api/status", 30000);
        }
        catch (JSException exception)
        {
            DashboardStatusError = exception.Message;
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadDashboardDataAsync()
    {
        try
        {
            var dashboard = await JavaScript.InvokeAsync<DashboardResponse>(
                "sblssBackendApi.get", "/api/dashboard", 30000);
            RecentRecords = dashboard.RecentRecords;
            PlantItems = dashboard.Plants;
            SuccessfulItems = dashboard.SuccessfulDdq;
            FailedItems = dashboard.FailedDdq;
            MmfItems = dashboard.ActiveMmf;

            TotalPlants = PlantItems.Count;
            DdqSuccessfulRequests = SuccessfulItems.Count;
            DdqFailedRequests = FailedItems.Count;
            ActiveMmf = MmfItems.Count;
        }
        catch (JSException exception)
        {
            DashboardLoadError = exception.Message;
        }
        finally
        {
            IsDashboardLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OpenMetric(string title, string description, IReadOnlyList<DashboardMetricItem> items)
    {
        SelectedMetricTitle = title;
        SelectedMetricDescription = description;
        SelectedMetricItems = items;
        SelectedMetricCode = title switch
        {
            PlantMetricTitle => "PLT",
            SuccessMetricTitle => "OK",
            FailedMetricTitle => "ERR",
            _ => "MMF"
        };
        IsMetricOpen = true;
    }

    private void CloseMetric() => IsMetricOpen = false;
    private void OpenHealthStatus() => IsHealthStatusOpen = true;
    private void CloseHealthStatus() => IsHealthStatusOpen = false;

}
