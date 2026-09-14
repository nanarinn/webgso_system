using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class CollectionManagement : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILssDataApiClient ApiClient { get; set; } = default!;
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private const string DataCategory = "CollectionJobs";

    private List<PlantConnection> Plants { get; set; } = [];

    private List<DemoCollectionJob> Jobs { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedPlantName { get; set; } = string.Empty;
    private bool IsRunDialogOpen { get; set; }
    private bool IsSummaryDialogOpen { get; set; }
    private string SummaryFilter { get; set; } = "All";
    private DemoCollectionJob? RunResult { get; set; }
    private int Sequence { get; set; } = 6;

    private List<DemoCollectionJob> SummaryJobs => SummaryFilter == "All"
        ? Jobs
        : Jobs.Where(job => job.Status == SummaryFilter).ToList();

    private string SummaryDialogTitle => SummaryFilter == "All"
        ? "All collection jobs"
        : $"{SummaryFilter} collection jobs";

    private PlantConnection SelectedPlant => Plants.First(plant => plant.Name == SelectedPlantName);

    private List<DemoCollectionJob> FilteredJobs => string.IsNullOrWhiteSpace(SearchTerm)
        ? Jobs
        : Jobs.Where(job => job.PlantName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.State.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.CollectionId.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Mode.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Status.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || job.Description.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Plants = await database.LssPlants
            .Where(plant => plant.IsActive)
            .OrderBy(plant => plant.Name)
            .Select(plant => new PlantConnection(plant.PlantId, plant.Name, plant.State, plant.DdqApiEndpoint))
            .ToListAsync();
        SelectedPlantName = Plants[0].Name;
        var seed = DemoData.CollectionJobs(Plants.Select(p => p.Name).ToList(), Plants.Select(p => p.State).ToList(), DateTime.Now);
        await DataStore.SeedAsync(DataCategory, seed.Select(item => (item.CollectionId, item)));
        Jobs = await DataStore.LoadAsync<DemoCollectionJob>(DataCategory);
        Sequence = Math.Max(Sequence, Jobs.Count);
    }

    private void OpenRunDialog()
    {
        SelectedPlantName = Plants[0].Name;
        RunResult = null;
        IsRunDialogOpen = true;
    }

    private void CloseRunDialog()
    {
        IsRunDialogOpen = false;
        RunResult = null;
    }

    private void OpenSummaryDialog(string filter)
    {
        SummaryFilter = filter;
        IsSummaryDialogOpen = true;
    }

    private void OpenAllSummary() => OpenSummaryDialog("All");
    private void OpenSuccessfulSummary() => OpenSummaryDialog("Successful");
    private void OpenFailedSummary() => OpenSummaryDialog("Failed");

    private void CloseSummaryDialog() => IsSummaryDialogOpen = false;

    private async Task RunCollection()
    {
        Sequence++;
        var apiResult = await ApiClient.CollectAsync(SelectedPlant.Name, "DDQ", SelectedPlant.ApiUrl);
        var status = apiResult.IsSuccessful ? "Successful" : "Failed";
        var job = DemoData.CreateCollectionJob(SelectedPlant.Name, SelectedPlant.State, "Manual", status, DateTime.Now, Sequence);
        job = job with { ResponseMessage = apiResult.Message };
        await DataStore.SaveAsync(DataCategory, job.CollectionId, job);
        Jobs.Insert(0, job);
        RunResult = job;
    }

    private static string GetPlantCode(string plantName) => string.Concat(
        plantName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    private sealed record PlantConnection(string PlantId, string Name, string State, string ApiUrl);
}
