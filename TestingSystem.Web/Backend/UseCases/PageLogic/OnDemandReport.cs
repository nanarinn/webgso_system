using Microsoft.AspNetCore.Components;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class OnDemandReport : ComponentBase
{
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private const string DataCategory = "OnDemandReports";
    private List<AnalysisResult> Results { get; set; } = [];
    private AnalysisResult? SelectedResult { get; set; }
    private bool IsGenerateDialogOpen { get; set; }
    private bool IsSummaryDialogOpen { get; set; }
    private string SummaryFilter { get; set; } = "All";
    private string SelectedType { get; set; } = "DDQ";
    private DateTime SelectedSubmissionDate { get; set; } = DateTime.Today;
    private int GenerationSequence { get; set; } = 3;

    private List<AnalysisResult> SummaryResults => SummaryFilter == "All"
        ? Results
        : Results.Where(result => result.Status == SummaryFilter).ToList();

    private string SummaryDialogTitle => SummaryFilter == "All"
        ? "All generated requests"
        : $"{SummaryFilter} requests";

    protected override async Task OnInitializedAsync()
    {
        var seed = new[]
        {
            CreateResult("DDQ", DateTime.Today.AddDays(-1), DateTime.Now.AddHours(-2), "Successful", 1),
            CreateResult("MMF", DateTime.Today.AddDays(-2), DateTime.Now.AddHours(-5), "Successful", 2),
            CreateResult("DDQ", DateTime.Today.AddDays(-3), DateTime.Now.AddDays(-1), "Failed", 3)
        };
        await DataStore.SeedAsync(DataCategory, seed.Select(item => (item.Reference, item)));
        Results = await DataStore.LoadAsync<AnalysisResult>(DataCategory);
        GenerationSequence = Math.Max(GenerationSequence, Results.Count);
    }

    private void OpenGenerateDialog()
    {
        SelectedType = "DDQ";
        SelectedSubmissionDate = DateTime.Today;
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
        GenerationSequence++;
        var generatedAt = DateTime.Now;
        var status = GenerationSequence % 5 == 0 ? "Failed" : "Successful";
        var result = CreateResult(
            SelectedType,
            SelectedSubmissionDate,
            generatedAt,
            status,
            GenerationSequence);
        await DataStore.SaveAsync(DataCategory, result.Reference, result);
        Results.Insert(0, result);
        IsGenerateDialogOpen = false;
    }

    private void ViewResult(AnalysisResult result) => SelectedResult = result;
    private void CloseResult() => SelectedResult = null;

    private static AnalysisResult CreateResult(
        string type,
        DateTime submissionDate,
        DateTime generatedAt,
        string status,
        int sequence)
    {
        var reference = $"ODA-{type}-{generatedAt:yyyyMMdd}-{sequence:000}";
        var name = $"{type} On-Demand Analysis {sequence:000}";
        var description = type == "DDQ"
            ? $"DDQ interval validation generated from the {submissionDate:dd MMM yyyy} submission."
            : $"MMF forecast and variance analysis generated from the {submissionDate:dd MMM yyyy} submission.";
        if (status == "Failed")
        {
            description += " Generation completed with validation errors and requires review.";
        }

        return new AnalysisResult(reference, name, type, submissionDate, generatedAt, description, status);
    }

    private static string GetCsvDataUri(AnalysisResult result)
    {
        var csv = "Reference,Name,Type,Submission Date,Generated At,Description,Status\r\n"
            + string.Join(",", new[]
            {
                Csv(result.Reference),
                Csv(result.Name),
                Csv(result.Type),
                Csv(result.SubmissionDate.ToString("yyyy-MM-dd")),
                Csv(result.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(result.Description),
                Csv(result.Status)
            });
        return $"data:text/csv;charset=utf-8,{Uri.EscapeDataString(csv)}";
    }

    private static string GetCsvFileName(AnalysisResult result) => $"{result.Reference}.csv";
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private sealed record AnalysisResult(
        string Reference,
        string Name,
        string Type,
        DateTime SubmissionDate,
        DateTime GeneratedAt,
        string Description,
        string Status);
}
