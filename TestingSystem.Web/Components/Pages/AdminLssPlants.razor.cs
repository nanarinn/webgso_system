using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Pages;

public partial class AdminLssPlants : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILssDataApiClient ApiClient { get; set; } = default!;

    private static readonly string[] States = ["Johor", "Kedah", "Melaka", "Negeri Sembilan", "Pahang", "Perak", "Perlis", "Pulau Pinang", "Sabah", "Sarawak", "Selangor", "Terengganu", "Kelantan"];
    private List<LssPlant> Plants { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private bool IsDialogOpen { get; set; }
    private bool IsApiTestDialogOpen { get; set; }
    private bool IsTestingApi { get; set; }
    private int? EditingPlantId { get; set; }
    private int TestPlantId { get; set; }
    private string TestApiSource { get; set; } = "DDQ";
    private string TestApiEndpoint { get; set; } = string.Empty;
    private LssApiTestResult? LastTestResult { get; set; }
    private string FormError { get; set; } = string.Empty;
    private string NewName { get; set; } = string.Empty;
    private string NewPlantId { get; set; } = string.Empty;
    private string NewState { get; set; } = "Johor";
    private int NewCapacity { get; set; } = 50;
    private string NewDdqApi { get; set; } = "https://api.example.com/ddq/collection";
    private string NewMmfApi { get; set; } = "https://api.example.com/mmf/collection";

    private List<LssPlant> FilteredPlants => string.IsNullOrWhiteSpace(SearchTerm)
        ? Plants
        : Plants.Where(x => x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || x.State.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || x.PlantId.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

    protected override async Task OnInitializedAsync() => await LoadPlantsAsync();

    private async Task LoadPlantsAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Plants = await database.LssPlants.OrderBy(x => x.Name).ToListAsync();
    }

    private void OpenAddDialog()
    {
        EditingPlantId = null;
        NewName = string.Empty;
        NewPlantId = $"PLT-{Plants.Count + 1:000}";
        NewState = "Johor";
        NewCapacity = 50;
        NewDdqApi = "https://api.example.com/ddq/collection";
        NewMmfApi = "https://api.example.com/mmf/collection";
        FormError = string.Empty;
        IsDialogOpen = true;
    }

    private void OpenEditDialog(LssPlant plant)
    {
        EditingPlantId = plant.Id;
        NewName = plant.Name;
        NewPlantId = plant.PlantId;
        NewState = plant.State;
        NewCapacity = (int)plant.CapacityMw;
        NewDdqApi = plant.DdqApiEndpoint;
        NewMmfApi = plant.MmfApiEndpoint;
        FormError = string.Empty;
        IsDialogOpen = true;
    }

    private void CloseDialog() => IsDialogOpen = false;

    private void OpenApiTestDialog()
    {
        TestPlantId = Plants[0].Id;
        TestApiSource = "DDQ";
        LastTestResult = null;
        LoadSelectedEndpoint();
        IsApiTestDialogOpen = true;
    }

    private void CloseApiTestDialog() => IsApiTestDialogOpen = false;

    private void LoadSelectedEndpoint()
    {
        var plant = Plants.First(item => item.Id == TestPlantId);
        TestApiEndpoint = TestApiSource == "DDQ" ? plant.DdqApiEndpoint : plant.MmfApiEndpoint;
        LastTestResult = null;
    }

    private async Task TestApiAsync()
    {
        IsTestingApi = true;
        try
        {
            LastTestResult = await ApiClient.TestConnectionAsync(TestApiEndpoint.Trim());
            await using var database = await ContextFactory.CreateDbContextAsync();
            var plant = await database.LssPlants.SingleAsync(item => item.Id == TestPlantId);
            if (TestApiSource == "DDQ") plant.DdqApiEndpoint = TestApiEndpoint.Trim();
            else plant.MmfApiEndpoint = TestApiEndpoint.Trim();
            plant.ApiConnectionStatus = LastTestResult.IsConnected ? "Connected" : "Failed";
            plant.LastApiTestSource = TestApiSource;
            plant.LastApiTestMessage = LastTestResult.Message;
            plant.LastApiTestAt = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync();
            await LoadPlantsAsync();
        }
        finally
        {
            IsTestingApi = false;
        }
    }

    private async Task SavePlantAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var plantId = NewPlantId.Trim();
        if (await database.LssPlants.AnyAsync(x => x.PlantId == plantId && x.Id != EditingPlantId))
        {
            FormError = $"Plant ID {plantId} is already registered.";
            return;
        }

        var plant = EditingPlantId.HasValue
            ? await database.LssPlants.SingleAsync(x => x.Id == EditingPlantId.Value)
            : new LssPlant();

        var apiChanged = EditingPlantId.HasValue
            && (plant.DdqApiEndpoint != NewDdqApi.Trim() || plant.MmfApiEndpoint != NewMmfApi.Trim());
        plant.PlantId = plantId;
        plant.Name = NewName.Trim();
        plant.State = NewState;
        plant.CapacityMw = NewCapacity;
        plant.DdqApiEndpoint = NewDdqApi.Trim();
        plant.MmfApiEndpoint = NewMmfApi.Trim();
        plant.IsActive = true;
        if (apiChanged)
        {
            plant.ApiConnectionStatus = "Not tested";
            plant.LastApiTestSource = null;
            plant.LastApiTestMessage = null;
            plant.LastApiTestAt = null;
        }

        if (!EditingPlantId.HasValue)
        {
            database.LssPlants.Add(plant);
        }
        await database.SaveChangesAsync();
        IsDialogOpen = false;
        await LoadPlantsAsync();
    }

    private static string GetCode(string name) => string.Concat(
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0]));

    private static string GetApiStatusClass(string status) => status.ToLowerInvariant().Replace(" ", string.Empty);
    private static string GetLastTestLabel(LssPlant plant) => plant.LastApiTestAt.HasValue
        ? $"{plant.LastApiTestSource} · {plant.LastApiTestAt.Value.ToLocalTime():dd MMM HH:mm}"
        : "No test recorded";
}
