using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Models;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Services;
using System.Globalization;
using Microsoft.JSInterop;
using TestingSystem.Web.Data;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class DdqAnalytics : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
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
    private HashSet<string> SelectedDataTypes = [];
    private HashSet<string> SelectedStates = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> SelectedRegions = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> SelectedSubmissionIntervals = [];
    private List<DdqTableRow> TableRows = [];
    private List<SolarApiReading> LoadedReadings = [];
    private IReadOnlyList<DdqScheduledSubmissionOption> ScheduledSubmissionOptions = [];
    private HashSet<string> SelectedSubmissionKeys { get; set; } = [];
    private string PendingSubmission { get; set; } = string.Empty;
    private int SubmissionSearchVersion;
    private int FilterVersion;
    private string ActiveDataType { get; set; } = "Rolling 24 Hour";
    private string ActiveSubmissionKey { get; set; } = string.Empty;
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
        await LoadScheduledSubmissionOptionsAsync(database);
    }

    private async Task LoadScheduledSubmissionOptionsAsync(ApplicationDbContext database)
    {
        var sourceRows = await (
            from job in database.CollectionJobs.AsNoTracking()
            join schedule in database.CollectionSchedules.AsNoTracking()
                on job.CollectionScheduleId equals (int?)schedule.Id
            where job.Status == CollectionJobStatus.Completed
                && schedule.ApiType == "DDQ"
                && database.SolarApiReadings.Any(reading => reading.CollectionJobId == job.Id)
            orderby job.SubmissionDateTime descending
            select new DdqScheduledSubmissionSource(
                job.DataType,
                job.SubmissionDateTime,
                schedule.Name,
                job.PlantId))
            .Take(5000)
            .ToListAsync();

        ScheduledSubmissionOptions = DdqScheduledSubmissionPolicy.Build(sourceRows);
    }

    private void AddScheduledSubmission(DdqScheduledSubmissionOption option)
    {
        SelectedSubmissionKeys.Add(option.Key);
        SelectedSubmissionIntervals.TryAdd(option.Key,
            DdqDatasetPolicy.Get(option.DataType).ReferenceTemplateIntervalMinutes);
        SynchroniseSelectedDataTypes();
        ValidationMessage = string.Empty;
    }

    private void ToggleScheduledSubmission(string key, object? value)
    {
        var option = ScheduledSubmissionOptions.FirstOrDefault(candidate => candidate.Key == key);
        if (option is null) return;

        if (value is true)
        {
            AddScheduledSubmission(option);
        }
        else
        {
            SelectedSubmissionKeys.Remove(key);
            SelectedSubmissionIntervals.Remove(key);
            SynchroniseSelectedDataTypes();
        }
    }

    private void RemoveScheduledSubmission(string key) => ToggleScheduledSubmission(key, false);

    private void SynchroniseSelectedDataTypes()
    {
        SelectedDataTypes = SelectedSubmissionOptions
            .Select(option => option.DataType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedOptions = SelectedSubmissionOptions;
        if (selectedOptions.Count == 0)
        {
            ActiveDataType = "Rolling 24 Hour";
            ActiveSubmissionKey = string.Empty;
            return;
        }

        if (!SelectedSubmissionKeys.Contains(ActiveSubmissionKey))
            ActiveSubmissionKey = selectedOptions[0].Key;
        ActiveDataType = selectedOptions.First(option => option.Key == ActiveSubmissionKey).DataType;
    }

    private IReadOnlyList<DdqScheduledSubmissionOption> SelectedSubmissionOptions =>
        ScheduledSubmissionOptions.Where(option => SelectedSubmissionKeys.Contains(option.Key)).ToArray();

    private IEnumerable<DdqScheduledSubmissionOption> AvailableSubmissionOptions =>
        ScheduledSubmissionOptions.Where(option => !SelectedSubmissionKeys.Contains(option.Key));

    private static string SubmissionOptionText(DdqScheduledSubmissionOption option) => option.DisplayLabel;

    private string SubmissionSelectionStatus => SelectedSubmissionOptions.Count == 0
        ? "Select from the suggestions"
        : $"{SelectedSubmissionOptions.Count} selected · ready for another";

    private string SubmissionHelpText => ScheduledSubmissionOptions.Count == 0
        ? "No completed scheduled DDQ collection is available yet. Run Collection Schedule first."
        : SelectedSubmissionOptions.Count == 0
            ? "No submission selected. Choose one or more completed collections."
            : $"{SelectedSubmissionOptions.Count} submission(s) selected. Add more submissions or remove a selection.";

    private void AddSubmissionFromSelection(ChangeEventArgs args)
    {
        PendingSubmission = args.Value?.ToString()?.Trim() ?? string.Empty;
        var option = AvailableSubmissionOptions.FirstOrDefault(candidate =>
            SubmissionOptionText(candidate).Equals(PendingSubmission, StringComparison.OrdinalIgnoreCase));
        if (option is null) return;

        AddScheduledSubmission(option);
        PendingSubmission = string.Empty;
        SubmissionSearchVersion++;
    }

    private void OnSubmissionIntervalChanged(string key, ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var interval)
            && Intervals.Any(item => item.Value == interval))
            SelectedSubmissionIntervals[key] = interval;
    }

    private int IntervalFor(DdqScheduledSubmissionOption option) =>
        SelectedSubmissionIntervals.GetValueOrDefault(option.Key,
            DdqDatasetPolicy.Get(option.DataType).ReferenceTemplateIntervalMinutes);

    private Task OnPlantsChanged(HashSet<int> plantIds)
    {
        SelectedPlantIds = plantIds;
        if (plantIds.Count > 0)
        {
            SelectedStates.Clear();
            SelectedRegions.Clear();
        }
        ValidationMessage = string.Empty;
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
        ValidationMessage = string.Empty;
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
        ValidationMessage = string.Empty;
        return Task.CompletedTask;
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

        if (SelectedSubmissionOptions.Count == 0)
        {
            ValidationMessage = "Select at least one completed DDQ submission.";
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

        ActiveSubmissionKey = SelectedSubmissionOptions[0].Key;
        ActiveDataType = SelectedSubmissionOptions[0].DataType;
        HiddenGraphPlantIds.Clear();
        LegendSearch = string.Empty;
        await LoadTableRowsAsync();
        HasApplied = true;

        var location = SelectedPlantIds.Count > 0 ? $"{SelectedPlantIds.Count} selected plant(s)"
            : SelectedStates.Count > 0 ? $"{SelectedStates.Count} selected state(s)"
            : SelectedRegions.Count > 0 ? $"{SelectedRegions.Count} selected region(s)"
            : "all active plants";
        var intervals = string.Join(", ", SelectedSubmissionOptions
            .Select(option => $"{option.DataType} {option.SubmissionDateTime:dd MMM HH:mm}: {IntervalText(IntervalFor(option))}"));
        ResultDescription = $"{ResultPlants.Count} plant(s) from {location}; {SelectedSubmissionOptions.Count} submission(s); "
            + $"{TimestampRangeDescription}; {intervals}.";

    }

    private async Task SelectSubmissionTab(string submissionKey)
    {
        ValidationMessage = string.Empty;
        var option = SelectedSubmissionOptions.FirstOrDefault(candidate => candidate.Key == submissionKey);
        if (option is null) return;
        ActiveSubmissionKey = option.Key;
        ActiveDataType = option.DataType;
        await LoadTableRowsAsync();
    }

    private async Task LoadTableRowsAsync()
    {
        var plantIds = ResultPlants.Select(x => x.Id).ToArray();
        var activeOption = SelectedSubmissionOptions.FirstOrDefault(option => option.Key == ActiveSubmissionKey);
        if (activeOption is null)
        {
            TableRows = [];
            LoadedReadings = [];
            return;
        }
        var submissionStart = activeOption.SubmissionDateTime;
        var submissionEnd = submissionStart.AddMinutes(1);
        var selectedInterval = IntervalFor(activeOption);
        await using var database = await ContextFactory.CreateDbContextAsync();
        var loadedReadings = await database.PublishedReadings()
            .Where(x => x.DataType == ActiveDataType && plantIds.Contains(x.PlantId)
                        && x.SubmissionDateTime >= submissionStart
                        && x.SubmissionDateTime < submissionEnd
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
            .GroupBy(reading => new { reading.SubmissionDateTime, reading.IntervalStart })
            .OrderBy(group => group.Key.SubmissionDateTime).ThenBy(group => group.Key.IntervalStart)
            .ToList();
        TableRows = readingGroups.Select(group =>
        {
            var timestampReadings = group.ToList();
            return new DdqTableRow(group.Key.SubmissionDateTime, group.Key.IntervalStart,
                timestampReadings.GroupBy(reading => reading.PlantId)
                    .ToDictionary(x => x.Key, x => x.First().Value), selectedInterval);
        }).ToList();
        LoadedReadings = loadedReadings;
    }

    private void ResetFilters()
    {
        SelectedSubmissionKeys = [];
        PendingSubmission = string.Empty;
        SubmissionSearchVersion++;
        SelectedStates = new(StringComparer.OrdinalIgnoreCase);
        SelectedRegions = new(StringComparer.OrdinalIgnoreCase);
        SelectedPlantIds = [];
        SelectedDataTypes = [];
        SelectedSubmissionIntervals = [];
        ActiveDataType = "Rolling 24 Hour";
        ActiveSubmissionKey = string.Empty;
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

    private string SelectedIntervalLabel
    {
        get
        {
            var option = SelectedSubmissionOptions.FirstOrDefault(candidate => candidate.Key == ActiveSubmissionKey);
            return option is null ? "No interval selected" : IntervalText(IntervalFor(option));
        }
    }

    private string SubmissionDateTimeLabel => SelectedSubmissionOptions.Count switch
    {
        0 => "No submission selected",
        1 => TimeDisplay.DateTime(SelectedSubmissionOptions[0].SubmissionDateTime),
        _ => $"{SelectedSubmissionOptions.Count} submissions selected"
    };

    private string ActiveSubmissionDateTimeLabel
    {
        get
        {
            var option = SelectedSubmissionOptions.FirstOrDefault(candidate => candidate.Key == ActiveSubmissionKey);
            return option is null ? "No submission selected" : TimeDisplay.DateTime(option.SubmissionDateTime);
        }
    }
    private string TimestampRangeLabel => ReadingTimestampRange(LoadedReadings);
    private string TimestampRangeDescription => LoadedReadings.Count == 0
        ? "all timestamps returned by the API"
        : $"API data timestamps {LoadedReadings.Min(reading => reading.IntervalStart):dd MMM yyyy HH:mm} to {LoadedReadings.Max(reading => reading.IntervalStart):dd MMM yyyy HH:mm}";

    private static string ReadingTimestampRange(IReadOnlyCollection<SolarApiReading> readings) => readings.Count == 0
        ? "No matching timestamps"
        : $"{readings.Min(reading => reading.IntervalStart):dd/MM/yyyy HH:mm} - {readings.Max(reading => reading.IntervalStart):dd/MM/yyyy HH:mm}";

    private async Task DownloadExcelAsync()
    {
        if (IsExporting || ResultPlants.Count == 0) return;
        IsExporting = true;
        try
        {
            var workbookSheets = new List<DdqWorkbookSheet>();
            var anomalyRows = new List<IReadOnlyList<object?>>();
            var plantIds = ResultPlants.Select(plant => plant.Id).ToArray();
            await using var database = await ContextFactory.CreateDbContextAsync();
            var validationRules = await database.ValidationRules.AsNoTracking()
                .Where(rule => rule.DataType == "DDQ" && rule.IsActive).ToListAsync();

            foreach (var selectedSubmission in SelectedSubmissionOptions)
            {
                var dataType = selectedSubmission.DataType;
                var selectedInterval = IntervalFor(selectedSubmission);
                var submissionStart = selectedSubmission.SubmissionDateTime;
                var submissionEnd = submissionStart.AddMinutes(1);
                var readings = await database.PublishedReadings()
                    .Where(reading => reading.DataType == dataType && plantIds.Contains(reading.PlantId)
                        && reading.SubmissionDateTime >= submissionStart
                        && reading.SubmissionDateTime < submissionEnd
                        && (reading.IntervalStart.Hour * 60 + reading.IntervalStart.Minute) % selectedInterval == 0)
                    .OrderByDescending(reading => reading.Id)
                    .ToListAsync();

                var effectiveSubmission = submissionStart;
                var previousSubmission = effectiveSubmission.AddDays(-1);
                var previousReadings = await database.PublishedReadings()
                    .Where(reading => reading.DataType == dataType && plantIds.Contains(reading.PlantId)
                        && reading.SubmissionDateTime == previousSubmission)
                    .OrderByDescending(reading => reading.Id).ToListAsync();
                var prepared = ReportSubstitutionPolicy.PrepareDdq(dataType, effectiveSubmission,
                    ResultPlants, readings, previousReadings, validationRules);
                readings = prepared.Readings.Where(reading =>
                        (reading.IntervalStart.Hour * 60 + reading.IntervalStart.Minute) % selectedInterval == 0)
                    .ToList();
                anomalyRows.AddRange(prepared.Details.Select(detail => new object?[]
                {
                    dataType, effectiveSubmission.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    detail.DataTimestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ResultPlants.FirstOrDefault(plant => plant.Id == detail.PlantId)?.PlantId ?? detail.PlantId.ToString(),
                    ResultPlants.FirstOrDefault(plant => plant.Id == detail.PlantId)?.Name ?? "Unknown plant",
                    detail.Parameter, detail.OriginalValue, detail.ReportValue, detail.RuleCode, detail.RuleName,
                    detail.AnomalyDetail, detail.SubstitutionSource, detail.SourceReference
                }));

                var headers = new List<string> { "Submission Date & Time", "Year", "Month", "Day", "Period" };
                headers.AddRange(ResultPlants.Select(plant => TemplatePlantCode(plant.PlantId)));
                var sheetRows = new List<IReadOnlyList<object?>>();
                var groupedReadings = readings.GroupBy(reading => new { reading.SubmissionDateTime, reading.IntervalStart })
                    .OrderBy(group => group.Key.SubmissionDateTime).ThenBy(group => group.Key.IntervalStart).ToList();

                for (var rowIndex = 0; rowIndex < groupedReadings.Count; rowIndex++)
                {
                    var group = groupedReadings[rowIndex];
                    var latestByPlant = group.GroupBy(reading => reading.PlantId)
                        .ToDictionary(items => items.Key, items => items.First());
                    var values = new List<object?>
                    {
                        group.Key.SubmissionDateTime.ToString("dd/MM/yyyy HH:mm:ss"),
                        group.Key.IntervalStart.Year, group.Key.IntervalStart.Month, group.Key.IntervalStart.Day,
                        PeriodNumber(group.Key.IntervalStart, selectedInterval)
                    };
                    foreach (var plant in ResultPlants)
                    {
                        if (latestByPlant.TryGetValue(plant.Id, out var reading))
                        {
                            values.Add(reading.Value);
                        }
                        else
                        {
                            values.Add(null);
                        }
                    }
                    sheetRows.Add(values);
                }
                var metadata = new (string Label, string Value)[]
                {
                    ("Data Type", dataType),
                    ("Submission Date & Time", TimeDisplay.DateTime(selectedSubmission.SubmissionDateTime)),
                    ("Data Interval", IntervalText(selectedInterval)),
                    ("Data Timestamp Range", ReadingTimestampRange(readings))
                };
                workbookSheets.Add(new DdqWorkbookSheet($"{dataType} {selectedSubmission.SubmissionDateTime:yyyyMMdd HHmm}",
                    "SOLAR DATA SUBMISSION / DATA MONITORING", metadata, headers, sheetRows, 5));
            }

            var anomalyHeaders = new[]
            {
                "Data Type", "Submission Date & Time", "Data Timestamp",
                "Plant ID", "Plant Name", "Field / Parameter", "Original Value", "Report Value",
                "Rule Code", "Rule Name", "Anomaly Detail", "Substitution Source", "Source Reference"
            };
            var anomalyMetadata = new (string Label, string Value)[]
            {
                ("Data Type", string.Join(", ", DataTypes.Where(SelectedDataTypes.Contains))),
                ("Submission Date & Time", SubmissionDateTimeLabel),
                ("Data Interval", string.Join(", ", SelectedSubmissionOptions
                    .Select(option => $"{option.DataType} {option.SubmissionDateTime:dd MMM HH:mm}: {IntervalText(IntervalFor(option))}"))),
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
                $"Downloaded {string.Join(", ", DataTypes.Where(SelectedDataTypes.Contains))}; {ResultPlants.Count} plant(s); submission date and time {SubmissionDateTimeLabel}.");
        }
        finally
        {
            IsExporting = false;
        }
    }

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

    private void ShowAllGraphPlants() => HiddenGraphPlantIds.Clear();

    private void HideAllGraphPlants() => HiddenGraphPlantIds = ResultPlants.Select(plant => plant.Id).ToHashSet();

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
