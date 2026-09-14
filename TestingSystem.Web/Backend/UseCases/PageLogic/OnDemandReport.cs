using Microsoft.AspNetCore.Components;
using TestingSystem.Web.Demo;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class OnDemandReport : ComponentBase
{
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private const string DataCategory = "OnDemandReports";
    private List<DemoAnalysisResult> Results { get; set; } = [];
    private DemoAnalysisResult? SelectedResult { get; set; }
    private bool IsGenerateDialogOpen { get; set; }
    private bool IsSummaryDialogOpen { get; set; }
    private string SummaryFilter { get; set; } = "All";
    private string SelectedType { get; set; } = "DDQ";
    private DateTime SelectedSubmissionDate { get; set; } = DateTime.Today;
    private int GenerationSequence { get; set; } = 3;

    private List<DemoAnalysisResult> SummaryResults => SummaryFilter == "All"
        ? Results
        : Results.Where(result => result.Status == SummaryFilter).ToList();

    private string SummaryDialogTitle => SummaryFilter == "All"
        ? "All generated requests"
        : $"{SummaryFilter} requests";

    protected override async Task OnInitializedAsync()
    {
        var seed = DemoData.OnDemandReports(DateTime.Now);
        await DataStore.SeedAsync(DataCategory, seed.Select(item => (item.Reference, item)));
        Results = await DataStore.LoadAsync<DemoAnalysisResult>(DataCategory);
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
        var result = DemoData.CreateAnalysis(
            SelectedType,
            SelectedSubmissionDate,
            generatedAt,
            status,
            GenerationSequence);
        await DataStore.SaveAsync(DataCategory, result.Reference, result);
        Results.Insert(0, result);
        IsGenerateDialogOpen = false;
    }

    private void ViewResult(DemoAnalysisResult result) => SelectedResult = result;
    private void CloseResult() => SelectedResult = null;

    private static string GetCsvDataUri(DemoAnalysisResult result)
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

    private static string GetCsvFileName(DemoAnalysisResult result) => $"{result.Reference}.csv";
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

}
