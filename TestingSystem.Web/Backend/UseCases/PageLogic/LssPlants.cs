using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;
using TestingSystem.Web.Data;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class LssPlants : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private ILssDataApiClient ApiClient { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private IAuditTrailService AuditTrail { get; set; } = default!;

    private static readonly string[] States = ["Johor", "Kedah", "Melaka", "Negeri Sembilan", "Pahang", "Perak", "Perlis", "Pulau Pinang", "Sabah", "Sarawak", "Selangor", "Terengganu", "Kelantan"];
    private static readonly string[] ApiTypes = ["DDQ", "MMF"];
    private static readonly string[] DdqDataTypes = ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];
    private static readonly TimeZoneInfo MalaysiaTimeZone = ResolveMalaysiaTimeZone();
    private List<LssPlant> Plants { get; set; } = [];
    private string SearchTerm { get; set; } = string.Empty;
    private string SelectedApiType { get; set; } = "DDQ";
    private bool IsDialogOpen { get; set; }
    private bool IsApiTestDialogOpen { get; set; }
    private bool IsTestingApi { get; set; }
    private bool IsSavingPlant { get; set; }
    private int? EditingPlantId { get; set; }
    private int TestPlantId { get; set; }
    private string TestApiSource { get; set; } = "DDQ";
    private string TestDataType { get; set; } = "Rolling 24 Hour";
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
    private List<ApiLinkInput> NewApiLinks { get; set; } = [];
    private string NewMmfApiLink { get; set; } = string.Empty;
    private string MmfValidationState { get; set; } = "idle";
    private string MmfValidationMessage { get; set; } = string.Empty;

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
        Plants = await database.LssPlants.Include(x => x.ApiConfigurations)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }

    private void OpenAddDialog()
    {
        EditingPlantId = null;
        NewName = string.Empty;
        NewPlantId = $"PLT-{Plants.Count + 1:000}";
        NewState = "Johor";
        NewCapacity = 50;
        NewApiLinks = [new ApiLinkInput { DataType = "Rolling 24 Hour" }];
        NewMmfApiLink = string.Empty;
        MmfValidationState = "idle";
        MmfValidationMessage = string.Empty;
        FormError = string.Empty;
        IsDeleteConfirmationOpen = false;
        IsDialogOpen = true;
    }

    private void OpenEditDialog(LssPlant plant)
    {
        EditingPlantId = plant.Id;
        NewName = plant.Name;
        NewPlantId = plant.PlantId;
        NewState = plant.State;
        NewCapacity = (int)plant.CapacityMw;
        NewApiLinks = plant.ApiConfigurations.Where(x => x.ApiType == "DDQ")
            .Select(x => new ApiLinkInput { DataType = x.DataType, ApiLink = x.ApiLink }).ToList();
        if (NewApiLinks.Count == 0)
            NewApiLinks.Add(new ApiLinkInput { DataType = "Rolling 24 Hour", ApiLink = plant.DdqApiEndpoint });
        NewMmfApiLink = plant.ApiConfigurations.FirstOrDefault(x => x.ApiType == "MMF")?.ApiLink
            ?? plant.MmfApiEndpoint;
        MmfValidationState = "idle";
        MmfValidationMessage = string.Empty;
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
    private bool CanAddApiLink => NewApiLinks.Count < DdqDataTypes.Length;
    private IEnumerable<string> AvailableDataTypes(ApiLinkInput current) => DdqDataTypes
        .Where(type => type == current.DataType || NewApiLinks.All(link => ReferenceEquals(link, current) || link.DataType != type));

    private static string EndpointFieldClass(string state) => $"api-endpoint-field {state}";

    private static void ClearEndpointStatus(ApiLinkInput input)
    {
        input.ValidationState = "idle";
        input.ValidationMessage = string.Empty;
    }

    private void ClearMmfEndpointStatus()
    {
        MmfValidationState = "idle";
        MmfValidationMessage = string.Empty;
    }

    private void OpenApiTestDialog()
    {
        TestPlantId = Plants[0].Id;
        TestApiSource = "DDQ";
        TestDataType = "Rolling 24 Hour";
        LastTestResult = null;
        LoadSelectedEndpoint();
        IsApiTestDialogOpen = true;
    }

    private void CloseApiTestDialog() => IsApiTestDialogOpen = false;

    private IEnumerable<string> TestDataTypes => TestApiSource == "DDQ" ? DdqDataTypes : ["MMF"];

    private void TestApiSourceChanged()
    {
        TestDataType = TestApiSource == "DDQ" ? DdqDataTypes[0] : "MMF";
        LoadSelectedEndpoint();
    }

    private void LoadSelectedEndpoint()
    {
        var plant = Plants.First(item => item.Id == TestPlantId);
        TestApiEndpoint = plant.ApiConfigurations.FirstOrDefault(item =>
                item.ApiType == TestApiSource && item.DataType == TestDataType)?.ApiLink
            ?? (TestApiSource == "DDQ" ? plant.DdqApiEndpoint : plant.MmfApiEndpoint);
        LastTestResult = null;
    }

    private async Task TestApiAsync()
    {
        IsTestingApi = true;
        try
        {
            LastTestResult = await ApiClient.TestConnectionAsync(
                TestApiEndpoint.Trim(), TestApiSource, TestDataType);
            await using var database = await ContextFactory.CreateDbContextAsync();
            var plant = await database.LssPlants.SingleAsync(item => item.Id == TestPlantId);
            if (LastTestResult.IsConnected)
            {
                var endpoint = TestApiEndpoint.Trim();
                var configuration = await database.PlantApiConfigurations.SingleOrDefaultAsync(item =>
                    item.PlantId == TestPlantId && item.ApiType == TestApiSource && item.DataType == TestDataType);
                if (configuration is null)
                {
                    configuration = new PlantApiConfiguration
                    {
                        PlantId = TestPlantId,
                        ApiType = TestApiSource,
                        DataType = TestDataType
                    };
                    database.PlantApiConfigurations.Add(configuration);
                }
                configuration.ApiLink = endpoint;
                if (TestApiSource == "DDQ") plant.DdqApiEndpoint = endpoint;
                else plant.MmfApiEndpoint = endpoint;
            }
            plant.ApiConnectionStatus = LastTestResult.IsConnected ? "Connected" : "Failed";
            plant.LastApiTestSource = TestApiSource;
            plant.LastApiTestMessage = LastTestResult.Message;
            plant.LastApiTestAt = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync();
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, "Test plant API connection",
                LastTestResult.IsConnected ? "Success" : "Failed", "LssPlant", plant.PlantId,
                $"{TestApiSource} {TestDataType} endpoint test: {LastTestResult.Message}",
                $"plant:{plant.PlantId}");
            await LoadPlantsAsync();
        }
        finally
        {
            IsTestingApi = false;
        }
    }

    private async Task SavePlantAsync()
    {
        if (IsSavingPlant) return;
        IsSavingPlant = true;
        try
        {
            var wasEditing = EditingPlantId.HasValue;
            var plantId = NewPlantId.Trim();
            var ddqLinks = NewApiLinks.Select(link =>
                new PlantApiEndpointInput(link.DataType, link.ApiLink.Trim())).ToList();
            var validationError = LssPlantApiConfigurationPolicy.Validate(ddqLinks, NewMmfApiLink);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                FormError = validationError;
                return;
            }
            FormError = string.Empty;

            foreach (var input in NewApiLinks)
            {
                input.ValidationState = "checking";
                input.ValidationMessage = "Checking...";
            }
            MmfValidationState = "checking";
            MmfValidationMessage = "Checking...";
            await InvokeAsync(StateHasChanged);

            var ddqVerificationTasks = NewApiLinks.Select(async input => new
            {
                Input = input,
                Result = await ApiClient.TestConnectionAsync(
                    input.ApiLink.Trim(), "DDQ", input.DataType)
            }).ToList();
            var mmfVerificationTask = ApiClient.TestConnectionAsync(
                NewMmfApiLink.Trim(), "MMF", "MMF");
            var ddqVerifications = await Task.WhenAll(ddqVerificationTasks);
            var mmfVerification = await mmfVerificationTask;

            foreach (var verification in ddqVerifications)
            {
                verification.Input.ValidationState = verification.Result.IsConnected ? "verified" : "failed";
                verification.Input.ValidationMessage = verification.Result.IsConnected
                    ? "Verified"
                    : "Invalid DDQ API";
            }
            MmfValidationState = mmfVerification.IsConnected ? "verified" : "failed";
            MmfValidationMessage = mmfVerification.IsConnected
                ? "Verified"
                : "Invalid MMF API";

            var failedDdq = ddqVerifications.FirstOrDefault(item => !item.Result.IsConnected);
            if (failedDdq is not null || !mmfVerification.IsConnected)
            {
                FormError = "API verification failed. Check the red field.";
                return;
            }

            await using var database = await ContextFactory.CreateDbContextAsync();
            if (await database.LssPlants.AnyAsync(x => x.PlantId == plantId && x.Id != EditingPlantId))
            {
                FormError = $"Plant ID {plantId} is already registered.";
                return;
            }

            var plant = EditingPlantId.HasValue
                ? await database.LssPlants.Include(x => x.ApiConfigurations).SingleAsync(x => x.Id == EditingPlantId.Value)
                : new LssPlant();

            var mmfApiLink = NewMmfApiLink.Trim();
            plant.PlantId = plantId;
            plant.Name = NewName.Trim();
            plant.State = NewState;
            plant.CapacityMw = NewCapacity;
            plant.DdqApiEndpoint = ddqLinks[0].ApiLink;
            plant.MmfApiEndpoint = mmfApiLink;
            plant.IsActive = true;
            plant.ApiConnectionStatus = "Connected";
            plant.LastApiTestSource = "DDQ & MMF";
            plant.LastApiTestMessage = $"Verified {ddqLinks.Count} DDQ endpoint(s) and one MMF endpoint before saving.";
            plant.LastApiTestAt = DateTimeOffset.UtcNow;

            if (!EditingPlantId.HasValue) database.LssPlants.Add(plant);
            await database.SaveChangesAsync();
            await database.PlantApiConfigurations.Where(x => x.PlantId == plant.Id).ExecuteDeleteAsync();
            database.PlantApiConfigurations.AddRange(ddqLinks.Select(link => new PlantApiConfiguration
                { PlantId = plant.Id, ApiType = "DDQ", DataType = link.DataType, ApiLink = link.ApiLink }));
            database.PlantApiConfigurations.Add(new PlantApiConfiguration
                { PlantId = plant.Id, ApiType = "MMF", DataType = "MMF", ApiLink = mmfApiLink });
            await database.SaveChangesAsync();
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, wasEditing ? "Update LSS plant configuration" : "Create LSS plant configuration",
                "Success", "LssPlant", plant.PlantId,
                $"Plant {plant.Name}; state {plant.State}; capacity {plant.CapacityMw:0.##} MW; {ddqLinks.Count} DDQ endpoint(s); 1 MMF endpoint.",
                $"plant:{plant.PlantId}");
            IsDialogOpen = false;
            await LoadPlantsAsync();
            SelectedApiType = "DDQ";
            SuccessMessage = $"{NewName.Trim()} DDQ and MMF API configurations were verified and successfully {(wasEditing ? "updated" : "added")} in MS SQL.";
            SuccessTitle = "Successfully verified and saved";
        }
        catch (Exception exception)
        {
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal,
                EditingPlantId.HasValue ? "Update LSS plant configuration" : "Create LSS plant configuration",
                "Failed", "LssPlant", NewPlantId, exception.GetBaseException().Message,
                $"plant:{NewPlantId}");
            FormError = $"The plant configuration could not be saved: {exception.GetBaseException().Message}";
        }
        finally
        {
            IsSavingPlant = false;
        }
    }

    private async Task DeletePlantAsync()
    {
        if (!EditingPlantId.HasValue) return;
        FormError = string.Empty;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            var plant = await database.LssPlants.AsNoTracking()
                .Where(x => x.Id == EditingPlantId.Value)
                .Select(x => new { x.Id, x.Name, x.PlantId })
                .SingleOrDefaultAsync();
            if (plant is null)
            {
                FormError = "This plant no longer exists in MS SQL.";
                IsDeleteConfirmationOpen = false;
                return;
            }

            var collectionInProgress = await database.CollectionJobs.AsNoTracking().AnyAsync(x =>
                x.PlantId == plant.Id && x.Status == CollectionJobStatus.Processing);
            if (collectionInProgress)
            {
                IsDeleteConfirmationOpen = false;
                FormError = "This plant is currently collecting API data. Wait until the collection finishes, then delete it again.";
                return;
            }

            var deletedName = plant.Name;
            var deletedPlantId = plant.PlantId;
            var strategy = database.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await database.Database.BeginTransactionAsync();
                // Collection job dependants use ON DELETE CASCADE, so SQL Server can remove
                // responses, readings and anomalies without repeated table scans here.
                await database.CollectionJobs.Where(x => x.PlantId == plant.Id).ExecuteDeleteAsync();
                await database.CollectionSchedules.Where(x => x.PlantId == plant.Id).ExecuteDeleteAsync();
                await database.PlantApiConfigurations.Where(x => x.PlantId == plant.Id).ExecuteDeleteAsync();
                await database.LssPlants.Where(x => x.Id == plant.Id).ExecuteDeleteAsync();
                await transaction.CommitAsync();
            });

            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, "Delete LSS plant configuration", "Success",
                "LssPlant", deletedPlantId,
                $"Deleted {deletedName} and its API configurations, collection schedules, collection jobs, readings and anomalies.",
                $"plant:{deletedPlantId}");

            IsDialogOpen = false;
            IsDeleteConfirmationOpen = false;
            await LoadPlantsAsync();
            SuccessTitle = "Plant deleted";
            SuccessMessage = $"{deletedName} ({deletedPlantId}) and all related collection data have been successfully removed from MS SQL.";
        }
        catch (Exception exception)
        {
            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, "Delete LSS plant configuration", "Failed",
                "LssPlant", NewPlantId, exception.GetBaseException().Message, $"plant:{NewPlantId}");
            IsDeleteConfirmationOpen = false;
            FormError = $"The plant could not be deleted: {exception.GetBaseException().Message}";
        }
    }

    private static string GetCode(string name) => string.Concat(
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0]));

    private static string FormatRegisteredDate(DateTimeOffset timestamp) =>
        TimeZoneInfo.ConvertTime(timestamp, MalaysiaTimeZone).ToString("dd MMM yyyy");

    private static string FormatRegisteredTime(DateTimeOffset timestamp) =>
        TimeZoneInfo.ConvertTime(timestamp, MalaysiaTimeZone).ToString("HH:mm");

    private static TimeZoneInfo ResolveMalaysiaTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuala_Lumpur", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("MYT", TimeSpan.FromHours(8), "Malaysia Time", "Malaysia Time");
    }

    private sealed class ApiLinkInput
    {
        public string DataType { get; set; } = string.Empty;
        public string ApiLink { get; set; } = string.Empty;
        public string ValidationState { get; set; } = "idle";
        public string ValidationMessage { get; set; } = string.Empty;
    }
}
