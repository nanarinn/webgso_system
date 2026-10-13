using Microsoft.AspNetCore.Components;
using TestingSystem.Web.Connectors.UseCases;
using TestingSystem.Web.Services;
namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class Dashboard : ComponentBase
{
    private const string PlantMetricTitle = "Total Plants";
    private const string PlantMetricDescription = "All registered Large Scale Solar plants in the repository.";
    private const string SuccessMetricTitle = "DDQ Successful Requests";
    private const string SuccessMetricDescription = "DDQ submissions that completed validation successfully.";
    private const string FailedMetricTitle = "DDQ Failed Requests";
    private const string FailedMetricDescription = "DDQ submissions that require correction or resubmission.";

    private int TotalPlants { get; set; }
    [Inject] private IRepositoryBackendApi RepositoryBackend { get; set; } = default!;
    [Inject] private ILogger<Dashboard> Logger { get; set; } = default!;

    private IReadOnlyList<DashboardRecentRecord> RecentRecords { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> PlantItems { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> SuccessfulItems { get; set; } = [];
    private IReadOnlyList<DashboardMetricItem> FailedItems { get; set; } = [];
    private IReadOnlyList<DashboardDdqTypeSummary> DdqTypes { get; set; } = [];
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
    private bool IsReloading { get; set; }
    private bool DashboardDataAvailable => !IsDashboardLoading && string.IsNullOrWhiteSpace(DashboardLoadError);

    private string MetricValue(int value) => DashboardDataAvailable ? value.ToString() : "—";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await ReloadDashboardAsync();
    }

    private async Task ReloadDashboardAsync()
    {
        if (IsReloading) return;

        IsReloading = true;
        IsDashboardLoading = true;
        DashboardLoadError = string.Empty;
        DashboardStatusError = string.Empty;

        try
        {
            await Task.WhenAll(LoadRepositoryStatusAsync(), LoadDashboardDataAsync());
        }
        finally
        {
            IsDashboardLoading = false;
            IsReloading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadRepositoryStatusAsync()
    {
        try
        {
            RepositoryStatus = await RepositoryBackend.GetStatusAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to load the dashboard repository status.");
            RepositoryStatus = null;
            DashboardStatusError = "The database status could not be confirmed. Try again.";
        }
    }

    private async Task LoadDashboardDataAsync()
    {
        try
        {
            var dashboard = await RepositoryBackend.GetDashboardAsync();
            RecentRecords = dashboard.RecentRecords;
            PlantItems = dashboard.Plants;
            SuccessfulItems = dashboard.SuccessfulDdq;
            FailedItems = dashboard.FailedDdq;

            TotalPlants = PlantItems.Count;
            DdqTypes = dashboard.DdqTypes;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unable to load the dashboard repository data.");
            DashboardLoadError = "Repository data could not be loaded. Check the server connection and try again.";
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
    private void OpenDdqMetric(DashboardDdqTypeSummary summary, string status)
    {
        var source = status == "Successful" ? SuccessfulItems : FailedItems;
        OpenMetric($"{summary.DataType} · {status}",
            $"Plants with a {status.ToLowerInvariant()} result in the latest {summary.DataType} collection.",
            source.Where(item => item.DataType == summary.DataType
                && item.SubmissionDateTime == summary.SubmissionDateTime).ToList());
        SelectedMetricCode = status == "Successful" ? "OK" : "ERR";
    }
    private void OpenHealthStatus() => IsHealthStatusOpen = true;
    private void CloseHealthStatus() => IsHealthStatusOpen = false;

}
