using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using System.Globalization;
using System.Text;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;
using TestingSystem.Web.Components.Shared;
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
    private DemoAnalysisResult? DeleteTarget { get; set; }
    private IReadOnlyList<DdqWorkbookSheet> PreviewSheets { get; set; } = [];
    private DdqWorkbookSheet? AnomalyPreviewSheet { get; set; }
    private int SelectedPreviewSheetIndex { get; set; }
    private bool IsAnomalyPreview { get; set; }
    private bool IsPreviewLoading { get; set; }
    private string PreviewError { get; set; } = string.Empty;
    private int PreviewRequestId { get; set; }
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
    private IReadOnlyList<DdqScheduledSubmissionOption> DdqSubmissionOptions { get; set; } = [];
    private string SelectedDdqSubmissionKey { get; set; } = string.Empty;
    private string PendingDdqSubmission { get; set; } = string.Empty;
    private int DdqSubmissionSearchVersion;
    private string MmfLocationScope { get; set; } = "All Plants";
    private DateTime MmfRangeStart { get; set; } = DateTime.Today;
    private DateTime MmfRangeEnd { get; set; } = DateTime.Today.AddDays(1).AddMinutes(-15);
    private DateTime MmfStartDateInput
    {
        get => MmfRangeStart.Date;
        set
        {
            var dateChanged = value.Date != MmfRangeStart.Date;
            MmfRangeStart = value.Date.Add(MmfRangeStart.TimeOfDay);
            if (dateChanged)
                MmfRangeEnd = value.Date.AddDays(1).AddMinutes(-15);
        }
    }
    private TimeOnly MmfStartTimeInput
    {
        get => TimeOnly.FromDateTime(MmfRangeStart);
        set => MmfRangeStart = MmfRangeStart.Date.Add(value.ToTimeSpan());
    }
    private DateTime MmfEndDateInput
    {
        get => MmfRangeEnd.Date;
        set => MmfRangeEnd = value.Date.Add(MmfRangeEnd.TimeOfDay);
    }
    private TimeOnly MmfEndTimeInput
    {
        get => TimeOnly.FromDateTime(MmfRangeEnd);
        set => MmfRangeEnd = MmfRangeEnd.Date.Add(value.ToTimeSpan());
    }
    private int GenerationSequence { get; set; } = 3;
    private int ActivePlantCount { get; set; }
    private string FormError { get; set; } = string.Empty;
    private bool IsGenerating { get; set; }
    private bool IsDeleting { get; set; }
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
                .Select(result => result with
                {
                    Name = DemoData.AnalysisDisplayName(result.Type, result.DataSelection, result.GeneratedAt,
                        result.DataRangeStart, result.DataRangeEnd)
                })
                .ToList();
        GenerationSequence = Math.Max(GenerationSequence, Results.Count);
        ActivePlantCount = await database.LssPlants.AsNoTracking().CountAsync(x => x.IsActive);
    }

    private async Task OpenGenerateDialog()
    {
        SelectedType = "DDQ";
        SelectedDataType = DdqDataTypes[0];
        SelectedInterval = 15;
        SelectedDdqSubmissionKey = string.Empty;
        PendingDdqSubmission = string.Empty;
        DdqSubmissionSearchVersion++;
        MmfLocationScope = "All Plants";
        await using var database = await ContextFactory.CreateDbContextAsync();
        var sourceRows = await (
            from job in database.CollectionJobs.AsNoTracking()
            join schedule in database.CollectionSchedules.AsNoTracking()
                on job.CollectionScheduleId equals (int?)schedule.Id
            where job.Status == CollectionJobStatus.Completed
                && schedule.ApiType == "DDQ"
                && database.SolarApiReadings.Any(reading => reading.CollectionJobId == job.Id)
            orderby job.SubmissionDateTime descending
            select new DdqScheduledSubmissionSource(job.DataType, job.SubmissionDateTime,
                schedule.Name, job.PlantId))
            .Take(5000)
            .ToListAsync();
        DdqSubmissionOptions = DdqScheduledSubmissionPolicy.Build(sourceRows);
        SelectedSubmissionDate = DateTime.Today;
        MmfRangeStart = DateTime.Today;
        MmfRangeEnd = DateTime.Today.AddDays(1).AddMinutes(-15);
        FormError = string.Empty;
        IsGenerateDialogOpen = true;
    }

    private IEnumerable<DdqScheduledSubmissionOption> AvailableDdqSubmissionOptions =>
        DdqSubmissionOptions.Where(option => option.Key != SelectedDdqSubmissionKey);

    private DdqScheduledSubmissionOption? SelectedDdqSubmission =>
        DdqSubmissionOptions.FirstOrDefault(option => option.Key == SelectedDdqSubmissionKey);

    private static string DdqSubmissionOptionText(DdqScheduledSubmissionOption option) => option.DisplayLabel;

    private void SelectDdqSubmission(ChangeEventArgs args)
    {
        PendingDdqSubmission = args.Value?.ToString()?.Trim() ?? string.Empty;
        var option = AvailableDdqSubmissionOptions.FirstOrDefault(candidate =>
            DdqSubmissionOptionText(candidate).Equals(PendingDdqSubmission, StringComparison.OrdinalIgnoreCase));
        if (option is null) return;

        SelectedDdqSubmissionKey = option.Key;
        SelectedDataType = option.DataType;
        SelectedSubmissionDate = option.SubmissionDateTime;
        SelectedInterval = DdqDatasetPolicy.Get(option.DataType).ReferenceTemplateIntervalMinutes;
        PendingDdqSubmission = string.Empty;
        DdqSubmissionSearchVersion++;
        FormError = string.Empty;
    }

    private void RemoveDdqSubmission()
    {
        SelectedDdqSubmissionKey = string.Empty;
        PendingDdqSubmission = string.Empty;
        DdqSubmissionSearchVersion++;
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

    private void RequestDelete(DemoAnalysisResult result)
    {
        DeleteTarget = result;
        FormError = string.Empty;
    }

    private void CancelDelete()
    {
        if (IsDeleting) return;
        DeleteTarget = null;
    }

    private async Task DeleteResultAsync()
    {
        if (DeleteTarget is null || IsDeleting || string.IsNullOrWhiteSpace(CurrentRequesterKey)) return;
        var target = DeleteTarget;
        IsDeleting = true;
        FormError = string.Empty;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            var entity = await database.ReportExecutions.SingleOrDefaultAsync(item =>
                item.ReportType == "On-Demand"
                && item.ReportId == target.Reference
                && item.RequestedBy == CurrentRequesterKey);
            if (entity is null)
            {
                FormError = "This analysis could not be found or does not belong to your account.";
                return;
            }

            database.ReportExecutions.Remove(entity);
            await database.SaveChangesAsync();
            Results.RemoveAll(item => item.Reference == target.Reference);
            if (SelectedResult?.Reference == target.Reference) CloseResult();
            if (AnalysisPage > 1 && TablePaging.Slice(Results, AnalysisPage).Count == 0) AnalysisPage--;

            var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
            await AuditTrail.RecordAsync(principal, "Delete on-demand analysis", "Success",
                "ReportExecution", target.Reference,
                $"Deleted {target.Type} on-demand analysis record generated at {target.GeneratedAt:dd MMM yyyy HH:mm:ss}.");
            DeleteTarget = null;
        }
        catch (Exception exception)
        {
            FormError = $"Analysis could not be deleted: {exception.GetBaseException().Message}";
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private async Task GenerateAnalysis()
    {
        if (IsGenerating) return;
        FormError = string.Empty;
        if (string.IsNullOrWhiteSpace(CurrentRequesterKey)) { FormError = "Your signed-in account could not be identified. Sign out and sign in again."; return; }
        if (SelectedType == "DDQ" && SelectedDdqSubmission is null) { FormError = "Select one completed DDQ submission."; return; }
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
            var selection = SelectedType == "DDQ" ? SelectedDataType : $"All MMF parameters · {MmfLocationScope}";
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
                PlantCount = preparedExport.Plants.Count,
                RecordCount = preparedExport.Prepared.Readings.Count,
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

    private DdqWorkbookSheet? SelectedPreviewSheet => IsAnomalyPreview
        ? AnomalyPreviewSheet
        : SelectedPreviewSheetIndex >= 0 && SelectedPreviewSheetIndex < PreviewSheets.Count
            ? PreviewSheets[SelectedPreviewSheetIndex]
            : null;

    private async Task ViewResult(DemoAnalysisResult result)
    {
        var requestId = ++PreviewRequestId;
        SelectedResult = result;
        PreviewSheets = [];
        AnomalyPreviewSheet = null;
        SelectedPreviewSheetIndex = 0;
        IsAnomalyPreview = false;
        PreviewError = string.Empty;
        IsPreviewLoading = true;
        await InvokeAsync(StateHasChanged);

        if (result.Status != "Successful")
        {
            PreviewError = "This analysis has no successful dataset to preview.";
            IsPreviewLoading = false;
            return;
        }

        try
        {
            var export = await PrepareExportAsync(result);
            var sheets = result.Type == "MMF"
                ? OnDemandMmfExcelExporter.BuildSheets(result.SubmissionDate, export.Plants,
                    export.Prepared.Readings, result.DataRangeStart, result.DataRangeEnd)
                : OnDemandDdqExcelExporter.BuildSheets(export.StoredDataType, result.SubmissionDate,
                    export.Plants, export.Prepared.Readings, result.IntervalMinutes);
            var anomalySheet = OnDemandAnomalyPreviewBuilder.Build(result.Type, export.StoredDataType,
                result.Type == "MMF" ? result.DataRangeStart ?? result.SubmissionDate : result.SubmissionDate,
                export.SourcePlants, export.Prepared, export.DetectedAnomalies);
            if (requestId == PreviewRequestId)
            {
                PreviewSheets = sheets;
                AnomalyPreviewSheet = anomalySheet;
            }
        }
        catch (Exception exception)
        {
            if (requestId == PreviewRequestId)
                PreviewError = $"Data preview could not be loaded: {exception.GetBaseException().Message}";
        }
        finally
        {
            if (requestId == PreviewRequestId) IsPreviewLoading = false;
        }
    }

    private void SelectPreviewSheet(int index)
    {
        if (index >= 0 && index < PreviewSheets.Count) SelectedPreviewSheetIndex = index;
    }

    private void ShowDataPreview() => IsAnomalyPreview = false;
    private void ShowAnomalyPreview() => IsAnomalyPreview = true;

    private void CloseResult()
    {
        PreviewRequestId++;
        SelectedResult = null;
        PreviewSheets = [];
        AnomalyPreviewSheet = null;
        IsAnomalyPreview = false;
        PreviewError = string.Empty;
        IsPreviewLoading = false;
    }

    private async Task ExportResultAsync(DemoAnalysisResult result)
    {
        var export = await PrepareExportAsync(result);
        byte[] bytes;
        string fileName;
        string contentType;
        if (result.Type == "MMF")
        {
            bytes = OnDemandMmfExcelExporter.Build(result.SubmissionDate, export.Plants,
                export.Prepared.Readings, result.DataRangeStart, result.DataRangeEnd);
            fileName = $"{GetFileStem(result)}.xlsx";
            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }
        else
        {
            bytes = OnDemandDdqExcelExporter.Build(export.StoredDataType, result.SubmissionDate,
                export.Plants, export.Prepared.Readings, result.IntervalMinutes);
            fileName = $"{GetFileStem(result)}.xlsx";
            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }
        await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName, contentType, Convert.ToBase64String(bytes));
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        await AuditTrail.RecordAsync(authenticationState.User, "Download on-demand analysis", "Success",
            "Download", fileName,
            result.Type == "MMF"
                ? $"Downloaded MMF Excel workbook with one worksheet per parameter at {IntervalText(result.IntervalMinutes)} interval for data timestamps {result.DataRangeStart:dd MMM yyyy HH:mm} to {result.DataRangeEnd:dd MMM yyyy HH:mm}; report {result.Reference}."
                : $"Downloaded DDQ Excel workbook for {result.DataSelection} at {IntervalText(result.IntervalMinutes)} interval for submission date and time {result.SubmissionDate:dd MMM yyyy HH:mm}; report {result.Reference}.");
    }

    private static string IntervalText(int minutes) => minutes == 60 ? "1 hour" : $"{minutes} minutes";

    private string ResultScopeCountLabel(DemoAnalysisResult result)
    {
        var count = result.PlantCount > 0 ? result.PlantCount : ActivePlantCount;
        if (result.Type != "MMF") return $"{count} plant(s)";
        return MmfScopeFromSelection(result.DataSelection) switch
        {
            "All States" => $"{count} state(s)",
            "All Regions" => $"{count} region(s)",
            _ => $"{count} plant(s)"
        };
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    private static string PreviewCell(object? value) => value switch
    {
        null => "—",
        decimal number => number.ToString("0.######", CultureInfo.InvariantCulture),
        double number => number.ToString("0.######", CultureInfo.InvariantCulture),
        float number => number.ToString("0.######", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private async Task ExportAnomalyResultAsync(DemoAnalysisResult result)
    {
        var export = await PrepareExportAsync(result);
        var csv = ReportAnomalyCsvExporter.Build(result.Type, export.StoredDataType,
            result.Type == "MMF" ? result.DataRangeStart ?? result.SubmissionDate : result.SubmissionDate,
            export.SourcePlants, export.OriginalReadings, export.Prepared, export.DetectedAnomalies);
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
        var sourcePlants = await database.LssPlants.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.PlantId).ThenBy(x => x.Name).ToListAsync();
        var plantIds = sourcePlants.Select(plant => plant.Id).ToList();
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
                result.IntervalMinutes, sourcePlants, readings, previousReadings, rules)
            : ReportSubstitutionPolicy.PrepareDdq(storedDataType, result.SubmissionDate,
                sourcePlants, readings, previousReadings, rules);
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
        IReadOnlyList<LssPlant> outputPlants = sourcePlants;
        if (result.Type == "MMF" && MmfScopeFromSelection(result.DataSelection) is { } scope
            && scope != "All Plants")
        {
            var aggregated = AggregateMmfScope(sourcePlants, prepared, scope);
            outputPlants = aggregated.Plants;
            prepared = aggregated.Prepared;
        }
        return new(outputPlants, sourcePlants, storedDataType, readings, prepared, detected);
    }

    private static string MmfScopeFromSelection(string? selection) =>
        selection?.Contains("All Regions", StringComparison.OrdinalIgnoreCase) == true ? "All Regions"
        : selection?.Contains("All States", StringComparison.OrdinalIgnoreCase) == true ? "All States"
        : "All Plants";

    private static (IReadOnlyList<LssPlant> Plants, PreparedReportReadings Prepared) AggregateMmfScope(
        IReadOnlyList<LssPlant> sourcePlants,
        PreparedReportReadings prepared,
        string scope)
    {
        var groups = sourcePlants
            .GroupBy(plant => scope == "All States"
                ? (string.IsNullOrWhiteSpace(plant.State) ? "Not assigned" : plant.State)
                : AnalyticsPlantFilter.RegionFor(plant.State), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .ToArray();
        var outputPlants = new List<LssPlant>();
        var outputReadings = new List<SolarApiReading>();
        var index = 0;
        foreach (var group in groups)
        {
            var aggregatePlantId = -(++index);
            var sourceIds = group.Select(plant => plant.Id).ToHashSet();
            outputReadings.AddRange(MmfLocationAveraging.AverageLatestByLocation(
                prepared.Readings.Where(reading => sourceIds.Contains(reading.PlantId)), aggregatePlantId));
            outputPlants.Add(new LssPlant
            {
                Id = aggregatePlantId,
                PlantId = $"{(scope == "All States" ? "STATE" : "REGION")}-{NormalizeLocationCode(group.Key)}",
                Name = group.Key,
                State = scope == "All States" ? group.Key : $"{group.Key} region",
                IsActive = true
            });
        }
        return (outputPlants, prepared with { Readings = outputReadings });
    }

    private static string NormalizeLocationCode(string value)
    {
        var code = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return string.IsNullOrWhiteSpace(code) ? "UNASSIGNED" : code;
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

        if (result.Type.Equals("MMF", StringComparison.OrdinalIgnoreCase)
            && result.DataRangeStart.HasValue && result.DataRangeEnd.HasValue)
            return $"{reportType}_{safeDataType}_{result.DataRangeStart:yyyyMMdd_HHmm}_to_{result.DataRangeEnd:yyyyMMdd_HHmm}_{result.GeneratedAt:HHmmss}";

        return $"{reportType}_{safeDataType}_{result.GeneratedAt:yyyyMMdd_HHmmss}";
    }

    private sealed record OnDemandPreparedExport(IReadOnlyList<LssPlant> Plants, IReadOnlyList<LssPlant> SourcePlants,
        string StoredDataType,
        IReadOnlyList<SolarApiReading> OriginalReadings, PreparedReportReadings Prepared,
        IReadOnlyList<ReportDetectedAnomaly> DetectedAnomalies);
}
