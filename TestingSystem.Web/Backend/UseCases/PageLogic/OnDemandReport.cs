using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using System.Text;
using TestingSystem.Web.Data;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class OnDemandReport : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;
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

    private List<DemoAnalysisResult> SummaryResults => SummaryFilter == "All"
        ? Results
        : Results.Where(result => result.Status == SummaryFilter).ToList();

    private string SummaryDialogTitle => SummaryFilter == "All"
        ? "All generated requests"
        : $"{SummaryFilter} requests";

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        Results = await database.ReportExecutions.AsNoTracking().Where(x => x.ReportType == "On-Demand")
            .OrderByDescending(x => x.RequestedAt).Select(x => new DemoAnalysisResult(x.ReportId, x.Name, x.DataType,
                x.SubmissionDate, x.RequestedAt.LocalDateTime, x.Description,
                x.GenerationStatus == "Completed" ? "Successful" : x.GenerationStatus,
                x.DataSelection, x.PlantCount, x.RecordCount)).ToListAsync();
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
        FormError = string.Empty;
        if (SelectedSubmissionDate.Date > DateTime.Today) { FormError = "Submission date cannot be in the future."; return; }
        await using var database = await ContextFactory.CreateDbContextAsync();
        var start = SelectedSubmissionDate.Date;
        var end = start.AddDays(1);
        var activePlantIds = await database.LssPlants.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync();
        var storedDataType = SelectedType == "DDQ" ? SelectedDataType : "MMF";
        var query = database.SolarApiReadings.AsNoTracking().Where(x => activePlantIds.Contains(x.PlantId)
            && x.DataType == storedDataType && x.SubmissionDateTime >= start && x.SubmissionDateTime < end);
        var recordCount = await query.CountAsync();
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
            recordCount);
        database.ReportExecutions.Add(new ReportExecution
        {
            ReportId = result.Reference, ReportType = "On-Demand", DataType = result.Type, RequestedBy = "Current user",
            RequestedAt = generatedAt, GenerationStatus = status == "Successful" ? "Completed" : "Failed",
            DeliveryStatus = "NotRequired", Name = result.Name, SubmissionDate = result.SubmissionDate,
            DataSelection = result.DataSelection, Description = result.Description, PlantCount = result.PlantCount,
            RecordCount = result.RecordCount, ErrorMessage = status == "Failed" ? "No matching MS SQL readings were found." : null
        });
        await database.SaveChangesAsync();
        Results.Insert(0, result);
        IsGenerateDialogOpen = false;
    }

    private void ViewResult(DemoAnalysisResult result) => SelectedResult = result;
    private void CloseResult() => SelectedResult = null;

    private async Task ExportResultAsync(DemoAnalysisResult result)
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        var start = result.SubmissionDate.Date;
        var end = start.AddDays(1);
        var plants = await database.LssPlants.AsNoTracking().Where(x => x.IsActive).ToDictionaryAsync(x => x.Id);
        var plantIds = plants.Keys.ToList();
        var storedDataType = result.Type == "DDQ" ? result.DataSelection : "MMF";
        if (string.IsNullOrWhiteSpace(storedDataType)) storedDataType = result.Type == "DDQ" ? "Rolling 24 Hour" : "MMF";
        var query = database.SolarApiReadings.AsNoTracking().Where(x => plantIds.Contains(x.PlantId)
            && x.DataType == storedDataType && x.SubmissionDateTime >= start && x.SubmissionDateTime < end);
        var readings = await query.OrderBy(x => x.PlantId).ThenBy(x => x.Parameter).ThenBy(x => x.IntervalStart).ToListAsync();
        var csv = new StringBuilder("Plant ID,Plant Name,API Type,Data Type or Parameter,Submission Date,Interval Start,Interval End,Interval Minutes,Value,Unit,Error Code,Error Message\r\n");
        foreach (var reading in readings)
        {
            plants.TryGetValue(reading.PlantId, out var plant);
            csv.AppendLine(string.Join(",", Csv(plant?.PlantId ?? reading.PlantId.ToString()), Csv(plant?.Name ?? "Unknown"), Csv(result.Type), Csv(reading.Parameter),
                Csv(reading.SubmissionDateTime.ToString("yyyy-MM-dd HH:mm:ss")), Csv(reading.IntervalStart.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(reading.IntervalEnd?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""), Csv(reading.IntervalMinutes?.ToString() ?? ""),
                Csv(reading.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), Csv(reading.Unit), Csv(reading.ErrorCode?.ToString() ?? ""), Csv(reading.ErrorMessage ?? "")));
        }
        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        await JavaScript.InvokeVoidAsync("sblssDownloadBase64", GetCsvFileName(result), "text/csv;charset=utf-8", Convert.ToBase64String(bytes));
    }

    private static string GetCsvFileName(DemoAnalysisResult result) => $"{result.Reference}.csv";
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

}
