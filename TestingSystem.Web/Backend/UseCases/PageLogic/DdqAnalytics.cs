using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Services;
using System.Globalization;
using Microsoft.JSInterop;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class DdqAnalytics : ComponentBase
{
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;

    private static readonly (string Label, int Value)[] Intervals =
    [
        ("15 minutes", 15),
        ("30 minutes", 30),
        ("1 hour", 60)
    ];

    private static readonly string[] DataTypes =
        ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];

    private IEnumerable<string> AvailableStates => RegisteredPlants.Select(x => x.State)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x);
    private IEnumerable<string> AvailableRegions => RegisteredPlants.Select(x => AnalyticsPlantFilter.RegionFor(x.State))
        .Where(x => x != "Not assigned").Distinct().OrderBy(x => x);

    private List<LssPlant> RegisteredPlants = [];
    private List<LssPlant> ResultPlants = [];
    private HashSet<int> SelectedPlantIds = [];
    private HashSet<string> SelectedDataTypes = ["Rolling 24 Hour"];
    private List<DdqTableRow> TableRows = [];
    private List<SolarApiReading> LoadedReadings = [];
    private DateTime? ImportedRangeStart;
    private DateTime? ImportedRangeEnd;
    private int FilterVersion;
    private DateTime? RangeStart { get; set; }
    private DateTime? RangeEnd { get; set; }
    private DateOnly? SubmissionStartDate { get; set; }
    private DateOnly? SubmissionEndDate { get; set; }
    private int SelectedInterval { get; set; } = 15;
    private string SelectedState { get; set; } = string.Empty;
    private string SelectedRegion { get; set; } = string.Empty;
    private string ActiveDataType { get; set; } = "Rolling 24 Hour";
    private string ValidationMessage { get; set; } = string.Empty;
    private string ResultDescription { get; set; } = string.Empty;
    private bool HasApplied { get; set; }
    private string DisplayMode { get; set; } = "table";
    private HashSet<int> HiddenGraphPlantIds = [];
    private bool ShowGraphCrosshair { get; set; } = true;
    private string LegendSearch { get; set; } = string.Empty;
    private bool IsExporting { get; set; }
    private static readonly string[] GraphColours =
        ["#0875c9", "#00a56a", "#f2a900", "#d64545", "#7656d6", "#00a6a6", "#e06c00", "#c13f87", "#54708f", "#4c9b35"];

    private bool PlantFilterDisabled => !string.IsNullOrWhiteSpace(SelectedState)
        || !string.IsNullOrWhiteSpace(SelectedRegion);
    private bool StateFilterDisabled => SelectedPlantIds.Count > 0
        || !string.IsNullOrWhiteSpace(SelectedRegion);
    private bool RegionFilterDisabled => SelectedPlantIds.Count > 0
        || !string.IsNullOrWhiteSpace(SelectedState);

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        RegisteredPlants = await database.LssPlants
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
        var importedWindow = await database.SolarApiReadings.AsNoTracking()
            .Where(x => x.DataType == "Rolling 24 Hour")
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Start = group.Min(x => x.IntervalStart),
                End = group.Max(x => x.IntervalStart),
                Submission = group.Max(x => x.SubmissionDateTime)
            })
            .FirstOrDefaultAsync();
        if (importedWindow is not null)
        {
            ImportedRangeStart = importedWindow.Start;
            ImportedRangeEnd = importedWindow.End;
        }
    }

    private Task OnPlantsChanged(HashSet<int> plantIds)
    {
        SelectedPlantIds = plantIds;
        if (plantIds.Count > 0)
        {
            SelectedState = string.Empty;
            SelectedRegion = string.Empty;
        }
        return Task.CompletedTask;
    }

    private void OnStateChanged(ChangeEventArgs args)
    {
        SelectedState = args.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(SelectedState))
        {
            SelectedRegion = string.Empty;
            SelectedPlantIds = [];
            FilterVersion++;
        }
    }

    private void OnRegionChanged(ChangeEventArgs args)
    {
        SelectedRegion = args.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(SelectedRegion))
        {
            SelectedState = string.Empty;
            SelectedPlantIds = [];
            FilterVersion++;
        }
    }

    private void ToggleDataType(string dataType, object? value)
    {
        if (value is true) SelectedDataTypes.Add(dataType);
        else SelectedDataTypes.Remove(dataType);
    }

    private void OnRangeStartDateChanged(ChangeEventArgs args) => RangeStart = WithOptionalDate(RangeStart, args.Value?.ToString());
    private void OnRangeStartTimeChanged(ChangeEventArgs args) => RangeStart = WithOptionalTime(RangeStart, args.Value?.ToString());
    private void OnRangeEndDateChanged(ChangeEventArgs args) => RangeEnd = WithOptionalDate(RangeEnd, args.Value?.ToString());
    private void OnRangeEndTimeChanged(ChangeEventArgs args) => RangeEnd = WithOptionalTime(RangeEnd, args.Value?.ToString());
    private void OnSubmissionStartDateChanged(ChangeEventArgs args) => SubmissionStartDate = ParseOptionalDate(args.Value?.ToString());
    private void OnSubmissionEndDateChanged(ChangeEventArgs args) => SubmissionEndDate = ParseOptionalDate(args.Value?.ToString());

    private static DateTime WithDate(DateTime current, string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date.ToDateTime(TimeOnly.FromDateTime(current))
            : current;

    private static DateTime WithTime(DateTime current, string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time)
            ? DateOnly.FromDateTime(current).ToDateTime(time)
            : current;

    private static DateTime? WithOptionalDate(DateTime? current, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : WithDate(current ?? DateTime.Today, value);

    private static DateTime? WithOptionalTime(DateTime? current, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : WithTime(current ?? DateTime.Today, value);

    private static DateOnly? ParseOptionalDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;

    private DateTime? SubmissionStartDateTime => SubmissionStartDate?.ToDateTime(TimeOnly.MinValue);
    private DateTime? SubmissionEndDateTime => SubmissionEndDate?.ToDateTime(TimeOnly.MinValue);

    private async Task ApplyFilters()
    {
        ValidationMessage = string.Empty;
        HasApplied = false;
        ResultPlants = [];
        TableRows = [];

        if (RangeStart.HasValue != RangeEnd.HasValue)
        {
            ValidationMessage = "Complete both Start and End, or leave the entire data timestamp range empty.";
            return;
        }

        if (RangeStart.HasValue && RangeEnd <= RangeStart)
        {
            ValidationMessage = "The end date and time must be later than the start date and time.";
            return;
        }

        var hasCompleteSubmissionFilter = SubmissionStartDate.HasValue && SubmissionEndDate.HasValue;
        if (!hasCompleteSubmissionFilter)
        {
            ValidationMessage = "Submission range is required. Select both the Start Date and End Date.";
            return;
        }

        if (hasCompleteSubmissionFilter && SubmissionEndDateTime < SubmissionStartDateTime)
        {
            ValidationMessage = "The submission end time must be the same as or later than the start time.";
            return;
        }

        if (SelectedDataTypes.Count == 0)
        {
            ValidationMessage = "Select at least one data type.";
            return;
        }

        ResultPlants = AnalyticsPlantFilter.Match(
            RegisteredPlants, SelectedPlantIds, SelectedState, SelectedRegion).ToList();
        if (ResultPlants.Count == 0)
        {
            ValidationMessage = "No active plants match the selected plant, state, or region.";
            return;
        }

        ActiveDataType = DataTypes.First(SelectedDataTypes.Contains);
        HiddenGraphPlantIds.Clear();
        LegendSearch = string.Empty;
        await LoadTableRowsAsync();
        HasApplied = true;

        var location = SelectedPlantIds.Count > 0 ? $"{SelectedPlantIds.Count} selected plant(s)"
            : !string.IsNullOrWhiteSpace(SelectedState) ? $"state {SelectedState}"
            : !string.IsNullOrWhiteSpace(SelectedRegion) ? $"region {SelectedRegion}"
            : "all active plants";
        ResultDescription = $"{ResultPlants.Count} plant(s) from {location}; {SelectedDataTypes.Count} data type(s); "
            + $"{TimestampRangeDescription}; {IntervalLabel} interval.";

    }

    private async Task SelectDataTypeTab(string dataType)
    {
        ValidationMessage = string.Empty;
        ActiveDataType = dataType;
        await LoadTableRowsAsync();
    }

    private async Task LoadTableRowsAsync()
    {
        var plantIds = ResultPlants.Select(x => x.Id).ToArray();
        var submissionStart = SubmissionStartDateTime;
        var submissionEnd = SubmissionEndDateTime?.AddDays(1);
        await using var database = await ContextFactory.CreateDbContextAsync();
        var timestamps = await database.SolarApiReadings.AsNoTracking()
            .Where(x => x.DataType == ActiveDataType && plantIds.Contains(x.PlantId)
                        && (!RangeStart.HasValue || x.IntervalStart >= RangeStart.Value)
                        && (!RangeEnd.HasValue || x.IntervalStart <= RangeEnd.Value)
                        && (!submissionStart.HasValue || x.SubmissionDateTime >= submissionStart.Value)
                        && (!submissionEnd.HasValue || x.SubmissionDateTime < submissionEnd.Value)
                        && x.IntervalStart.Minute % SelectedInterval == 0)
            .Select(x => x.IntervalStart).Distinct().OrderBy(x => x).Take(500).ToListAsync();
        if (timestamps.Count == 0)
        {
            TableRows = [];
            LoadedReadings = [];
            ValidationMessage = $"No {ActiveDataType} readings match the selected location, submission, date range, and interval.";
            return;
        }
        var lastTimestamp = timestamps[^1];
        LoadedReadings = await database.SolarApiReadings.AsNoTracking()
            .Where(x => x.DataType == ActiveDataType && plantIds.Contains(x.PlantId)
                        && x.IntervalStart >= timestamps[0] && x.IntervalStart <= lastTimestamp
                        && (!submissionStart.HasValue || x.SubmissionDateTime >= submissionStart.Value)
                        && (!submissionEnd.HasValue || x.SubmissionDateTime < submissionEnd.Value)
                        && x.IntervalStart.Minute % SelectedInterval == 0)
            .OrderByDescending(x => x.Id).ToListAsync();
        TableRows = timestamps.Select(timestamp => new DdqTableRow(timestamp,
            LoadedReadings.Where(x => x.IntervalStart == timestamp)
                .GroupBy(x => x.PlantId).ToDictionary(x => x.Key, x => x.First().Value))).ToList();
    }

    private void ResetFilters()
    {
        RangeStart = null;
        RangeEnd = null;
        SubmissionStartDate = null;
        SubmissionEndDate = null;
        SelectedInterval = 15;
        SelectedState = string.Empty;
        SelectedRegion = string.Empty;
        SelectedPlantIds = [];
        SelectedDataTypes = ["Rolling 24 Hour"];
        ActiveDataType = "Rolling 24 Hour";
        FilterVersion++;
        ValidationMessage = string.Empty;
        ResultDescription = string.Empty;
        HasApplied = false;
        ResultPlants = [];
        TableRows = [];
        LoadedReadings = [];
        DisplayMode = "table";
        HiddenGraphPlantIds.Clear();
        LegendSearch = string.Empty;
    }

    private string IntervalLabel => Intervals.First(interval => interval.Value == SelectedInterval).Label;
    private string SubmissionPeriodLabel => SubmissionStartDate.HasValue && SubmissionEndDate.HasValue
        ? $"{SubmissionStartDate:dd/MM/yyyy} - {SubmissionEndDate:dd/MM/yyyy}"
        : "All available submissions";
    private string SubmissionTableLabel => SubmissionStartDate.HasValue
        ? $"{SubmissionStartDate:dd/MM/yyyy}"
        : LoadedReadings.FirstOrDefault()?.SubmissionDateTime.ToString("dd/MM/yyyy HH:mm") ?? "-";
    private string TimestampRangeLabel => RangeStart.HasValue && RangeEnd.HasValue
        ? $"{RangeStart:dd/MM/yyyy HH:mm} - {RangeEnd:dd/MM/yyyy HH:mm}"
        : "All available timestamps";
    private string TimestampRangeDescription => RangeStart.HasValue && RangeEnd.HasValue
        ? $"{RangeStart:dd MMM yyyy HH:mm} to {RangeEnd:dd MMM yyyy HH:mm}"
        : "all available data timestamps";

    private async Task DownloadExcelAsync()
    {
        if (IsExporting || ResultPlants.Count == 0) return;
        IsExporting = true;
        try
        {
            var workbookSheets = new List<DdqWorkbookSheet>();
            var anomalyRows = new List<IReadOnlyList<object?>>();
            var plantIds = ResultPlants.Select(plant => plant.Id).ToArray();
            var submissionStart = SubmissionStartDateTime;
            var submissionEnd = SubmissionEndDateTime?.AddDays(1);
            await using var database = await ContextFactory.CreateDbContextAsync();

            foreach (var dataType in DataTypes.Where(SelectedDataTypes.Contains))
            {
                var readings = await database.SolarApiReadings.AsNoTracking()
                    .Where(reading => reading.DataType == dataType && plantIds.Contains(reading.PlantId)
                        && (!RangeStart.HasValue || reading.IntervalStart >= RangeStart.Value)
                        && (!RangeEnd.HasValue || reading.IntervalStart <= RangeEnd.Value)
                        && (!submissionStart.HasValue || reading.SubmissionDateTime >= submissionStart.Value)
                        && (!submissionEnd.HasValue || reading.SubmissionDateTime < submissionEnd.Value)
                        && reading.IntervalStart.Minute % SelectedInterval == 0)
                    .OrderByDescending(reading => reading.Id)
                    .ToListAsync();

                var headers = new List<string>
                {
                    "#", "Submission Date & Time", "Data Timestamp", "Year", "Month", "Day", "Period"
                };
                headers.AddRange(ResultPlants.Select(plant => $"{plant.Name} ({plant.PlantId}) MW"));
                var sheetRows = new List<IReadOnlyList<object?>>();
                var groupedReadings = readings.GroupBy(reading => reading.IntervalStart)
                    .OrderBy(group => group.Key).ToList();

                for (var rowIndex = 0; rowIndex < groupedReadings.Count; rowIndex++)
                {
                    var group = groupedReadings[rowIndex];
                    var latestByPlant = group.GroupBy(reading => reading.PlantId)
                        .ToDictionary(items => items.Key, items => items.First());
                    var values = new List<object?>
                    {
                        rowIndex + 1,
                        group.Max(reading => reading.SubmissionDateTime).ToString("dd/MM/yyyy HH:mm"),
                        group.Key.ToString("dd/MM/yyyy HH:mm"),
                        group.Key.Year, group.Key.Month, group.Key.Day, PeriodNumber(group.Key)
                    };
                    foreach (var plant in ResultPlants)
                    {
                        if (latestByPlant.TryGetValue(plant.Id, out var reading))
                        {
                            values.Add(reading.Value);
                            if (reading.Value < 0 || (plant.CapacityMw > 0 && reading.Value > plant.CapacityMw))
                            {
                                var type = reading.Value < 0 ? "Negative value" : "Above plant capacity";
                                var reason = reading.Value < 0
                                    ? "Active power cannot be below 0 MW."
                                    : "Active power exceeds the registered plant capacity.";
                                anomalyRows.Add(AnomalyWorkbookRow(dataType, group.Key, plant, type, reading.Value, reason));
                            }
                        }
                        else
                        {
                            values.Add(null);
                            anomalyRows.Add(AnomalyWorkbookRow(dataType, group.Key, plant, "Missing reading", null,
                                "No reading was supplied for this plant and interval."));
                        }
                    }
                    sheetRows.Add(values);
                }
                var metadata = new (string Label, string Value)[]
                {
                    ("Data Type", dataType),
                    ("Submission Period", SubmissionPeriodLabel),
                    ("Data Interval", IntervalLabel),
                    ("Data Timestamp Range", TimestampRangeLabel)
                };
                workbookSheets.Add(new DdqWorkbookSheet(dataType,
                    "SOLAR DATA SUBMISSION / DATA MONITORING", metadata, headers, sheetRows));
            }

            var anomalyHeaders = new[]
            {
                "Data Type", "Submission Start Date", "Submission End Date", "Data Timestamp",
                "Plant ID", "Plant Name", "Anomaly Type", "MW Value", "Capacity MW", "Reason"
            };
            var anomalyMetadata = new (string Label, string Value)[]
            {
                ("Data Type", string.Join(", ", DataTypes.Where(SelectedDataTypes.Contains))),
                ("Submission Period", SubmissionPeriodLabel),
                ("Data Interval", IntervalLabel),
                ("Data Timestamp Range", TimestampRangeLabel)
            };
            workbookSheets.Add(new DdqWorkbookSheet("Anomaly Report", "ANOMALY REPORT",
                anomalyMetadata, anomalyHeaders, anomalyRows));
            var bytes = DdqExcelWorkbookExporter.Build(workbookSheets);
            await JavaScript.InvokeVoidAsync("sblssDownloadBase64", "SBLSS-Filtered-Data.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(bytes));
        }
        finally
        {
            IsExporting = false;
        }
    }

    private IReadOnlyList<object?> AnomalyWorkbookRow(string dataType, DateTime timestamp, LssPlant plant,
        string anomalyType, decimal? value, string reason) => new object?[]
        {
            dataType,
            SubmissionStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SubmissionEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            plant.PlantId, plant.Name, anomalyType, value, plant.CapacityMw, reason
        };

    private static int PeriodNumber(DateTime timestamp) =>
        (timestamp.Hour * 60 + timestamp.Minute) / 15 + 1;

    private void SetDisplayMode(string mode) => DisplayMode = mode;

    private void ToggleGraphPlant(int plantId)
    {
        if (!HiddenGraphPlantIds.Add(plantId)) HiddenGraphPlantIds.Remove(plantId);
    }

    private void ToggleGraphCrosshair() => ShowGraphCrosshair = !ShowGraphCrosshair;

    private IEnumerable<LssPlant> FilteredLegendPlants => string.IsNullOrWhiteSpace(LegendSearch)
        ? ResultPlants
        : ResultPlants.Where(x => x.Name.Contains(LegendSearch, StringComparison.OrdinalIgnoreCase)
            || x.PlantId.Contains(LegendSearch, StringComparison.OrdinalIgnoreCase));

    private string GraphColour(LssPlant plant)
    {
        var index = ResultPlants.FindIndex(x => x.Id == plant.Id);
        return GraphColours[Math.Max(0, index) % GraphColours.Length];
    }

    private decimal GraphMaximum
    {
        get
        {
            var visibleIds = ResultPlants.Where(x => !HiddenGraphPlantIds.Contains(x.Id)).Select(x => x.Id).ToHashSet();
            var maximum = TableRows.SelectMany(x => x.PlantValues)
                .Where(x => visibleIds.Contains(x.Key)).Select(x => x.Value).DefaultIfEmpty(1).Max();
            return Math.Max(1, Math.Ceiling(maximum / 10) * 10);
        }
    }

    private string GraphPoints(LssPlant plant)
    {
        const double left = 74, top = 24, width = 990, height = 340;
        if (TableRows.Count == 0) return string.Empty;
        var denominator = Math.Max(1, TableRows.Count - 1);
        var maximum = (double)GraphMaximum;
        return string.Join(' ', TableRows.Select((row, index) =>
        {
            if (!row.PlantValues.TryGetValue(plant.Id, out var value)) return null;
            var x = left + index / (double)denominator * width;
            var y = top + (1 - (double)value / maximum) * height;
            return $"{x.ToString("F1", CultureInfo.InvariantCulture)},{y.ToString("F1", CultureInfo.InvariantCulture)}";
        }).Where(x => x is not null));
    }

    private IEnumerable<GraphPoint> GraphPointItems(LssPlant plant)
    {
        const double left = 74, top = 24, width = 990, height = 340;
        var denominator = Math.Max(1, TableRows.Count - 1);
        var maximum = (double)GraphMaximum;
        for (var index = 0; index < TableRows.Count; index++)
        {
            var row = TableRows[index];
            if (!row.PlantValues.TryGetValue(plant.Id, out var value)) continue;
            var x = left + index / (double)denominator * width;
            var y = top + (1 - (double)value / maximum) * height;
            yield return new GraphPoint(x, y, row.Timestamp, value);
        }
    }

    private IEnumerable<(double Y, decimal Value)> GraphYTicks()
    {
        const double top = 24, height = 340;
        for (var index = 0; index <= 5; index++)
            yield return (top + height - index / 5d * height, GraphMaximum * index / 5m);
    }

    private IEnumerable<(double X, DateTime Value)> GraphXTicks()
    {
        const double left = 74, width = 990;
        if (TableRows.Count == 0) yield break;
        var last = TableRows.Count - 1;
        for (var index = 0; index <= 4; index++)
        {
            var rowIndex = (int)Math.Round(last * index / 4d);
            yield return (left + index / 4d * width, TableRows[rowIndex].Timestamp);
        }
    }

    private sealed record DdqTableRow(DateTime Timestamp, Dictionary<int, decimal> PlantValues);
    private sealed record GraphPoint(double X, double Y, DateTime Timestamp, decimal Value);
}
