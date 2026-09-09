using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Pages;

public partial class Repository : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILssDataApiClient ApiClient { get; set; } = default!;

    private List<PlantConnection> Plants { get; set; } = [];

    private List<CollectionJob> Jobs { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedPlantName { get; set; } = string.Empty;
    private bool IsRunDialogOpen { get; set; }
    private bool IsSummaryDialogOpen { get; set; }
    private string SummaryFilter { get; set; } = "All";
    private CollectionJob? RunResult { get; set; }
    private int Sequence { get; set; } = 6;

    private List<CollectionJob> SummaryJobs => SummaryFilter == "All"
        ? Jobs
        : Jobs.Where(job => job.Status == SummaryFilter).ToList();

    private string SummaryDialogTitle => SummaryFilter == "All"
        ? "All collection jobs"
        : $"{SummaryFilter} collection jobs";

    private PlantConnection SelectedPlant => Plants.First(plant => plant.Name == SelectedPlantName);

    private List<CollectionJob> FilteredJobs => string.IsNullOrWhiteSpace(SearchTerm)
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
        var now = DateTime.Now;
        Jobs =
        [
            CreateJob(Plants[0], "Auto", "Successful", now.AddMinutes(-18), 1),
            CreateJob(Plants[1], "Manual", "Successful", now.AddHours(-2), 2),
            CreateJob(Plants[2], "Auto", "Failed", now.AddHours(-5), 3),
            CreateJob(Plants[3], "Auto", "Successful", now.AddDays(-1), 4),
            CreateJob(Plants[0], "Manual", "Failed", now.AddDays(-1).AddHours(-3), 5),
            CreateJob(Plants[1], "Auto", "Successful", now.AddDays(-2), 6)
        ];
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
        var job = CreateJob(SelectedPlant, "Manual", status, DateTime.Now, Sequence);
        job = job with { ResponseMessage = apiResult.Message };
        Jobs.Insert(0, job);
        RunResult = job;
    }

    private static CollectionJob CreateJob(PlantConnection plant, string mode, string status, DateTime runAt, int sequence)
    {
        var collectionId = $"DDQ-{runAt:yyyyMMdd}-{sequence:000}";
        var description = status == "Successful"
            ? "DDQ interval records collected and validated."
            : "Plant API did not return a complete interval dataset.";
        var responseMessage = status == "Successful"
            ? $"DDQ data from {plant.Name} was received and validated successfully."
            : $"Collection from {plant.Name} failed validation. Review the API configuration before retrying.";

        return new CollectionJob(collectionId, plant.Name, plant.State, mode, runAt.Date, runAt, status,
            description, responseMessage, 1.4 + (sequence % 6) * 0.7);
    }

    private static string GetPlantCode(string plantName) => string.Concat(
        plantName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    private sealed record PlantConnection(string PlantId, string Name, string State, string ApiUrl);
    private sealed record CollectionJob(string CollectionId, string PlantName, string State, string Mode,
        DateTime SubmissionDate, DateTime RunAt, string Status, string Description,
        string ResponseMessage, double DurationSeconds);
}
