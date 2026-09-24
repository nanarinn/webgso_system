using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using System.Text;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Services;
using TestingSystem.Web.Backend.UseCases.UCLSS027Audit;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class OnDemandReport : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private IAuditTrailService AuditTrail { get; set; } = default!;
    private List<DemoAnalysisResult> Results { get; set; } = [];
    private DemoAnalysisResult? SelectedResult { get; set; }
    private bool IsGenerateDialogOpen { get; set; }
    private bool IsSummaryDialogOpen { get; set; }
    private string SummaryFilter { get; set; } = "All";
    private string SelectedType { get; set; } = "DDQ";
    private static readonly string[] DdqDataTypes = ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];
    private string SelectedDataType { get; set; } = DdqDataTypes[0];
    private DateTime SelectedSubmissionDate { get; set; } = DateTime.Today;
    private int GenerationSequence { get; set; } = 3;
    private int ActivePlantCount { get; set; }
    private string FormError { get; set; } = string.Empty;
    private bool IsGenerating { get; set; }
    private string CurrentRequesterKey { get; set; } = string.Empty;

    private List<DemoAnalysisResult> SummaryResults => SummaryFilter == "All"
        ? Results
        : Results.Where(result => result.Status == SummaryFilter).ToList();

    private string SummaryDialogTitle => SummaryFilter == "All"
        ? "All generated requests"
        : $"{SummaryFilter} requests";

    protected override async Task OnInitializedAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        CurrentRequesterKey = OnDemandReportOwnership.GetRequesterKey(authenticationState.User);
        await using var database = await ContextFactory.CreateDbContextAsync();
        Results = string.IsNullOrWhiteSpace(CurrentRequesterKey)
            ? []
            : await database.ReportExecutions.AsNoTracking()
                .Where(x => x.ReportType == "On-Demand" && x.RequestedBy == CurrentRequesterKey)
                .OrderByDescending(x => x.RequestedAt).Select(x => new DemoAnalysisResult(x.ReportId, x.Name, x.DataType,
                    x.SubmissionDate, x.RequestedAt.LocalDateTime, x.Description,
                    x.GenerationStatus == "Completed" ? "Successful" : x.GenerationStatus,
                    x.DataSelection, x.PlantCount, x.RecordCount, x.AnomalyCount)).ToListAsync();
        GenerationSequence = Math.Max(GenerationSequence, Results.Count);
        ActivePlantCount = await database.LssPlants.AsNoTracking().CountAsync(x => x.IsActive);
    }

    private void OpenGenerateDialog()
    {
        SelectedType = "DDQ";
        SelectedDataType = DdqDataTypes[0];
        SelectedSubmissionDate = DateTime.Today;
        FormError = string.Empty;
        IsGenerateDialogOpen = true;
    }

    private void CloseGenerateDialog() => IsGenerateDialogOpen = false;

    private void OpenSummaryDialog(string filter)
    {
        SummaryFilter = filter;
        IsSummaryDialogOpen = true;
    }

    private void OpenAllSummary() => OpenSummaryDialog("All");
    private void OpenSuccessfulSummary() => OpenSummaryDialog("Successful");
    private void OpenFailedSummary() => OpenSummaryDialog("Failed");

    private void CloseSummaryDialog() => IsSummaryDialogOpen = false;

    private async Task GenerateAnalysis()
    {
        if (IsGenerating) return;
        FormError = string.Empty;
        if (string.IsNullOrWhiteSpace(CurrentRequesterKey)) { FormError = "Your signed-in account could not be identified. Sign out and sign in again."; return; }
        if (SelectedSubmissionDate.Date > DateTime.Today) { FormError = "Submission date cannot be in the future."; return; }
        IsGenerating = true;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            var start = SelectedSubmissionDate.Date;
            var end = start.AddDays(1);
            var activePlantIds = await database.LssPlants.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync();
            var storedDataType = SelectedType == "DDQ" ? SelectedDataType : "MMF";
            var query = database.SolarApiReadings.AsNoTracking().Where(x => activePlantIds.Contains(x.PlantId)
                && x.DataType == storedDataType && x.SubmissionDateTime >= start && x.SubmissionDateTime < end);
            var recordCount = await query.CountAsync();
            var submissionDate = DateOnly.FromDateTime(SelectedSubmissionDate);
            var anomalyCount = await (from anomaly in database.DataAnomalies.AsNoTracking()
                join job in database.CollectionJobs.AsNoTracking() on anomaly.CollectionJobId equals job.Id
                where activePlantIds.Contains(job.PlantId)
                    && job.SubmissionDate == submissionDate
                    && anomaly.DataType == SelectedType
                    && (SelectedType == "MMF" || job.DataType == storedDataType)
                select anomaly.Id).CountAsync();
            GenerationSequence++;
            var generatedAt = DateTime.Now;
            var status = recordCount > 0 ? "Successful" : "Failed";
            var selection = SelectedType == "DDQ" ? SelectedDataType : "All MMF parameters";
            var result = DemoData.CreateAnalysis(
                SelectedType,
                SelectedSubmissionDate,
                generatedAt,
                status,
                GenerationSequence,
                selection,
                activePlantIds.Count,
                recordCount,
                anomalyCount) with { Reference = OnDemandReportReference.Create(SelectedType, generatedAt) };
            database.ReportExecutions.Add(new ReportExecution
            {
                ReportId = result.Reference, ReportType = "On-Demand", DataType = result.Type, RequestedBy = CurrentRequesterKey,
                RequestedAt = generatedAt, GenerationStatus = status == "Successful" ? "Completed" : "Failed",
                DeliveryStatus = "NotRequired", Name = result.Name, SubmissionDate = result.SubmissionDate,
                DataSelection = result.DataSelection, Description = result.Description, PlantCount = result.PlantCount,
                RecordCount = result.RecordCount, AnomalyCount = result.AnomalyCount,
                ErrorMessage = status == "Failed" ? "No matching MS SQL readings were found." : null
            });
            await database.SaveChangesAsync();
            Results.Insert(0, result);
            AnalysisPage = 1;
            IsGenerateDialogOpen = false;
        }
        catch (Exception exception)
        {
            FormError = $"Analysis could not be generated: {exception.GetBaseException().Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private void ViewResult(DemoAnalysisResult result) => SelectedResult = result;
    private void CloseResult() => SelectedResult = null;

    private async Task ExportResultAsync(DemoAnalysisResult result)
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var start = result.SubmissionDate.Date;
        var end = start.AddDays(1);
        var plants = await database.LssPlants.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.PlantId).ThenBy(x => x.Name).ToListAsync();
        var plantIds = plants.Select(plant => plant.Id).ToList();
        var storedDataType = result.Type == "DDQ" ? result.DataSelection : "MMF";
        if (string.IsNullOrWhiteSpace(storedDataType)) storedDataType = result.Type == "DDQ" ? "Rolling 24 Hour" : "MMF";
        var query = database.SolarApiReadings.AsNoTracking().Where(x => plantIds.Contains(x.PlantId)
            && x.DataType == storedDataType && x.SubmissionDateTime >= start && x.SubmissionDateTime < end);
        var readings = await query.OrderByDescending(x => x.Id).ToListAsync();
        var csv = result.Type == "DDQ"
            ? OnDemandCsvExporter.BuildDdq(storedDataType, result.SubmissionDate, plants, readings)
            : OnDemandCsvExporter.BuildMmf(result.SubmissionDate, plants, readings);
        var bytes = new UTF8Encoding(true).GetBytes(csv);
        var fileName = GetCsvFileName(result);
        await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName, "text/csv;charset=utf-8", Convert.ToBase64String(bytes));
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        await AuditTrail.RecordAsync(authenticationState.User, "Download on-demand analysis", "Success",
            "Download", fileName,
            $"Downloaded {result.Type} {result.DataSelection} analysis for submission date {result.SubmissionDate:dd MMM yyyy}; report {result.Reference}.");
    }

    private static string GetCsvFileName(DemoAnalysisResult result)
        => $"{GetFileStem(result)}.csv";

    private async Task ExportAnomalyResultAsync(DemoAnalysisResult result)
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var submissionDate = DateOnly.FromDateTime(result.SubmissionDate);
        var storedDataType = result.Type == "DDQ" ? result.DataSelection : "MMF";
        if (string.IsNullOrWhiteSpace(storedDataType)) storedDataType = result.Type == "DDQ" ? "Rolling 24 Hour" : "MMF";

        var rows = await (from anomaly in database.DataAnomalies.AsNoTracking()
            join job in database.CollectionJobs.AsNoTracking() on anomaly.CollectionJobId equals job.Id
            join plant in database.LssPlants.AsNoTracking() on job.PlantId equals plant.Id
            where plant.IsActive
                && job.SubmissionDate == submissionDate
                && anomaly.DataType == result.Type
                && (result.Type == "MMF" || job.DataType == storedDataType)
            orderby anomaly.DetectedAt, plant.PlantId, anomaly.FieldName
            select new AnomalyCsvRow(job.CollectionId, plant.PlantId, plant.Name, anomaly.FieldName,
                anomaly.OriginalValue, anomaly.Reason, anomaly.Status, anomaly.DetectedAt)).ToListAsync();

        var csv = new StringBuilder();
        AddCsvRow(csv, "Collection ID", "Plant ID", "Plant Name", "API Type", "Data Type", "Submission Date",
            "Field / Parameter", "Original Value", "Reason", "Status", "Detected At");
        foreach (var row in rows)
        {
            AddCsvRow(csv, row.CollectionId, row.PlantId, row.PlantName, result.Type, result.DataSelection,
                result.SubmissionDate.ToString("yyyy-MM-dd"), row.FieldName, row.OriginalValue ?? "Missing",
                row.Reason, row.Status, row.DetectedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        var bytes = new UTF8Encoding(true).GetBytes(csv.ToString());
        var fileName = $"{GetFileStem(result)}_Anomaly.csv";
        await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName,
            "text/csv;charset=utf-8", Convert.ToBase64String(bytes));
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        await AuditTrail.RecordAsync(authenticationState.User, "Download anomaly report", "Success",
            "Download", fileName,
            $"Downloaded {rows.Count} anomaly record(s) for {result.Type} {result.DataSelection}; report {result.Reference}.");
    }

    private static string GetFileStem(DemoAnalysisResult result)
    {
        var reportType = result.Type.Trim().ToUpperInvariant();
        var dataType = string.IsNullOrWhiteSpace(result.DataSelection)
            ? result.Type
            : result.DataSelection;
        var safeDataType = string.Join("_", dataType.Split(
            Path.GetInvalidFileNameChars().Concat([' ']).ToArray(),
            StringSplitOptions.RemoveEmptyEntries));

        return $"{reportType}_{safeDataType}_{result.SubmissionDate:yyyyMMdd}";
    }

    private static void AddCsvRow(StringBuilder output, params string[] values)
        => output.AppendJoin(',', values.Select(value => $"\"{value.Replace("\"", "\"\"")}\"")).Append("\r\n");

    private sealed record AnomalyCsvRow(string CollectionId, string PlantId, string PlantName, string FieldName,
        string? OriginalValue, string Reason, string Status, DateTimeOffset DetectedAt);
}
