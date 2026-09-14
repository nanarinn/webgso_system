using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;


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
    private List<SolarDocument> RecentDocuments { get; set; } = [];
    private List<DashboardItem> PlantItems { get; set; } = [];
    private List<DashboardItem> SuccessfulItems { get; set; } = [];
    private List<DashboardItem> FailedItems { get; set; } = [];
    private List<DashboardItem> MmfItems { get; set; } = [];
    private IReadOnlyList<DashboardItem> SelectedMetricItems { get; set; } = [];
    private string SelectedMetricTitle { get; set; } = string.Empty;
    private string SelectedMetricDescription { get; set; } = string.Empty;
    private string SelectedMetricCode { get; set; } = string.Empty;
    private bool IsMetricOpen { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var documents = await database.SolarDocuments
            .OrderByDescending(document => document.UpdatedAt)
            .ToListAsync();
        var plants = await database.LssPlants
            .Where(plant => plant.IsActive)
            .OrderBy(plant => plant.Name)
            .ToListAsync();

        RecentDocuments = documents.Take(4).ToList();
        PlantItems = plants.Select(plant => new DashboardItem(
            plant.Name,
            $"{plant.State} · {plant.CapacityMw:N0} MW · API configuration registered",
            plant.PlantId,
            plant.IsActive ? "Active" : "Inactive",
            plant.CreatedAt)).ToList();

        SuccessfulItems = documents
            .Where(document => document.ProjectStage == "Active")
            .Select(document => CreateRequestItem(document, "Successful"))
            .ToList();
        FailedItems = documents
            .Where(document => document.ProjectStage == "Review")
            .Select(document => CreateRequestItem(document, "Failed"))
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

    private static DashboardItem CreateRequestItem(SolarDocument document, string status) =>
        new(
            $"{document.ReferenceNumber} DDQ Submission",
            $"{document.Category} data request for {document.Title}",
            document.ReferenceNumber.Replace("LSS-", "DDQ-"),
            status,
            document.UpdatedAt);

    private sealed record DashboardItem(
        string Name,
        string Description,
        string Reference,
        string Status,
        DateTimeOffset UpdatedAt);
}
