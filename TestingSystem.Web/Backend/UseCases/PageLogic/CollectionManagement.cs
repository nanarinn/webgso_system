using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Services;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class CollectionManagement : ComponentBase, IAsyncDisposable
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILssDataApiClient ApiClient { get; set; } = default!;

    private List<PlantConnection> Plants { get; set; } = [];

    private List<DemoCollectionJob> Jobs { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedPlantName { get; set; } = string.Empty;
    private bool IsRunDialogOpen { get; set; }
    private bool IsSummaryDialogOpen { get; set; }
    private bool IsJobDetailOpen { get; set; }
    private DemoCollectionJob? SelectedJob { get; set; }
    private string SummaryFilter { get; set; } = "All";
    private DemoCollectionJob? RunResult { get; set; }
    private string LoadErrorMessage { get; set; } = string.Empty;
    private bool IsLoadingJobs { get; set; }
    private int Sequence { get; set; } = 6;
    private readonly CancellationTokenSource RefreshCancellation = new();
    private Task? RefreshTask { get; set; }

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
        await LoadPageAsync();
        RefreshTask = RefreshJobsAsync(RefreshCancellation.Token);
    }

    private async Task LoadPageAsync()
    {
        IsLoadingJobs = true;
        LoadErrorMessage = string.Empty;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            Plants = await database.LssPlants.AsNoTracking()
                .Where(plant => plant.IsActive)
                .OrderBy(plant => plant.Name)
                .Select(plant => new PlantConnection(plant.Id, plant.PlantId, plant.Name, plant.State, plant.DdqApiEndpoint))
                .ToListAsync();
            SelectedPlantName = Plants.FirstOrDefault()?.Name ?? string.Empty;
            await LoadJobsAsync();
        }
        catch (Exception exception)
        {
            Jobs = [];
            LoadErrorMessage = $"Collection history could not be loaded: {exception.GetBaseException().Message}";
        }
        finally
        {
            IsLoadingJobs = false;
        }
    }

    private async Task RetryLoadAsync()
    {
        await LoadPageAsync();
    }

    private async Task LoadJobsAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Jobs = await database.CollectionJobs.AsNoTracking().OrderByDescending(x => x.StartedAt)
            .Join(database.LssPlants, job => job.PlantId, plant => plant.Id, (job, plant) => new { job, plant })
            .Select(x => new DemoCollectionJob(
                x.job.CollectionId,
                x.plant.Name,
                x.plant.State,
                x.job.Mode,
                x.job.SubmissionDate.ToDateTime(TimeOnly.MinValue),
                x.job.CompletedAt.HasValue ? x.job.CompletedAt.Value.LocalDateTime : x.job.StartedAt.LocalDateTime,
                x.job.Status == "Completed" ? "Successful" : x.job.Status,
                x.job.Status == "Completed" ? "API data collected and stored in MS SQL." : (x.job.ErrorMessage ?? "Collection is still being processed."),
                x.job.ResponseMessage,
                x.job.DurationSeconds,
                x.job.Mode.Contains("MMF") || x.job.DataType == "MMF" ? "MMF" : "DDQ",
                x.job.DataType,
                "",
                0,
                0,
                null,
                "",
                x.job.Id,
                x.job.ErrorMessage ?? string.Empty))
            .Take(500)
            .ToListAsync();
        Sequence = Math.Max(Sequence, Jobs.Count);
    }

    private void OpenJobDetail(DemoCollectionJob job)
    {
        SelectedJob = job;
        IsJobDetailOpen = true;
    }

    private void CloseJobDetail()
    {
        IsJobDetailOpen = false;
        SelectedJob = null;
    }

    private async Task RefreshJobsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    try
                    {
                        await LoadJobsAsync();
                        LoadErrorMessage = string.Empty;
                    }
                    catch (Exception exception)
                    {
                        LoadErrorMessage = $"Automatic refresh failed: {exception.GetBaseException().Message}";
                    }
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        RefreshCancellation.Cancel();
        if (RefreshTask is not null)
            try { await RefreshTask; } catch (OperationCanceledException) { }
        RefreshCancellation.Dispose();
    }

    private void OpenRunDialog()
    {
        SelectedPlantName = Plants.FirstOrDefault()?.Name ?? string.Empty;
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
        await using var database = await ContextFactory.CreateDbContextAsync();
        database.CollectionJobs.Add(new CollectionJobEntity
        {
            CollectionId = job.CollectionId, PlantId = SelectedPlant.DatabaseId, DataType = "DDQ", Mode = "Manual",
            SubmissionDate = DateOnly.FromDateTime(job.SubmissionDate), StartedAt = job.RunAt,
            CompletedAt = job.RunAt.AddSeconds(job.DurationSeconds), Status = status == "Successful" ? "Completed" : "Failed",
            ErrorMessage = status == "Failed" ? apiResult.Message : null, ResponseMessage = apiResult.Message,
            DurationSeconds = job.DurationSeconds
        });
        await database.SaveChangesAsync();
        Jobs.Insert(0, job);
        RunResult = job;
    }

    private static string GetPlantCode(string plantName) => string.Concat(
        plantName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    private sealed record PlantConnection(int DatabaseId, string PlantId, string Name, string State, string ApiUrl);
}
