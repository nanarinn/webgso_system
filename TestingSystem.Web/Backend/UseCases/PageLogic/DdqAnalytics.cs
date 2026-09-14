using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class DdqAnalytics : ComponentBase
{
    [Inject] private IStoredDataService DataStore { get; set; } = default!;
    private List<LssPlant> RegisteredPlants = [];
    private HashSet<int> SelectedPlantIds = [];
    private int FilterVersion;
    private string AppliedDataType = "Rolling 24 Hour";
    private DateTime AppliedStart, AppliedSubmission;
    private int AppliedInterval;
    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        RegisteredPlants = await database.LssPlants.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
    }
    private static readonly (string Label, int Value)[] Intervals =
    [
        ("15 minutes", 15),
        ("30 minutes", 30),
        ("1 hour", 60)
    ];

    private static readonly string[] DataTypes = ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month"];
    private static readonly string[] XAxisOptions = ["Timestamp", "Data Point Number"];
    private static readonly string[] YAxisOptions = ["MW Power", "Capacity Utilisation", "DDQ Success"];

    private static readonly string[] States =
    [
        "Johor", "Kedah", "Kelantan", "Melaka", "Negeri Sembilan", "Pahang", "Penang",
        "Perak", "Perlis", "Sabah", "Sarawak", "Selangor", "Terengganu"
    ];

    private static readonly string[] Regions = ["Northern", "Central", "Southern", "East Coast"];

    private DateTime DateFrom { get; set; } = DateTime.Today.AddDays(-7);
    private DateTime DateTo { get; set; } = DateTime.Today;
    private DateTime SubmissionDate { get; set; } = DateTime.Today;
    private DateTime TimestampStart { get; set; } = DateTime.Today.AddHours(8);
    private DateTime TimestampEnd { get; set; } = DateTime.Today.AddHours(18);
    private int SelectedInterval { get; set; } = 15;
    private string SelectedDataType { get; set; } = "Rolling 24 Hour";
    private string SelectedState { get; set; } = "";
    private string SelectedRegion { get; set; } = "";
    private string ValidationMessage { get; set; } = string.Empty;
    private string ResultDescription { get; set; } = string.Empty;
    private bool HasApplied { get; set; }
    private List<DdqDataset> Datasets { get; set; } = [];
    private DdqDataset? SelectedDataset { get; set; }
    private List<DdqRow> Rows { get; set; } = [];
    private List<PlantGraphSeries> ComparisonSeries = [];
    private string ViewMode { get; set; } = "Table";
    private bool IsGraphSettingsOpen { get; set; }
    private string PendingXAxis { get; set; } = "Timestamp";
    private string PendingYAxis { get; set; } = "MW Power";
    private string AppliedXAxis { get; set; } = "Timestamp";
    private string AppliedYAxis { get; set; } = "MW Power";

    private async Task ApplyFilters()
    {
        ValidationMessage = string.Empty;
        HasApplied = false;
        Datasets = [];
        SelectedDataset = null;
        Rows = [];
        ViewMode = "Table";
        IsGraphSettingsOpen = false;

        if (DateTo.Date < DateFrom.Date)
        {
            ValidationMessage = "The end date must be on or after the start date.";
            return;
        }

        if (TimestampEnd <= TimestampStart)
        {
            ValidationMessage = "The end timestamp must be later than the start timestamp.";
            return;
        }

        var totalAvailable = (int)Math.Floor((TimestampEnd - TimestampStart).TotalMinutes / SelectedInterval) + 1;
        HasApplied = true;
        AppliedDataType = SelectedDataType;
        AppliedStart = TimestampStart;
        AppliedSubmission = SubmissionDate;
        AppliedInterval = SelectedInterval;
        Datasets = AnalyticsPlantFilter.Match(RegisteredPlants, SelectedPlantIds, SelectedState, SelectedRegion)
            .Select(plant => new DdqDataset(plant.Id,
                $"DDQ-{plant.PlantId}-{SubmissionDate:yyyyMMdd}", plant.Name,
                (double)plant.CapacityMw, totalAvailable, plant.State, AnalyticsPlantFilter.RegionFor(plant.State)))
            .ToList();
        foreach (var dataset in Datasets)
            await DataStore.SaveAsync("DdqDatasets", dataset.Code, dataset);

        ResultDescription = $"{Datasets.Count} {SelectedDataType} dataset(s) for {string.Join(", ", Datasets.Select(d => d.Name))}. "
            + $"submitted on {SubmissionDate:dd MMM yyyy}. Date range {DateFrom:dd MMM yyyy} to {DateTo:dd MMM yyyy}; "
            + $"timestamps {TimestampStart:dd MMM yyyy HH:mm} to {TimestampEnd:dd MMM yyyy HH:mm} at {IntervalLabel} intervals.";
    }

    private async Task SelectDataset(DdqDataset dataset)
    {
        SelectedDataset = dataset;
        ViewMode = "Table";
        IsGraphSettingsOpen = false;
        Rows = BuildRows(dataset);
        foreach (var row in Rows)
            await DataStore.SaveAsync("DdqRecords", $"{dataset.Code}:{row.Timestamp:O}", row);
    }

    private List<DdqRow> BuildRows(DdqDataset dataset)
    {
        var rowCount = Math.Min(dataset.AvailableRows, 200);
        var stateFactor = Array.IndexOf(States, dataset.State) * 1.7;
        var typeFactor = Array.IndexOf(DataTypes, AppliedDataType) * 4.2;

        return Enumerable.Range(0, rowCount)
            .Select(index =>
            {
                var timestamp = AppliedStart.AddMinutes(index * AppliedInterval);
                var daylightCurve = Math.Max(0, Math.Sin((timestamp.TimeOfDay.TotalHours - 6) / 13 * Math.PI));
                var variation = Math.Sin((index + dataset.Id) * 0.73) * 5.2;
                var power = Math.Min(dataset.CapacityMw,
                    Math.Max(0, 8 + (daylightCurve * dataset.CapacityMw * 0.78) + stateFactor + typeFactor + variation));
                var status = index > 0 && (index + dataset.Id) % 11 == 0 ? "Failed" : "Successful";

                return new DdqRow(timestamp, AppliedSubmission, dataset.State, dataset.Region,
                    AppliedDataType, Intervals.First(i => i.Value == AppliedInterval).Label, Math.Round(power, 2), status);
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
        ComparisonSeries = Datasets.Select(dataset => new PlantGraphSeries(dataset.Name,
            BuildRows(dataset).Select(row => AppliedYAxis switch
            {
                "Capacity Utilisation" => dataset.CapacityMw <= 0 ? 0 : row.MwPower / dataset.CapacityMw * 100,
                "DDQ Success" => row.Status == "Successful" ? 100d : 0d,
                _ => row.MwPower
            }).ToList())).ToList();
        IsGraphSettingsOpen = false;
        ViewMode = "Graph";
    }

    private void ShowTable() => ViewMode = "Table";

    private void ResetFilters()
    {
        DateFrom = DateTime.Today.AddDays(-7);
        DateTo = DateTime.Today;
        SubmissionDate = DateTime.Today;
        TimestampStart = DateTime.Today.AddHours(8);
        TimestampEnd = DateTime.Today.AddHours(18);
        SelectedInterval = 15;
        SelectedDataType = "Rolling 24 Hour";
        SelectedState = "";
        SelectedRegion = "";
        SelectedPlantIds = [];
        FilterVersion++;
        ValidationMessage = string.Empty;
        ResultDescription = string.Empty;
        HasApplied = false;
        Datasets = [];
        SelectedDataset = null;
        Rows = [];
        ViewMode = "Table";
        IsGraphSettingsOpen = false;
        PendingXAxis = "Timestamp";
        PendingYAxis = "MW Power";
        AppliedXAxis = "Timestamp";
        AppliedYAxis = "MW Power";
    }

    private string IntervalLabel => Intervals.First(interval => interval.Value == SelectedInterval).Label;
    private string GraphUnit => AppliedYAxis == "MW Power" ? "MW" : "%";
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

    private List<double> GraphValues => ComparisonSeries.SelectMany(s => s.Values).ToList();

    private double GetGraphValue(DdqRow row) => AppliedYAxis switch
    {
        "Capacity Utilisation" when SelectedDataset is not null => row.MwPower / SelectedDataset.CapacityMw * 100,
        "DDQ Success" => row.Status == "Successful" ? 100 : 0,
        _ => row.MwPower
    };

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

    private sealed record DdqDataset(int Id, string Code, string Name, double CapacityMw, int AvailableRows, string State, string Region);

    private sealed record DdqRow(
        DateTime Timestamp,
        DateTime SubmissionDate,
        string State,
        string Region,
        string DataType,
        string Interval,
        double MwPower,
        string Status);
}
