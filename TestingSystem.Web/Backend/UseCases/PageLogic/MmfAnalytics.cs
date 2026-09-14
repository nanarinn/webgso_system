using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class MmfAnalytics : ComponentBase
{
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private List<LssPlant> RegisteredPlants { get; set; } = [];
    private HashSet<int> SelectedPlantIds = [];
    private int FilterVersion;
    private string AppliedParameter = "Irradiance";
    private DateTime AppliedStart;
    private int AppliedInterval;
    private List<string> States { get; set; } = [];
    private static readonly string[] Parameters = ["Irradiance", "GHI", "POA"];
    private static readonly (string Label, int Value)[] Intervals =
    [
        ("15 minutes", 15),
        ("30 minutes", 30),
        ("1 hour", 60)
    ];
    private static readonly string[] XAxisOptions = ["Timestamp", "Data Point Number"];
    private static readonly string[] YAxisOptions = ["Actual MW", "Forecast MW", "Variance"];

    private string SelectedState { get; set; } = string.Empty;
    private string SelectedParameter { get; set; } = "Irradiance";
    private DateTime StartDate { get; set; } = DateTime.Today.AddDays(-1);
    private DateTime EndDate { get; set; } = DateTime.Today;
    private int SelectedInterval { get; set; } = 15;
    private string ValidationMessage { get; set; } = string.Empty;
    private string ResultDescription { get; set; } = string.Empty;
    private bool HasApplied { get; set; }
    private List<MmfDataset> Datasets { get; set; } = [];
    private MmfDataset? SelectedDataset { get; set; }
    private List<MmfRow> Rows { get; set; } = [];
    private List<PlantGraphSeries> ComparisonSeries = [];
    private string ViewMode { get; set; } = "Table";
    private bool IsGraphSettingsOpen { get; set; }
    private string PendingXAxis { get; set; } = "Timestamp";
    private string PendingYAxis { get; set; } = "Actual MW";
    private string AppliedXAxis { get; set; } = "Timestamp";
    private string AppliedYAxis { get; set; } = "Actual MW";

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        RegisteredPlants = await database.LssPlants
            .Where(plant => plant.IsActive)
            .OrderBy(plant => plant.Name)
            .ToListAsync();
        States = RegisteredPlants.Select(plant => plant.State).Distinct().OrderBy(state => state).ToList();
    }

    private async Task ApplyFilters()
    {
        ValidationMessage = string.Empty;
        HasApplied = false;
        Datasets = [];
        SelectedDataset = null;
        Rows = [];
        ViewMode = "Table";

        if (EndDate.Date < StartDate.Date)
        {
            ValidationMessage = "The end date must be on or after the start date.";
            return;
        }

        var totalRows = CalculateAvailableRows();
        HasApplied = true;
        IsGraphSettingsOpen = false;
        AppliedParameter = SelectedParameter;
        AppliedStart = StartDate.Date;
        AppliedInterval = SelectedInterval;
        var selectedPlants = AnalyticsPlantFilter.Match(RegisteredPlants, SelectedPlantIds, SelectedState).ToList();
        var streamNames = new[] { $"{SelectedParameter} Forecast", "Operational Availability", "Dispatch Performance" };

        Datasets = selectedPlants.SelectMany(plant => streamNames.Select((name, index) => new MmfDataset(
            plant.Id * 3 + index,
            $"MMF-{plant.PlantId}-{StartDate:yyyyMMdd}-{index + 1:00}",
            $"{plant.Name} {name}", (double)plant.CapacityMw,
            totalRows, plant.Name, plant.State))).ToList();
        foreach (var dataset in Datasets)
            await DataStore.SaveAsync("MmfDatasets", dataset.Code, dataset);

        var limitNote = totalRows > 200 ? $" Each table displays the first 200 of {totalRows} intervals." : string.Empty;
        ResultDescription = $"{Datasets.Count} {SelectedParameter} MMF dataset(s) for {string.Join(", ", selectedPlants.Select(p => p.Name))}, "
            + $"from {StartDate:dd MMM yyyy} to {EndDate:dd MMM yyyy} at {IntervalLabel} intervals.{limitNote}";
    }

    private async Task SelectDataset(MmfDataset dataset)
    {
        SelectedDataset = dataset;
        ViewMode = "Table";
        IsGraphSettingsOpen = false;
        Rows = BuildRows(dataset);
        foreach (var row in Rows)
            await DataStore.SaveAsync("MmfRecords", $"{dataset.Code}:{row.Timestamp:O}", row);
    }

    private List<MmfRow> BuildRows(MmfDataset dataset)
    {
        var rowCount = Math.Min(dataset.AvailableRows, 200);
        var startTimestamp = AppliedStart;
        var parameterFactor = AppliedParameter switch
        {
            "GHI" => 0.92,
            "POA" => 1.08,
            _ => 1d
        };

        return Enumerable.Range(0, rowCount)
            .Select(index =>
            {
                var timestamp = startTimestamp.AddMinutes(index * AppliedInterval);
                var solarCurve = Math.Max(0, Math.Sin((timestamp.TimeOfDay.TotalHours - 6) / 13 * Math.PI));
                var forecast = Math.Min(dataset.CapacityMw, solarCurve * dataset.CapacityMw * 0.9 * parameterFactor);
                var variation = Math.Sin((index + dataset.Id) * 0.57) * 4.8;
                var actual = Math.Clamp(forecast * (1 + variation / 100), 0, dataset.CapacityMw);
                var variance = forecast <= 0 ? 0 : (actual - forecast) / forecast * 100;
                var status = Math.Abs(variance) <= 5 ? "Within" : "Alert";

                return new MmfRow(timestamp, dataset.PlantName, dataset.State, AppliedParameter, Intervals.First(i => i.Value == AppliedInterval).Label,
                    Math.Round(forecast, 2), Math.Round(actual, 2), Math.Round(variance, 2), status);
            })
            .ToList();
    }

    private void OpenGraphSettings()
    {
        PendingXAxis = AppliedXAxis;
        PendingYAxis = AppliedYAxis;
        IsGraphSettingsOpen = true;
    }

    private void CloseGraphSettings() => IsGraphSettingsOpen = false;

    private void GenerateGraph()
    {
        AppliedXAxis = PendingXAxis;
        AppliedYAxis = PendingYAxis;
        ComparisonSeries = Datasets.Where(d => SelectedDataset is not null && d.Id % 3 == SelectedDataset.Id % 3)
            .Select(dataset => new PlantGraphSeries(dataset.PlantName,
                BuildRows(dataset).Select(row => AppliedYAxis switch
                {
                    "Forecast MW" => row.ForecastMw,
                    "Variance" => row.VariancePercent,
                    _ => row.ActualMw
                }).ToList())).ToList();
        IsGraphSettingsOpen = false;
        ViewMode = "Graph";
    }

    private void ShowTable() => ViewMode = "Table";

    private void ResetFilters()
    {
        SelectedPlantIds = [];
        FilterVersion++;
        SelectedState = "";
        SelectedParameter = "Irradiance";
        StartDate = DateTime.Today.AddDays(-1);
        EndDate = DateTime.Today;
        SelectedInterval = 15;
        ValidationMessage = string.Empty;
        ResultDescription = string.Empty;
        HasApplied = false;
        Datasets = [];
        SelectedDataset = null;
        Rows = [];
        ViewMode = "Table";
        IsGraphSettingsOpen = false;
        PendingXAxis = AppliedXAxis = "Timestamp";
        PendingYAxis = AppliedYAxis = "Actual MW";
    }

    private int CalculateAvailableRows() =>
        (int)Math.Ceiling((EndDate.Date.AddDays(1) - StartDate.Date).TotalMinutes / SelectedInterval);

    private string IntervalLabel => Intervals.First(interval => interval.Value == SelectedInterval).Label;
    private string GraphUnit => AppliedYAxis == "Variance" ? "%" : "MW";
    private List<double> GraphValues => ComparisonSeries.SelectMany(s => s.Values).ToList();
    private double GraphMinimum => GraphValues.Count == 0 ? 0 : Math.Min(0, GraphValues.Min());
    private double GraphMaximum => GraphValues.Count == 0 ? 0 : GraphValues.Max();
    private double GraphAverage => GraphValues.Count == 0 ? 0 : GraphValues.Average();
    private string GraphLinePoints => BuildGraphPoints();
    private string GraphAreaPoints => Rows.Count == 0 ? string.Empty : $"72,325 {GraphLinePoints} 970,325";
    private string GraphStartLabel => AppliedXAxis == "Timestamp" && Rows.Count > 0
        ? Rows[0].Timestamp.ToString("dd MMM HH:mm")
        : "Point 1";
    private string GraphEndLabel => AppliedXAxis == "Timestamp" && Rows.Count > 0
        ? Rows[^1].Timestamp.ToString("dd MMM HH:mm")
        : $"Point {Rows.Count}";

    private string BuildGraphPoints()
    {
        if (Rows.Count == 0)
        {
            return string.Empty;
        }

        var values = GraphValues;
        var maximum = Math.Max(values.Max(), 1);
        return string.Join(" ", values.Select((value, index) =>
        {
            var x = Rows.Count == 1 ? 72 : 72 + (898d * index / (Rows.Count - 1));
            var y = 325 - (280d * value / maximum);
            return FormattableString.Invariant($"{x:F1},{y:F1}");
        }));
    }

    private sealed record MmfDataset(int Id, string Code, string Name, double CapacityMw, int AvailableRows, string PlantName, string State);

    private sealed record MmfRow(
        DateTime Timestamp,
        string Plant,
        string State,
        string Parameter,
        string Interval,
        double ForecastMw,
        double ActualMw,
        double VariancePercent,
        string Status);
}
