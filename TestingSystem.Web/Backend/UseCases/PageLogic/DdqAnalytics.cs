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

    private static readonly string[] DataTypes =
        ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];
    private static readonly (string Label, int Value)[] Intervals =
        [("15 minutes", 15), ("30 minutes", 30), ("1 hour", 60)];

    private IEnumerable<string> AvailableStates => RegisteredPlants.Select(x => x.State)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x);
    private IEnumerable<string> AvailableRegions => RegisteredPlants.Select(x => AnalyticsPlantFilter.RegionFor(x.State))
        .Where(x => x != "Not assigned").Distinct().OrderBy(x => x);

    private List<LssPlant> RegisteredPlants = [];
    private List<LssPlant> ResultPlants = [];
    private HashSet<int> SelectedPlantIds = [];
    private HashSet<string> SelectedDataTypes = ["Rolling 24 Hour"];
    private HashSet<string> SelectedStates = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> SelectedRegions = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> SelectedIntervals = DataTypes.ToDictionary(
        dataType => dataType, dataType => DdqDatasetPolicy.Get(dataType).ReferenceTemplateIntervalMinutes);
    private List<DdqTableRow> TableRows = [];
    private List<SolarApiReading> LoadedReadings = [];
    private DateTime? ImportedRangeStart;
    private DateTime? ImportedRangeEnd;
    private int FilterVersion;
    private string RangeStartDateValue { get; set; } = string.Empty;
    private string RangeStartTimeValue { get; set; } = string.Empty;
    private string RangeEndDateValue { get; set; } = string.Empty;
    private string RangeEndTimeValue { get; set; } = string.Empty;
    private DateOnly? SubmissionStartDate { get; set; }
    private DateOnly? SubmissionEndDate { get; set; }
    private string ActiveDataType { get; set; } = "Rolling 24 Hour";
    private string ValidationMessage { get; set; } = string.Empty;
    private string ResultDescription { get; set; } = string.Empty;
    private bool HasApplied { get; set; }
    private string DisplayMode { get; set; } = "table";
    private HashSet<int> HiddenGraphPlantIds = [];
    private bool ShowGraphCrosshair { get; set; } = true;
    private string LegendSearch { get; set; } = string.Empty;
    private bool IsExporting { get; set; }
    private bool IsApplyingFilters { get; set; }
    private static readonly string[] GraphColours =
        ["#0875c9", "#00a56a", "#f2a900", "#d64545", "#7656d6", "#00a6a6", "#e06c00", "#c13f87", "#54708f", "#4c9b35"];

    private bool PlantFilterDisabled => SelectedStates.Count > 0 || SelectedRegions.Count > 0;
    private bool StateFilterDisabled => SelectedPlantIds.Count > 0 || SelectedRegions.Count > 0;
    private bool RegionFilterDisabled => SelectedPlantIds.Count > 0 || SelectedStates.Count > 0;

    protected override async Task OnInitializedAsync()
    {
        await using var database = await ContextFactory.CreateDbContextAsync();
        RegisteredPlants = await database.LssPlants
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
        var importedWindow = await database.PublishedReadings()
            .Where(x => DataTypes.Contains(x.DataType))
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
            SubmissionStartDate = DateOnly.FromDateTime(importedWindow.Submission);
            SubmissionEndDate = SubmissionStartDate;
        }
    }

    private Task OnPlantsChanged(HashSet<int> plantIds)
    {
        SelectedPlantIds = plantIds;
        if (plantIds.Count > 0)
        {
            SelectedStates.Clear();
            SelectedRegions.Clear();
        }
        return Task.CompletedTask;
    }

    private Task OnStatesChanged(HashSet<string> states)
    {
        SelectedStates = states;
        if (states.Count > 0)
        {
            SelectedRegions.Clear();
            SelectedPlantIds = [];
            FilterVersion++;
        }
        return Task.CompletedTask;
    }

    private Task OnRegionsChanged(HashSet<string> regions)
    {
        SelectedRegions = regions;
        if (regions.Count > 0)
        {
            SelectedStates.Clear();
            SelectedPlantIds = [];
            FilterVersion++;
        }
        return Task.CompletedTask;
    }

    private void ToggleDataType(string dataType, object? value)
    {
        if (value is true) SelectedDataTypes.Add(dataType);
        else SelectedDataTypes.Remove(dataType);
    }

    private void OnDataTypeIntervalChanged(string dataType, ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var interval)
            && Intervals.Any(item => item.Value == interval))
            SelectedIntervals[dataType] = interval;
    }

    private int IntervalFor(string dataType) => SelectedIntervals.GetValueOrDefault(dataType,
        DdqDatasetPolicy.Get(dataType).ReferenceTemplateIntervalMinutes);
    private void OnSubmissionStartDateChanged(ChangeEventArgs args) => SubmissionStartDate = ParseOptionalDate(args.Value?.ToString());
    private void OnSubmissionEndDateChanged(ChangeEventArgs args) => SubmissionEndDate = ParseOptionalDate(args.Value?.ToString());

    private DateTime? RangeStart => ComposeDateTime(RangeStartDateValue, RangeStartTimeValue);
    private DateTime? RangeEnd => ComposeDateTime(RangeEndDateValue, RangeEndTimeValue);
    private bool HasAnyRangeInput => !string.IsNullOrWhiteSpace(RangeStartDateValue)
        || !string.IsNullOrWhiteSpace(RangeStartTimeValue)
        || !string.IsNullOrWhiteSpace(RangeEndDateValue)
        || !string.IsNullOrWhiteSpace(RangeEndTimeValue);
    private bool HasCompleteRangeInput => !string.IsNullOrWhiteSpace(RangeStartDateValue)
        && !string.IsNullOrWhiteSpace(RangeStartTimeValue)
        && !string.IsNullOrWhiteSpace(RangeEndDateValue)
        && !string.IsNullOrWhiteSpace(RangeEndTimeValue);

    private static DateTime? ComposeDateTime(string? dateValue, string? timeValue)
    {
        if (!DateOnly.TryParseExact(dateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return null;
        }

        var timeFormats = new[] { "HH:mm", "HH:mm:ss", "h:mm tt", "hh:mm tt" };
        if (!TimeOnly.TryParseExact(timeValue, timeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            return null;
        }

        return date.ToDateTime(time);
    }

    private static DateOnly? ParseOptionalDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;

    private DateTime? SubmissionStartDateTime => SubmissionStartDate?.ToDateTime(TimeOnly.MinValue);
    private DateTime? SubmissionEndDateTime => SubmissionEndDate?.ToDateTime(TimeOnly.MinValue);

    private (DateTime? Start, DateTime? EndExclusive) TimestampWindowFor(string dataType)
    {
        if (RangeStart.HasValue && RangeEnd.HasValue)
            return (RangeStart.Value, RangeEnd.Value.AddTicks(1));

        if (DdqDatasetPolicy.CanonicalName(dataType) == "Day Ahead" && SubmissionStartDateTime.HasValue)
        {
            var window = DdqDatasetPolicy.ReferenceWindow(dataType, SubmissionStartDateTime.Value);
            return (window.Start, window.EndExclusive);
        }

        return (null, null);
    }

    private async Task ApplyFilters()
    {
        if (IsApplyingFilters) return;

        IsApplyingFilters = true;
        try
        {
            await ApplyFiltersCoreAsync();
        }
        finally
        {
            IsApplyingFilters = false;
        }
    }

    private async Task ApplyFiltersCoreAsync()
    {
        ValidationMessage = string.Empty;
        HasApplied = false;
        ResultPlants = [];
        TableRows = [];

        if (HasAnyRangeInput && !HasCompleteRangeInput)
        {
            ValidationMessage = "Complete both Start and End, or leave the entire data timestamp range empty.";
            return;
        }

        if (HasCompleteRangeInput && (!RangeStart.HasValue || !RangeEnd.HasValue))
        {
            ValidationMessage = "Enter a valid date and time for both Start and End.";
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
            RegisteredPlants, SelectedPlantIds, SelectedStates, SelectedRegions).ToList();
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
            : SelectedStates.Count > 0 ? $"{SelectedStates.Count} selected state(s)"
            : SelectedRegions.Count > 0 ? $"{SelectedRegions.Count} selected region(s)"
            : "all active plants";
        var intervals = string.Join(", ", DataTypes.Where(SelectedDataTypes.Contains)
            .Select(dataType => $"{DataTypeLabel(dataType)}: {IntervalText(IntervalFor(dataType))}"));
        ResultDescription = $"{ResultPlants.Count} plant(s) from {location}; {SelectedDataTypes.Count} data type(s); "
            + $"{TimestampRangeDescription}; {intervals}.";

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
        var selectedInterval = IntervalFor(ActiveDataType);
        var timestampWindow = TimestampWindowFor(ActiveDataType);
        await using var database = await ContextFactory.CreateDbContextAsync();
        var loadedReadings = await database.PublishedReadings()
            .Where(x => x.DataType == ActiveDataType && plantIds.Contains(x.PlantId)
                        && (!timestampWindow.Start.HasValue || x.IntervalStart >= timestampWindow.Start.Value)
                        && (!timestampWindow.EndExclusive.HasValue || x.IntervalStart < timestampWindow.EndExclusive.Value)
                        && (!submissionStart.HasValue || x.SubmissionDateTime >= submissionStart.Value)
                        && (!submissionEnd.HasValue || x.SubmissionDateTime < submissionEnd.Value)
                        && (x.IntervalStart.Hour * 60 + x.IntervalStart.Minute) % selectedInterval == 0)
            .OrderByDescending(x => x.Id)
            .ToListAsync();
        if (loadedReadings.Count == 0)
        {
            TableRows = [];
            LoadedReadings = [];
            ValidationMessage = $"No {ActiveDataType} readings match the selected location, submission, date range, and interval.";
            return;
        }

        var readingGroups = loadedReadings
            .GroupBy(reading => reading.IntervalStart)
            .OrderBy(group => group.Key)
            .ToList();
        TableRows = readingGroups.Select(group =>
        {
            var timestampReadings = group.ToList();
            return new DdqTableRow(timestampReadings.Max(reading => reading.SubmissionDateTime), group.Key,
                timestampReadings.GroupBy(reading => reading.PlantId)
                    .ToDictionary(x => x.Key, x => x.First().Value), selectedInterval);
        }).ToList();
        LoadedReadings = loadedReadings;
    }

    private void ResetFilters()
    {
        RangeStartDateValue = string.Empty;
        RangeStartTimeValue = string.Empty;
        RangeEndDateValue = string.Empty;
        RangeEndTimeValue = string.Empty;
        SubmissionStartDate = null;
        SubmissionEndDate = null;
        SelectedStates = new(StringComparer.OrdinalIgnoreCase);
        SelectedRegions = new(StringComparer.OrdinalIgnoreCase);
        SelectedPlantIds = [];
        SelectedDataTypes = ["Rolling 24 Hour"];
        SelectedIntervals = DataTypes.ToDictionary(
            dataType => dataType, dataType => DdqDatasetPolicy.Get(dataType).ReferenceTemplateIntervalMinutes);
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

    private string SelectedIntervalLabel => IntervalText(IntervalFor(ActiveDataType));
    private string SubmissionPeriodLabel => SubmissionStartDate.HasValue && SubmissionEndDate.HasValue
        ? $"{SubmissionStartDate:dd/MM/yyyy} - {SubmissionEndDate:dd/MM/yyyy}"
        : "All available submissions";
    private string TimestampRangeLabel => TimestampRangeLabelFor(ActiveDataType);

    private string TimestampRangeLabelFor(string dataType)
    {
        var window = TimestampWindowFor(dataType);
        return window.Start.HasValue && window.EndExclusive.HasValue
            ? $"{window.Start:dd/MM/yyyy HH:mm} - {window.EndExclusive.Value.AddTicks(-1):dd/MM/yyyy HH:mm}"
            : "All available timestamps";
    }
    private string TimestampRangeDescription
    {
        get
        {
            var window = TimestampWindowFor(ActiveDataType);
            return window.Start.HasValue && window.EndExclusive.HasValue
                ? $"{window.Start:dd MMM yyyy HH:mm} to {window.EndExclusive.Value.AddTicks(-1):dd MMM yyyy HH:mm}"
                : "all available data timestamps";
        }
    }

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
                var selectedInterval = IntervalFor(dataType);
                var timestampWindow = TimestampWindowFor(dataType);
                var readings = await database.PublishedReadings()
                    .Where(reading => reading.DataType == dataType && plantIds.Contains(reading.PlantId)
                        && (!timestampWindow.Start.HasValue || reading.IntervalStart >= timestampWindow.Start.Value)
                        && (!timestampWindow.EndExclusive.HasValue || reading.IntervalStart < timestampWindow.EndExclusive.Value)
                        && (!submissionStart.HasValue || reading.SubmissionDateTime >= submissionStart.Value)
                        && (!submissionEnd.HasValue || reading.SubmissionDateTime < submissionEnd.Value)
                        && (reading.IntervalStart.Hour * 60 + reading.IntervalStart.Minute) % selectedInterval == 0)
                    .OrderByDescending(reading => reading.Id)
                    .ToListAsync();

                var headers = new List<string> { "Submission Date", "Year", "Month", "Day", "Period" };
                headers.AddRange(ResultPlants.Select(plant => TemplatePlantCode(plant.PlantId)));
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
                        group.Max(reading => reading.SubmissionDateTime).ToString("dd/MM/yyyy"),
                        group.Key.Year, group.Key.Month, group.Key.Day,
                        PeriodNumber(group.Key, selectedInterval)
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
                    ("Data Interval", IntervalText(selectedInterval)),
                    ("Data Timestamp Range", TimestampRangeLabelFor(dataType))
                };
                workbookSheets.Add(new DdqWorkbookSheet(dataType,
                    "SOLAR DATA SUBMISSION / DATA MONITORING", metadata, headers, sheetRows, 5));
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
                ("Data Interval", string.Join(", ", DataTypes.Where(SelectedDataTypes.Contains)
                    .Select(dataType => $"{DataTypeLabel(dataType)}: {IntervalText(IntervalFor(dataType))}"))),
                ("Data Timestamp Range", TimestampRangeLabel)
            };
            workbookSheets.Add(new DdqWorkbookSheet("Anomaly Report", "ANOMALY REPORT",
                anomalyMetadata, anomalyHeaders, anomalyRows));
            var bytes = DdqExcelWorkbookExporter.Build(workbookSheets);
            const string fileName = "SBLSS-Filtered-Data.xlsx";
            await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(bytes));
            var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            await AuditTrail.RecordAsync(authenticationState.User, "Download DDQ analytics", "Success",
                "Download", fileName,
                $"Downloaded {string.Join(", ", DataTypes.Where(SelectedDataTypes.Contains))}; {ResultPlants.Count} plant(s); submission period {SubmissionPeriodLabel}.");
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

    private static int PeriodNumber(DateTime timestamp, int intervalMinutes) =>
        (timestamp.Hour * 60 + timestamp.Minute) / Math.Max(1, intervalMinutes) + 1;

    private static string IntervalText(int value) => value == 60 ? "1 hour" : $"{value} minutes";

    private static string TemplatePlantCode(string plantId)
    {
        var normalized = new string(plantId.Where(char.IsLetterOrDigit).ToArray());
        if (normalized.StartsWith("LSS", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized[3..], out var number))
            return $"LSS{number}";
        return plantId;
    }

    private static string DataTypeLabel(string dataType) => DdqDatasetPolicy.Get(dataType).DisplayName;

    private static string DataTypeHorizon(string dataType) => DdqDatasetPolicy.Get(dataType).HorizonDescription;

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

    private sealed record DdqTableRow(DateTime SubmissionDateTime, DateTime Timestamp, Dictionary<int, decimal> PlantValues,
        int IntervalMinutes);
    private sealed record GraphPoint(double X, double Y, DateTime Timestamp, decimal Value);
}
