using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
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
    private List<RecentRepositoryRecord> RecentRecords { get; set; } = [];
    private List<DashboardItem> PlantItems { get; set; } = [];
    private List<DashboardItem> SuccessfulItems { get; set; } = [];
    private List<DashboardItem> FailedItems { get; set; } = [];
    private List<DashboardItem> MmfItems { get; set; } = [];
    private IReadOnlyList<DashboardItem> SelectedMetricItems { get; set; } = [];
    private string SelectedMetricTitle { get; set; } = string.Empty;
    private string SelectedMetricDescription { get; set; } = string.Empty;
    private string SelectedMetricCode { get; set; } = string.Empty;
    private bool IsMetricOpen { get; set; }
    private bool IsHealthStatusOpen { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var plants = await database.LssPlants
            .Where(plant => plant.IsActive)
            .OrderByDescending(plant => plant.CreatedAt)
            .ThenByDescending(plant => plant.Id)
            .ToListAsync();

        var jobs = await database.CollectionJobs.AsNoTracking().OrderByDescending(x => x.StartedAt)
            .Join(database.LssPlants, job => job.PlantId, plant => plant.Id, (job, plant) => new { job, plant }).Take(100).ToListAsync();
        RecentRecords = jobs.Take(4).Select(x => new RecentRepositoryRecord(
            x.job.CollectionId,
            DisplayCollectionReference(x.job.CollectionId),
            x.plant.Name,
            x.job.DataType,
            x.job.Status == "Completed" ? "Successful" : x.job.Status,
            x.job.StartedAt)).ToList();
        PlantItems = plants.Select(plant => new DashboardItem(
            plant.Name,
            $"{plant.State} · {plant.CapacityMw:N0} MW · API configuration registered",
            plant.PlantId,
            plant.IsActive ? "Active" : "Inactive",
            plant.CreatedAt)).ToList();

        SuccessfulItems = jobs.Where(x => x.job.DataType != "MMF" && x.job.Status == "Completed")
            .Select(x => CreateRequestItem(x.job.CollectionId, x.plant.Name, x.job.StartedAt, "Successful"))
            .ToList();
        FailedItems = jobs.Where(x => x.job.DataType != "MMF" && x.job.Status == "Failed")
            .Select(x => CreateRequestItem(x.job.CollectionId, x.plant.Name, x.job.StartedAt, "Failed"))
            .ToList();
        MmfItems = plants
            .Where(plant => !string.IsNullOrWhiteSpace(plant.MmfApiEndpoint))
            .Select(plant => new DashboardItem(
                plant.Name,
                $"Active MMF endpoint · {plant.MmfApiEndpoint}",
                plant.PlantId,
                "Active",
                plant.CreatedAt))
            .ToList();

        TotalPlants = PlantItems.Count;
        DdqSuccessfulRequests = SuccessfulItems.Count;
        DdqFailedRequests = FailedItems.Count;
        ActiveMmf = MmfItems.Count;
    }

    private void OpenMetric(string title, string description, IReadOnlyList<DashboardItem> items)
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

    private static string DisplayCollectionReference(string reference)
    {
        if (reference.StartsWith("CMD-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = reference.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) return $"{parts[0]}-{parts[1]}-{parts[2]}";
        }
        if (reference.StartsWith("COL-SCH-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = reference.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) return $"COL-SCH-{parts[2]}";
        }

        return reference.Length <= 20 ? reference : $"{reference[..19]}…";
    }

    private static DashboardItem CreateRequestItem(string collectionId, string plantName, DateTimeOffset updatedAt, string status) =>
        new(
            $"{plantName} DDQ Submission",
            $"DDQ collection data for {plantName}",
            collectionId,
            status,
            updatedAt);

    private sealed record RecentRepositoryRecord(string Reference, string DisplayReference, string PlantName,
        string DataType, string Status, DateTimeOffset UpdatedAt);

    private sealed record DashboardItem(
        string Name,
        string Description,
        string Reference,
        string Status,
        DateTimeOffset UpdatedAt);
}
