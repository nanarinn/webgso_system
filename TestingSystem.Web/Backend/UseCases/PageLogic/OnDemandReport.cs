using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using System.Text;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
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
    private static readonly (string Label, int Value)[] DataIntervals =
        [("15 minutes", 15), ("30 minutes", 30), ("1 hour", 60)];
    private string SelectedDataType { get; set; } = DdqDataTypes[0];
    private int SelectedInterval { get; set; } = 15;
    private DateTime SelectedSubmissionDate { get; set; } = DateTime.Today;
    private DateTime MmfRangeStart { get; set; } = DateTime.Today;
    private DateTime MmfRangeEnd { get; set; } = DateTime.Today.AddDays(1).AddMinutes(-15);
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
            : (await database.ReportExecutions.AsNoTracking()
                .Where(x => x.ReportType == "On-Demand" && x.RequestedBy == CurrentRequesterKey)
                .OrderByDescending(x => x.RequestedAt).Select(x => new DemoAnalysisResult(x.ReportId, x.Name, x.DataType,
                    x.SubmissionDate, x.RequestedAt.LocalDateTime, x.Description,
                    x.GenerationStatus == "Completed" ? "Successful" : x.GenerationStatus,
                    x.DataSelection, x.PlantCount, x.RecordCount, x.AnomalyCount,
                    x.IntervalMinutes, x.DataRangeStart, x.DataRangeEnd)).ToListAsync())
                .Select(result => result with { Name = DemoData.SimplifyAnalysisName(result.Name) })
                .ToList();
        GenerationSequence = Math.Max(GenerationSequence, Results.Count);
        ActivePlantCount = await database.LssPlants.AsNoTracking().CountAsync(x => x.IsActive);
    }

    private async Task OpenGenerateDialog()
    {
        SelectedType = "DDQ";
        SelectedDataType = DdqDataTypes[0];
        SelectedInterval = 15;
        await using var database = await ContextFactory.CreateDbContextAsync();
        var latestDdqRun = await database.CollectionJobs.AsNoTracking()
            .Where(job => job.Status == CollectionJobStatus.Completed && job.DataType != "MMF"
                && database.SolarApiReadings.Any(reading => reading.CollectionJobId == job.Id))
            .OrderByDescending(job => job.SubmissionDateTime)
            .Select(job => new { job.DataType, job.SubmissionDateTime })
            .FirstOrDefaultAsync();
        if (latestDdqRun is not null)
        {
            SelectedDataType = DdqDatasetPolicy.CanonicalName(latestDdqRun.DataType);
            SelectedSubmissionDate = TruncateToMinute(latestDdqRun.SubmissionDateTime);
            SelectedInterval = DdqDatasetPolicy.Get(SelectedDataType).ReferenceTemplateIntervalMinutes;
        }
        else
        {
            SelectedSubmissionDate = DateTime.Today;
        }
        MmfRangeStart = DateTime.Today;
        MmfRangeEnd = DateTime.Today.AddDays(1).AddMinutes(-15);
        FormError = string.Empty;
        IsGenerateDialogOpen = true;
    }

    private async Task OnDdqDataTypeChanged(ChangeEventArgs args)
    {
        var requestedType = DdqDatasetPolicy.CanonicalName(args.Value?.ToString());
        SelectedDataType = requestedType;
        SelectedInterval = DdqDatasetPolicy.Get(requestedType).ReferenceTemplateIntervalMinutes;
        await using var database = await ContextFactory.CreateDbContextAsync();
        var latestSubmission = await database.CollectionJobs.AsNoTracking()
            .Where(job => job.Status == CollectionJobStatus.Completed && job.DataType == requestedType
                && database.SolarApiReadings.Any(reading => reading.CollectionJobId == job.Id))
            .OrderByDescending(job => job.SubmissionDateTime)
            .Select(job => (DateTime?)job.SubmissionDateTime)
            .FirstOrDefaultAsync();
        if (latestSubmission.HasValue)
            SelectedSubmissionDate = TruncateToMinute(latestSubmission.Value);
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
        if (SelectedType == "DDQ" && SelectedSubmissionDate > DateTime.Now.AddMinutes(1)) { FormError = "Submission date and time cannot be in the future."; return; }
        if (!DataIntervals.Any(interval => interval.Value == SelectedInterval)) { FormError = "Select a valid data interval."; return; }
        if (SelectedType == "MMF" && MmfRangeEnd < MmfRangeStart) { FormError = "MMF data range end must be the same as or later than the start."; return; }
        IsGenerating = true;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            var activePlantIds = await database.LssPlants.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync();
            var storedDataType = SelectedType == "DDQ" ? SelectedDataType : "MMF";
            var selectedSubmissionEnd = SelectedSubmissionDate.AddMinutes(1);
            var query = database.PublishedReadings().Where(x => activePlantIds.Contains(x.PlantId)
                && x.DataType == storedDataType
                && (x.IntervalStart.Hour * 60 + x.IntervalStart.Minute) % SelectedInterval == 0);
            if (SelectedType == "MMF")
                query = query.Where(x => x.IntervalStart >= MmfRangeStart && x.IntervalStart <= MmfRangeEnd);
            else
                query = query.Where(x => x.SubmissionDateTime >= SelectedSubmissionDate
                    && x.SubmissionDateTime < selectedSubmissionEnd);
            var recordCount = await query.CountAsync();
            var anomalyCount = await (from anomaly in database.DataAnomalies.AsNoTracking()
                join job in database.CollectionJobs.AsNoTracking() on anomaly.CollectionJobId equals job.Id
                where activePlantIds.Contains(job.PlantId)
                    && anomaly.DataType == SelectedType
                    && (SelectedType == "MMF" || job.DataType == storedDataType)
                    && (SelectedType == "MMF"
                        ? job.SubmissionDateTime >= MmfRangeStart && job.SubmissionDateTime <= MmfRangeEnd
                        : job.SubmissionDateTime >= SelectedSubmissionDate
                            && job.SubmissionDateTime < selectedSubmissionEnd)
                select anomaly.Id).CountAsync();
            GenerationSequence++;
            var generatedAt = DateTime.Now;
            var status = recordCount > 0 ? "Successful" : "Failed";
            var selection = SelectedType == "DDQ" ? SelectedDataType : "All MMF parameters";
            var result = DemoData.CreateAnalysis(
                SelectedType,
                SelectedType == "MMF" ? MmfRangeStart : SelectedSubmissionDate,
                generatedAt,
                status,
                GenerationSequence,
                selection,
                activePlantIds.Count,
                recordCount,
                anomalyCount,
                SelectedInterval,
                SelectedType == "MMF" ? MmfRangeStart : null,
                SelectedType == "MMF" ? MmfRangeEnd : null) with
                { Reference = OnDemandReportReference.Create(SelectedType, generatedAt) };
            var preparedExport = await PrepareExportAsync(result);
            result = result with
            {
                AnomalyCount = preparedExport.DetectedAnomalies.Count + preparedExport.Prepared.Details.Count
            };
            database.ReportExecutions.Add(new ReportExecution
            {
                ReportId = result.Reference, ReportType = "On-Demand", DataType = result.Type, RequestedBy = CurrentRequesterKey,
                RequestedAt = generatedAt, GenerationStatus = status == "Successful" ? "Completed" : "Failed",
                DeliveryStatus = "NotRequired", Name = result.Name, SubmissionDate = result.SubmissionDate,
                DataSelection = result.DataSelection, Description = result.Description, PlantCount = result.PlantCount,
                IntervalMinutes = result.IntervalMinutes, DataRangeStart = result.DataRangeStart, DataRangeEnd = result.DataRangeEnd,
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
        var export = await PrepareExportAsync(result);
        var csv = result.Type == "DDQ"
            ? OnDemandCsvExporter.BuildDdq(export.StoredDataType, result.SubmissionDate, export.Plants,
                export.Prepared.Readings, result.IntervalMinutes)
            : OnDemandCsvExporter.BuildMmf(result.SubmissionDate, export.Plants, export.Prepared.Readings,
                result.DataRangeStart, result.DataRangeEnd);
        var bytes = new UTF8Encoding(true).GetBytes(csv);
        var fileName = GetCsvFileName(result);
        await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName, "text/csv;charset=utf-8", Convert.ToBase64String(bytes));
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        await AuditTrail.RecordAsync(authenticationState.User, "Download on-demand analysis", "Success",
            "Download", fileName,
            result.Type == "MMF"
                ? $"Downloaded MMF {result.DataSelection} analysis at {IntervalText(result.IntervalMinutes)} interval for data timestamps {result.DataRangeStart:dd MMM yyyy HH:mm} to {result.DataRangeEnd:dd MMM yyyy HH:mm}; report {result.Reference}."
                : $"Downloaded DDQ {result.DataSelection} analysis at {IntervalText(result.IntervalMinutes)} interval for submission date and time {result.SubmissionDate:dd MMM yyyy HH:mm}; report {result.Reference}.");
    }

    private static string GetCsvFileName(DemoAnalysisResult result)
        => $"{GetFileStem(result)}.csv";

    private static string IntervalText(int minutes) => minutes == 60 ? "1 hour" : $"{minutes} minutes";

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    private async Task ExportAnomalyResultAsync(DemoAnalysisResult result)
    {
        var export = await PrepareExportAsync(result);
        var csv = ReportAnomalyCsvExporter.Build(result.Type, export.StoredDataType,
            result.Type == "MMF" ? result.DataRangeStart ?? result.SubmissionDate : result.SubmissionDate,
            export.Plants, export.OriginalReadings, export.Prepared, export.DetectedAnomalies);
        var bytes = new UTF8Encoding(true).GetBytes(csv);
        var fileName = $"{GetFileStem(result)}_Anomaly.csv";
        await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName,
            "text/csv;charset=utf-8", Convert.ToBase64String(bytes));
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        await AuditTrail.RecordAsync(authenticationState.User, "Download anomaly report", "Success",
            "Download", fileName,
            $"Downloaded {export.DetectedAnomalies.Count + export.Prepared.Details.Count} anomaly and substitution record(s) for {result.Type} {result.DataSelection}; report {result.Reference}.");
    }

    private async Task<OnDemandPreparedExport> PrepareExportAsync(DemoAnalysisResult result)
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var plants = await database.LssPlants.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.PlantId).ThenBy(x => x.Name).ToListAsync();
        var plantIds = plants.Select(plant => plant.Id).ToList();
        var storedDataType = result.Type == "DDQ" ? result.DataSelection : "MMF";
        if (string.IsNullOrWhiteSpace(storedDataType)) storedDataType = result.Type == "DDQ" ? "Rolling 24 Hour" : "MMF";
        var submissionEnd = result.SubmissionDate.AddMinutes(1);
        var query = database.PublishedReadings().Where(x => plantIds.Contains(x.PlantId)
            && x.DataType == storedDataType
            && (x.IntervalStart.Hour * 60 + x.IntervalStart.Minute) % result.IntervalMinutes == 0);
        if (result.Type == "MMF" && result.DataRangeStart.HasValue && result.DataRangeEnd.HasValue)
            query = query.Where(x => x.IntervalStart >= result.DataRangeStart.Value
                && x.IntervalStart <= result.DataRangeEnd.Value);
        else
            query = query.Where(x => x.SubmissionDateTime >= result.SubmissionDate
                && x.SubmissionDateTime < submissionEnd);
        var readings = await query.OrderByDescending(x => x.Id).ToListAsync();
        List<SolarApiReading> previousReadings;
        if (result.Type == "MMF" && result.DataRangeStart.HasValue && result.DataRangeEnd.HasValue)
        {
            var previousStart = result.DataRangeStart.Value.AddDays(-1);
            var previousEnd = result.DataRangeEnd.Value.AddDays(-1);
            previousReadings = await database.PublishedReadings().Where(x => plantIds.Contains(x.PlantId)
                && x.DataType == "MMF" && x.IntervalStart >= previousStart && x.IntervalStart <= previousEnd)
                .OrderByDescending(x => x.Id).ToListAsync();
        }
        else
        {
            var previousSubmission = result.SubmissionDate.AddDays(-1);
            var previousSubmissionEnd = previousSubmission.AddMinutes(1);
            previousReadings = await database.PublishedReadings().Where(x => plantIds.Contains(x.PlantId)
                && x.DataType == storedDataType && x.SubmissionDateTime >= previousSubmission
                && x.SubmissionDateTime < previousSubmissionEnd)
                .OrderByDescending(x => x.Id).ToListAsync();
        }
        var rules = await database.ValidationRules.AsNoTracking()
            .Where(rule => rule.DataType == result.Type && rule.IsActive).ToListAsync();
        var prepared = result.Type == "MMF" && result.DataRangeStart.HasValue && result.DataRangeEnd.HasValue
            ? ReportSubstitutionPolicy.PrepareMmfRange(result.DataRangeStart.Value, result.DataRangeEnd.Value,
                result.IntervalMinutes, plants, readings, previousReadings, rules)
            : ReportSubstitutionPolicy.PrepareDdq(storedDataType, result.SubmissionDate,
                plants, readings, previousReadings, rules);
        var detected = await (from anomaly in database.DataAnomalies.AsNoTracking()
            join job in database.CollectionJobs.AsNoTracking() on anomaly.CollectionJobId equals job.Id
            join rule in database.ValidationRules.AsNoTracking() on anomaly.ValidationRuleId equals rule.Id into ruleGroup
            from rule in ruleGroup.DefaultIfEmpty()
            where plantIds.Contains(job.PlantId) && anomaly.DataType == result.Type
                && (result.Type == "MMF" || job.DataType == storedDataType)
                && (result.Type == "MMF"
                    ? result.DataRangeStart.HasValue && result.DataRangeEnd.HasValue
                        && job.SubmissionDateTime >= result.DataRangeStart.Value && job.SubmissionDateTime <= result.DataRangeEnd.Value
                    : job.SubmissionDateTime >= result.SubmissionDate
                        && job.SubmissionDateTime < submissionEnd)
            select new ReportDetectedAnomaly(job.CollectionId, job.PlantId, anomaly.FieldName,
                anomaly.OriginalValue, anomaly.Reason, anomaly.Status, anomaly.DetectedAt,
                rule == null ? "" : rule.Code, rule == null ? "" : rule.Name)).ToListAsync();
        return new(plants, storedDataType, readings, prepared, detected);
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

        return $"{reportType}_{safeDataType}_{result.SubmissionDate:yyyyMMdd_HHmm}";
    }

    private sealed record OnDemandPreparedExport(IReadOnlyList<LssPlant> Plants, string StoredDataType,
        IReadOnlyList<SolarApiReading> OriginalReadings, PreparedReportReadings Prepared,
        IReadOnlyList<ReportDetectedAnomaly> DetectedAnomalies);
}
