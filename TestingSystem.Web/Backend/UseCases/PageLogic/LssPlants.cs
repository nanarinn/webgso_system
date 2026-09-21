using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class LssPlants : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILssDataApiClient ApiClient { get; set; } = default!;

    private static readonly string[] States = ["Johor", "Kedah", "Melaka", "Negeri Sembilan", "Pahang", "Perak", "Perlis", "Pulau Pinang", "Sabah", "Sarawak", "Selangor", "Terengganu", "Kelantan"];
    private static readonly string[] ApiTypes = ["DDQ", "MMF"];
    private static readonly string[] DdqDataTypes = ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];
    private List<LssPlant> Plants { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedApiType { get; set; } = "DDQ";
    private bool IsDialogOpen { get; set; }
    private bool IsApiTestDialogOpen { get; set; }
    private bool IsTestingApi { get; set; }
    private int? EditingPlantId { get; set; }
    private int TestPlantId { get; set; }
    private string TestApiSource { get; set; } = "DDQ";
    private string TestApiEndpoint { get; set; } = string.Empty;
    private LssApiTestResult? LastTestResult { get; set; }
    private string FormError { get; set; } = string.Empty;
    private string SuccessMessage { get; set; } = string.Empty;
    private string SuccessTitle { get; set; } = "Successfully saved";
    private bool IsDeleteConfirmationOpen { get; set; }
    private string NewName { get; set; } = string.Empty;
    private string NewPlantId { get; set; } = string.Empty;
    private string NewState { get; set; } = "Johor";
    private int NewCapacity { get; set; } = 50;
    private string NewApiType { get; set; } = "DDQ";
    private string OriginalApiType { get; set; } = "DDQ";
    private string NewApiLink { get; set; } = string.Empty;
    private List<ApiLinkInput> NewApiLinks { get; set; } = [];

    private List<LssPlant> FilteredPlants => string.IsNullOrWhiteSpace(SearchTerm)
        ? Plants
        : Plants.Where(x => x.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || x.State.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
                || x.PlantId.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private List<PlantApiConfiguration> FilteredApiConfigurations => FilteredPlants.SelectMany(x => x.ApiConfigurations).ToList();

    protected override async Task OnInitializedAsync() => await LoadPlantsAsync();

    private async Task LoadPlantsAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Plants = await database.LssPlants.Include(x => x.ApiConfigurations).OrderBy(x => x.Name).ToListAsync();
    }

    private void OpenAddDialog()
    {
        EditingPlantId = null;
        NewName = string.Empty;
        NewPlantId = $"PLT-{Plants.Count + 1:000}";
        NewState = "Johor";
        NewCapacity = 50;
        NewApiType = "DDQ";
        OriginalApiType = "DDQ";
        NewApiLink = string.Empty;
        NewApiLinks = [new ApiLinkInput { DataType = "Rolling 24 Hour" }];
        FormError = string.Empty;
        IsDeleteConfirmationOpen = false;
        IsDialogOpen = true;
    }

    private void OpenEditDialog(LssPlant plant, string apiType)
    {
        EditingPlantId = plant.Id;
        NewName = plant.Name;
        NewPlantId = plant.PlantId;
        NewState = plant.State;
        NewCapacity = (int)plant.CapacityMw;
        NewApiType = apiType;
        OriginalApiType = apiType;
        NewApiLink = apiType == "DDQ" ? plant.DdqApiEndpoint : plant.MmfApiEndpoint;
        NewApiLinks = plant.ApiConfigurations.Where(x => x.ApiType == apiType)
            .Select(x => new ApiLinkInput { DataType = x.DataType, ApiLink = x.ApiLink }).ToList();
        if (NewApiLinks.Count == 0) NewApiLinks.Add(new ApiLinkInput { DataType = apiType == "DDQ" ? "Rolling 24 Hour" : "MMF" });
        FormError = string.Empty;
        IsDeleteConfirmationOpen = false;
        IsDialogOpen = true;
    }

    private void CloseDialog() => IsDialogOpen = false;
    private void ShowDeleteConfirmation() => IsDeleteConfirmationOpen = true;
    private void HideDeleteConfirmation() => IsDeleteConfirmationOpen = false;
    private void AddApiLink()
    {
        if (!CanAddApiLink) return;
        var dataType = DdqDataTypes.First(type => NewApiLinks.All(link => link.DataType != type));
        NewApiLinks.Add(new ApiLinkInput { DataType = dataType });
    }
    private void RemoveApiLink(ApiLinkInput input) { if (NewApiLinks.Count > 1) NewApiLinks.Remove(input); }
    private void ApiTypeChanged() => NewApiLinks = [new ApiLinkInput { DataType = NewApiType == "DDQ" ? "Rolling 24 Hour" : "MMF" }];
    private bool CanAddApiLink => NewApiType == "DDQ" && NewApiLinks.Count < DdqDataTypes.Length;
    private IEnumerable<string> AvailableDataTypes(ApiLinkInput current) => DdqDataTypes
        .Where(type => type == current.DataType || NewApiLinks.All(link => ReferenceEquals(link, current) || link.DataType != type));

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
        var wasEditing = EditingPlantId.HasValue;
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

        var apiLink = NewApiLinks.FirstOrDefault()?.ApiLink.Trim() ?? string.Empty;
        var previousApiLink = OriginalApiType == "DDQ" ? plant.DdqApiEndpoint : plant.MmfApiEndpoint;
        var apiChanged = EditingPlantId.HasValue
            && (OriginalApiType != NewApiType || previousApiLink != apiLink);
        plant.PlantId = plantId;
        plant.Name = NewName.Trim();
        plant.State = NewState;
        plant.CapacityMw = NewCapacity;
        if (EditingPlantId.HasValue && OriginalApiType != NewApiType)
        {
            if (OriginalApiType == "DDQ") plant.DdqApiEndpoint = string.Empty;
            else plant.MmfApiEndpoint = string.Empty;
        }
        if (NewApiType == "DDQ") plant.DdqApiEndpoint = apiLink;
        else plant.MmfApiEndpoint = apiLink;
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
        await database.PlantApiConfigurations.Where(x => x.PlantId == plant.Id && x.ApiType == NewApiType).ExecuteDeleteAsync();
        var links = NewApiLinks.Where(x => !string.IsNullOrWhiteSpace(x.ApiLink))
            .GroupBy(x => x.DataType).Select(x => x.Last()).ToList();
        database.PlantApiConfigurations.AddRange(links.Select(x => new PlantApiConfiguration
            { PlantId = plant.Id, ApiType = NewApiType, DataType = NewApiType == "DDQ" ? x.DataType : "MMF", ApiLink = x.ApiLink.Trim() }));
        await database.SaveChangesAsync();
        IsDialogOpen = false;
        await LoadPlantsAsync();
        SelectedApiType = NewApiType;
        SuccessMessage = $"{NewName.Trim()} {NewApiType} API configuration has been successfully {(wasEditing ? "updated" : "added")} to MS SQL.";
        SuccessTitle = "Successfully saved";
    }

    private async Task DeletePlantAsync()
    {
        if (!EditingPlantId.HasValue) return;
        await using var database = await ContextFactory.CreateDbContextAsync();
        var plant = await database.LssPlants.SingleOrDefaultAsync(x => x.Id == EditingPlantId.Value);
        if (plant is null)
        {
            FormError = "This plant no longer exists in MS SQL.";
            IsDeleteConfirmationOpen = false;
            return;
        }

        var deletedName = plant.Name;
        var deletedPlantId = plant.PlantId;
        await database.PlantApiConfigurations.Where(x => x.PlantId == plant.Id).ExecuteDeleteAsync();
        database.LssPlants.Remove(plant);
        await database.SaveChangesAsync();
        IsDialogOpen = false;
        IsDeleteConfirmationOpen = false;
        await LoadPlantsAsync();
        SuccessTitle = "Plant deleted";
        SuccessMessage = $"{deletedName} ({deletedPlantId}) and its API configurations have been successfully removed from MS SQL.";
    }

    private static string GetCode(string name) => string.Concat(
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0]));

    private sealed class ApiLinkInput { public string DataType { get; set; } = string.Empty; public string ApiLink { get; set; } = string.Empty; }
}
