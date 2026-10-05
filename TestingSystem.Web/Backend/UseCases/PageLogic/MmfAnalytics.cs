using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

namespace TestingSystem.Web.Components.Frontend.UseCases;

public partial class MmfAnalytics : ComponentBase
{
    [Inject] private IDbContextFactory<ApplicationDbContext> ContextFactory { get; set; } = default!;
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;

    private static readonly (string Label, int Value)[] Intervals =
        [("15 minutes", 15), ("30 minutes", 30), ("1 hour", 60)];
    private static readonly string[] GraphColours =
        ["#0875c9", "#00a56a", "#f2a900", "#d64545", "#7656d6", "#00a6a6", "#e06c00", "#c13f87", "#54708f", "#4c9b35"];

    private List<LssPlant> RegisteredPlants = [], ResultPlants = [];
    private List<string> AvailableParameters = [], AppliedParameters = [];
    private HashSet<int> SelectedPlantIds = [], HiddenGraphPlantIds = [];
    private HashSet<string> SelectedParameters = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> SelectedStates = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> SelectedRegions = new(StringComparer.OrdinalIgnoreCase);
    private List<SolarApiReading> LoadedReadings = [];
    private List<DateTime> TimeColumns = [];
    private Dictionary<ReadingKey, SolarApiReading> ReadingIndex = [];
    private string ActiveParameter = "", ValidationMessage = "";
    private string ResultDescription = "", DisplayMode = "table", LegendSearch = "";
    private DateTime StartDate = DateTime.Today, EndDate = DateTime.Today;
    private DateTime DefaultStartDate = DateTime.Today, DefaultEndDate = DateTime.Today;
    private TimeOnly StartTime = TimeOnly.MinValue, EndTime = new(23, 45);
    private TimeOnly DefaultStartTime = TimeOnly.MinValue, DefaultEndTime = new(23, 45);
    private int SelectedInterval = 15, AppliedInterval = 15, FilterVersion, ParameterFilterVersion;
    private int AppliedSourcePlantCount;
    private bool HasApplied, ShowGraphCrosshair = true, IsExporting, IsLoading;
    private DateTime AppliedStart, AppliedEnd;
    private string AppliedLocationLabel = "";
    private string AppliedScope = "Plant";
    private decimal GraphMinimumValue, GraphMaximumValue = 1;

    private IEnumerable<string> AvailableStates => RegisteredPlants.Select(x => x.State)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x);
    private IEnumerable<string> AvailableRegions => RegisteredPlants.Select(x => AnalyticsPlantFilter.RegionFor(x.State))
        .Where(x => x != "Not assigned").Distinct().OrderBy(x => x);
    private bool PlantFilterDisabled => SelectedStates.Count > 0 || SelectedRegions.Count > 0;
    private bool StateFilterDisabled => SelectedPlantIds.Count > 0 || SelectedRegions.Count > 0;
    private bool RegionFilterDisabled => SelectedPlantIds.Count > 0 || SelectedStates.Count > 0;
    private bool IsLocationAverage => AppliedScope is "State" or "Region";
    private int VisibleGraphPlantCount => ResultPlants.Count(x => !HiddenGraphPlantIds.Contains(x.Id));

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await using var db = await ContextFactory.CreateDbContextAsync();
            RegisteredPlants = await db.LssPlants.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            await RefreshSourceMetadataAsync(db, true);
        }
        catch (Exception exception)
        {
            ValidationMessage = $"MMF data could not be loaded: {exception.GetBaseException().Message}";
        }
    }

    private Task OnPlantsChanged(HashSet<int> ids)
    {
        SelectedPlantIds = ids;
        if (ids.Count > 0) { SelectedStates.Clear(); SelectedRegions.Clear(); }
        return Task.CompletedTask;
    }

    private Task OnStatesChanged(HashSet<string> states)
    {
        SelectedStates = states;
        if (states.Count > 0) { SelectedRegions.Clear(); SelectedPlantIds = []; FilterVersion++; }
        return Task.CompletedTask;
    }

    private Task OnRegionsChanged(HashSet<string> regions)
    {
        SelectedRegions = regions;
        if (regions.Count > 0) { SelectedStates.Clear(); SelectedPlantIds = []; FilterVersion++; }
        return Task.CompletedTask;
    }

    private async Task ApplyFilters()
    {
        if (IsLoading) return;
        ValidationMessage = "";
        HasApplied = false;
        await using (var metadataDb = await ContextFactory.CreateDbContextAsync())
            await RefreshSourceMetadataAsync(metadataDb, false);
        var start = StartDate.Date.Add(StartTime.ToTimeSpan());
        var end = EndDate.Date.Add(EndTime.ToTimeSpan());
        if (end <= start)
        {
            ValidationMessage = "The end date and time must be later than the start date and time.";
            return;
        }

        var matchedPlants = AnalyticsPlantFilter.Match(
            RegisteredPlants, SelectedPlantIds, SelectedStates, SelectedRegions).ToList();
        if (matchedPlants.Count == 0)
        {
            ValidationMessage = "No active plants match the selected plant, state, or region.";
            return;
        }

        IsLoading = true;
        try
        {
            var ids = matchedPlants.Select(x => x.Id).ToArray();
            var allowedParameters = MmfParameterCatalog.Names.ToArray();
            await using var db = await ContextFactory.CreateDbContextAsync();
            LoadedReadings = await db.PublishedReadings()
                .Where(x => x.DataType == "MMF" && ids.Contains(x.PlantId)
                    && allowedParameters.Contains(x.Parameter)
                    && x.IntervalStart >= start && x.IntervalStart <= end
                    && x.IntervalStart.Second == 0 && x.IntervalStart.Minute % SelectedInterval == 0
                    && (SelectedParameters.Count == 0 || SelectedParameters.Contains(x.Parameter)))
                .OrderByDescending(x => x.Id).ToListAsync();

            AppliedSourcePlantCount = matchedPlants.Count;
            if (SelectedStates.Count > 0)
            {
                AppliedScope = "State";
                AppliedLocationLabel = string.Join(", ", SelectedStates.OrderBy(value => value));
                BuildLocationAverages(matchedPlants, SelectedStates.OrderBy(value => value), "STATE",
                    (plant, location) => plant.State.Equals(location, StringComparison.OrdinalIgnoreCase));
            }
            else if (SelectedRegions.Count > 0)
            {
                AppliedScope = "Region";
                AppliedLocationLabel = string.Join(", ", SelectedRegions.OrderBy(value => value));
                BuildLocationAverages(matchedPlants, SelectedRegions.OrderBy(value => value), "REGION",
                    (plant, location) => AnalyticsPlantFilter.RegionFor(plant.State)
                        .Equals(location, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                AppliedScope = "Plant";
                AppliedLocationLabel = "";
                ResultPlants = matchedPlants;
            }

            RebuildReadingIndex();
            AppliedParameters = ReadingIndex.Values.Select(x => MmfParameterCatalog.DisplayName(x.Parameter))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(MmfParameterCatalog.OrderOf).ToList();
            ActiveParameter = AppliedParameters.FirstOrDefault() ?? "";
            AppliedStart = start;
            AppliedEnd = end;
            AppliedInterval = SelectedInterval;
            RefreshActiveParameter();
            HiddenGraphPlantIds.Clear();
            LegendSearch = "";
            DisplayMode = "table";
            HasApplied = true;
            ResultDescription = IsLocationAverage
                ? $"{ReadingIndex.Count:N0} MMF reading(s) across {ResultPlants.Count} {AppliedScope.ToLowerInvariant()} average(s), calculated from {AppliedSourcePlantCount} LSS plant(s), " +
                  $"{AppliedParameters.Count} parameter(s), {start:dd MMM yyyy HH:mm} to {end:dd MMM yyyy HH:mm}, {IntervalLabel}."
                : $"{ReadingIndex.Count:N0} averaged MMF reading(s), {ResultPlants.Count} plant(s), " +
                  $"{AppliedParameters.Count} parameter(s), {start:dd MMM yyyy HH:mm} to {end:dd MMM yyyy HH:mm}, {IntervalLabel}.";
            if (ReadingIndex.Count == 0)
                ValidationMessage = "No imported MMF readings match the selected location, parameters, interval, and forecast range.";

        }
        catch (Exception exception)
        {
            ClearResults();
            ValidationMessage = $"The MMF query could not be completed: {exception.GetBaseException().Message}";
        }
        finally { IsLoading = false; }
    }

    private void BuildLocationAverages(List<LssPlant> matchedPlants, IEnumerable<string> locations,
        string codePrefix, Func<LssPlant, string, bool> belongsToLocation)
    {
        var sourceReadings = LoadedReadings;
        var averagedReadings = new List<SolarApiReading>();
        var locationRows = new List<LssPlant>();
        var index = 0;
        foreach (var location in locations)
        {
            var aggregatePlantId = -(++index);
            var plantIds = matchedPlants.Where(plant => belongsToLocation(plant, location))
                .Select(plant => plant.Id).ToHashSet();
            averagedReadings.AddRange(MmfLocationAveraging.AverageLatestByLocation(
                sourceReadings.Where(reading => plantIds.Contains(reading.PlantId)), aggregatePlantId));
            locationRows.Add(new LssPlant
            {
                Id = aggregatePlantId,
                PlantId = $"{codePrefix}-{NormalizeCode(location)}",
                Name = location,
                State = AppliedScope == "State" ? location : $"{location} region",
                IsActive = true
            });
        }
        LoadedReadings = averagedReadings;
        ResultPlants = locationRows;
    }

    private async Task RefreshSourceMetadataAsync(ApplicationDbContext db, bool initializeRange)
    {
        var previousDefaultStart = DefaultStartDate.Date.Add(DefaultStartTime.ToTimeSpan());
        var previousDefaultEnd = DefaultEndDate.Date.Add(DefaultEndTime.ToTimeSpan());
        var currentStart = StartDate.Date.Add(StartTime.ToTimeSpan());
        var currentEnd = EndDate.Date.Add(EndTime.ToTimeSpan());
        var wasUsingDefaults = currentStart == previousDefaultStart && currentEnd == previousDefaultEnd;

        AvailableParameters = MmfParameterCatalog.Names.ToList();
        var window = await db.PublishedReadings().Where(x => x.DataType == "MMF").GroupBy(_ => 1)
            .Select(g => new { Start = g.Min(x => x.IntervalStart), End = g.Max(x => x.IntervalStart) }).FirstOrDefaultAsync();
        if (window is null)
        {
            ValidationMessage = "No MMF readings are available yet. Run a successful MMF collection before applying a filter.";
            return;
        }

        DefaultStartDate = window.Start.Date;
        DefaultStartTime = TimeOnly.FromDateTime(window.Start);
        DefaultEndDate = window.End.Date;
        DefaultEndTime = TimeOnly.FromDateTime(window.End);
        if (initializeRange || wasUsingDefaults) RestoreDefaultRange();
    }

    private void RebuildReadingIndex()
    {
        ReadingIndex = [];
        foreach (var reading in LoadedReadings)
            ReadingIndex.TryAdd(new(reading.Parameter.ToUpperInvariant(), reading.PlantId, reading.IntervalStart), reading);
    }

    private void SelectParameter(string parameter) { ActiveParameter = parameter; RefreshActiveParameter(); }
    private void RefreshActiveParameter()
    {
        TimeColumns = string.IsNullOrWhiteSpace(ActiveParameter) ? [] : ReadingIndex.Values
            .Where(x => x.Parameter.Equals(ActiveParameter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.IntervalStart).Distinct().OrderBy(x => x).ToList();
        RecalculateGraphBounds();
    }
    private void SetDisplayMode(string mode) => DisplayMode = mode is "graph" ? "graph" : "table";
    private SolarApiReading? ReadingFor(string parameter, int plantId, DateTime timestamp) =>
        ReadingIndex.GetValueOrDefault(new(parameter.ToUpperInvariant(), plantId, timestamp));
    private decimal? ValueFor(int plantId, DateTime timestamp) => ReadingFor(ActiveParameter, plantId, timestamp)?.Value;
    private string UnitForParameter => MmfParameterCatalog.UnitFor(ActiveParameter) is { Length: > 0 } catalogUnit
        ? catalogUnit
        : ReadingIndex.Values.FirstOrDefault(x =>
            x.Parameter.Equals(ActiveParameter, StringComparison.OrdinalIgnoreCase))?.Unit ?? "";
    private string SubmissionDateTimeRange
    {
        get
        {
            if (ReadingIndex.Count == 0) return "No submission selected";
            var start = ReadingIndex.Values.Min(reading => reading.SubmissionDateTime);
            var end = ReadingIndex.Values.Max(reading => reading.SubmissionDateTime);
            return start == end ? TimeDisplay.DateTime(start)
                : $"{start:dd/MM/yyyy HH:mm} - {end:dd/MM/yyyy HH:mm}";
        }
    }
    private string TimeHeading(DateTime value) => AppliedStart.Date == AppliedEnd.Date
        ? value.ToString("HH:mm") : value.ToString("dd/MM HH:mm");
    private static string HourHeading(int index) => $"HR{index + 1}";
    private void ToggleGraphPlant(int id) { if (!HiddenGraphPlantIds.Add(id)) HiddenGraphPlantIds.Remove(id); RecalculateGraphBounds(); }
    private void ShowAllGraphPlants() { HiddenGraphPlantIds.Clear(); RecalculateGraphBounds(); }
    private void HideAllGraphPlants() { HiddenGraphPlantIds = ResultPlants.Select(x => x.Id).ToHashSet(); RecalculateGraphBounds(); }
    private void ToggleGraphCrosshair() => ShowGraphCrosshair = !ShowGraphCrosshair;
    private IEnumerable<LssPlant> FilteredLegendPlants => string.IsNullOrWhiteSpace(LegendSearch) ? ResultPlants :
        ResultPlants.Where(x => x.Name.Contains(LegendSearch, StringComparison.OrdinalIgnoreCase)
            || x.PlantId.Contains(LegendSearch, StringComparison.OrdinalIgnoreCase));
    private string GraphColour(LssPlant plant) => GraphColours[Math.Max(0, ResultPlants.FindIndex(x => x.Id == plant.Id)) % GraphColours.Length];
    private decimal GraphMinimum => GraphMinimumValue;
    private decimal GraphMaximum => GraphMaximumValue;
    private string GraphTickFormat => AnalyticsGraphScale.TickFormat(GraphMaximum - GraphMinimum);
    private void RecalculateGraphBounds()
    {
        var bounds = AnalyticsGraphScale.Bounds(ReadingIndex.Values
        .Where(x => x.Parameter.Equals(ActiveParameter, StringComparison.OrdinalIgnoreCase) && !HiddenGraphPlantIds.Contains(x.PlantId))
        .Select(x => x.Value));
        GraphMinimumValue = bounds.Minimum;
        GraphMaximumValue = bounds.Maximum;
    }

    private string GraphPoints(LssPlant plant)
    {
        const double left = 74, top = 24, width = 990, height = 340;
        var denominator = Math.Max(1, TimeColumns.Count - 1);
        var minimum = (double)GraphMinimum;
        var range = Math.Max(0.0000001, (double)(GraphMaximum - GraphMinimum));
        return string.Join(' ', TimeColumns.Select((time, index) => (time, index, value: ValueFor(plant.Id, time)))
            .Where(x => x.value.HasValue).Select(x =>
                $"{(left + x.index / (double)denominator * width).ToString("F1", CultureInfo.InvariantCulture)}," +
                $"{(top + (1 - ((double)x.value!.Value - minimum) / range) * height).ToString("F1", CultureInfo.InvariantCulture)}"));
    }

    private IEnumerable<GraphPoint> GraphPointItems(LssPlant plant)
    {
        const double left = 74, top = 24, width = 990, height = 340;
        var denominator = Math.Max(1, TimeColumns.Count - 1);
        var minimum = (double)GraphMinimum;
        var range = Math.Max(0.0000001, (double)(GraphMaximum - GraphMinimum));
        for (var index = 0; index < TimeColumns.Count; index++)
        {
            var value = ValueFor(plant.Id, TimeColumns[index]);
            if (value.HasValue) yield return new(left + index / (double)denominator * width,
                top + (1 - ((double)value.Value - minimum) / range) * height, TimeColumns[index], value.Value);
        }
    }

    private IEnumerable<(double Y, decimal Value)> GraphYTicks()
    {
        const double top = 24, height = 340;
        for (var index = 0; index <= 5; index++)
            yield return (top + height - index / 5d * height,
                GraphMinimum + (GraphMaximum - GraphMinimum) * index / 5m);
    }

    private IEnumerable<(double X, DateTime Value)> GraphXTicks()
    {
        const double left = 74, width = 990;
        if (TimeColumns.Count == 0) yield break;
        var last = TimeColumns.Count - 1;
        for (var index = 0; index <= 4; index++)
        {
            var timeIndex = (int)Math.Round(last * index / 4d);
            yield return (left + index / 4d * width, TimeColumns[timeIndex]);
        }
    }

    private async Task DownloadExcelAsync()
    {
        if (IsExporting || ReadingIndex.Count == 0) return;
        IsExporting = true;
        try
        {
            await using var database = await ContextFactory.CreateDbContextAsync();
            var sourcePlants = IsLocationAverage
                ? RegisteredPlants.Where(plant => AppliedScope == "State"
                    ? SelectedStates.Contains(plant.State)
                    : SelectedRegions.Contains(AnalyticsPlantFilter.RegionFor(plant.State))).ToList()
                : ResultPlants;
            var sourcePlantIds = sourcePlants.Select(plant => plant.Id).ToArray();
            var current = await database.PublishedReadings().Where(reading => reading.DataType == "MMF"
                    && sourcePlantIds.Contains(reading.PlantId) && AppliedParameters.Contains(reading.Parameter)
                    && reading.IntervalStart >= AppliedStart && reading.IntervalStart <= AppliedEnd
                    && reading.IntervalStart.Second == 0 && reading.IntervalStart.Minute % AppliedInterval == 0)
                .OrderByDescending(reading => reading.Id).ToListAsync();
            var previousStart = AppliedStart.AddDays(-1);
            var previousEnd = AppliedEnd.AddDays(-1);
            var previous = await database.PublishedReadings().Where(reading => reading.DataType == "MMF"
                    && sourcePlantIds.Contains(reading.PlantId) && AppliedParameters.Contains(reading.Parameter)
                    && reading.IntervalStart >= previousStart && reading.IntervalStart <= previousEnd)
                .OrderByDescending(reading => reading.Id).ToListAsync();
            var rules = await database.ValidationRules.AsNoTracking()
                .Where(rule => rule.DataType == "MMF" && rule.IsActive).ToListAsync();
            var prepared = ReportSubstitutionPolicy.PrepareMmfRange(AppliedStart, AppliedEnd,
                AppliedInterval, sourcePlants, current, previous, rules);
            var exportReadings = prepared.Readings.ToList();
            if (IsLocationAverage)
            {
                var averaged = new List<SolarApiReading>();
                var locations = (AppliedScope == "State" ? SelectedStates : SelectedRegions)
                    .OrderBy(value => value).ToList();
                for (var index = 0; index < locations.Count; index++)
                {
                    var location = locations[index];
                    var ids = sourcePlants.Where(plant => AppliedScope == "State"
                            ? plant.State.Equals(location, StringComparison.OrdinalIgnoreCase)
                            : AnalyticsPlantFilter.RegionFor(plant.State).Equals(location, StringComparison.OrdinalIgnoreCase))
                        .Select(plant => plant.Id).ToHashSet();
                    averaged.AddRange(MmfLocationAveraging.AverageLatestByLocation(
                        exportReadings.Where(reading => ids.Contains(reading.PlantId)), -(index + 1)));
                }
                exportReadings = averaged;
            }
            var exportIndex = exportReadings.GroupBy(reading =>
                    new ReadingKey(reading.Parameter.ToUpperInvariant(), reading.PlantId, reading.IntervalStart))
                .ToDictionary(group => group.Key, group => group.OrderByDescending(reading => reading.Id).First());
            var sheets = new List<DdqWorkbookSheet>();
            var anomalies = prepared.Details.Select(detail => (IReadOnlyList<object?>)new object?[]
            {
                detail.Parameter, detail.DataTimestamp.ToString("dd/MM/yyyy HH:mm"),
                sourcePlants.FirstOrDefault(plant => plant.Id == detail.PlantId)?.PlantId ?? detail.PlantId.ToString(),
                sourcePlants.FirstOrDefault(plant => plant.Id == detail.PlantId)?.Name ?? "Unknown plant",
                detail.OriginalValue, detail.ReportValue, detail.RuleCode, detail.RuleName,
                detail.AnomalyDetail, detail.SubstitutionSource, detail.SourceReference
            }).ToList();
            foreach (var parameter in AppliedParameters)
            {
                var times = exportIndex.Values.Where(x => x.Parameter.Equals(parameter, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.IntervalStart).Distinct().OrderBy(x => x).ToList();
                var headers = new List<string> { ScopeColumnHeading };
                headers.AddRange(times.Select((_, index) => HourHeading(index)));
                var rows = new List<IReadOnlyList<object?>>();
                foreach (var plant in ResultPlants)
                {
                    var values = new List<object?> { ResultRowLabel(plant) };
                    foreach (var time in times)
                    {
                        var reading = exportIndex.GetValueOrDefault(new(parameter.ToUpperInvariant(), plant.Id, time));
                        values.Add(reading?.Value);
                    }
                    rows.Add(values);
                }
                sheets.Add(new(parameter, parameter.ToUpperInvariant(), Metadata(parameter), headers, rows, 1));
            }
            sheets.Add(new("Anomaly Report", "ANOMALY REPORT", Metadata("All selected parameters"),
                ["Parameter", "Data Timestamp", "Plant ID", "Plant Name", "Original Value", "Report Value",
                 "Rule Code", "Rule Name", "Anomaly Detail", "Substitution Source", "Source Reference"], anomalies));
            var bytes = DdqExcelWorkbookExporter.Build(sheets);
            const string fileName = "SBLSS-MMF-Filtered-Data.xlsx";
            await JavaScript.InvokeVoidAsync("sblssDownloadBase64", fileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(bytes));
            var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            await AuditTrail.RecordAsync(authenticationState.User, "Download MMF analytics", "Success",
                "Download", fileName,
                $"Downloaded {AppliedParameters.Count} parameter(s); {AppliedScope} scope; {ResultPlants.Count} result row(s); data interval {IntervalLabel}.");
        }
        catch (Exception exception)
        {
            ValidationMessage = $"The Excel file could not be prepared: {exception.GetBaseException().Message}";
        }
        finally { IsExporting = false; }
    }

    private (string Label, string Value)[] Metadata(string parameter) =>
        [("Parameter", parameter), ("Data Interval", IntervalLabel),
         ("Submission Date & Time", SubmissionDateTimeRange),
         ("Result Scope", IsLocationAverage ? $"{ResultPlants.Count} {AppliedScope.ToLowerInvariant()} average(s) across {AppliedSourcePlantCount} LSS plant(s)" : $"{ResultPlants.Count} selected LSS plant(s)"),
         ("Date Range", $"{AppliedStart:dd/MM/yyyy HH:mm} - {AppliedEnd:dd/MM/yyyy HH:mm}")];

    private void RestoreDefaultRange()
    {
        StartDate = DefaultStartDate; StartTime = DefaultStartTime;
        EndDate = DefaultEndDate; EndTime = DefaultEndTime;
    }

    private void ResetFilters()
    {
        SelectedPlantIds = [];
        SelectedParameters = new(StringComparer.OrdinalIgnoreCase);
        SelectedStates = new(StringComparer.OrdinalIgnoreCase);
        SelectedRegions = new(StringComparer.OrdinalIgnoreCase);
        SelectedInterval = 15;
        RestoreDefaultRange();
        FilterVersion++; ParameterFilterVersion++;
        ValidationMessage = "";
        ClearResults();
    }

    private void ClearResults()
    {
        ResultDescription = ""; HasApplied = false; ResultPlants = []; LoadedReadings = [];
        AppliedParameters = []; TimeColumns = []; ReadingIndex = []; ActiveParameter = "";
        AppliedLocationLabel = ""; AppliedScope = "Plant"; AppliedSourcePlantCount = 0;
        DisplayMode = "table"; HiddenGraphPlantIds.Clear(); LegendSearch = "";
    }

    private string IntervalLabel => Intervals.First(x => x.Value == (HasApplied ? AppliedInterval : SelectedInterval)).Label;
    private string ScopeColumnHeading => AppliedScope switch { "State" => "State", "Region" => "Region", _ => "LSS" };
    private string ResultRowLabel(LssPlant plant) => IsLocationAverage ? plant.Name : TemplatePlantCode(plant.PlantId);
    private static string TemplatePlantCode(string plantId)
    {
        var normalized = new string(plantId.Where(char.IsLetterOrDigit).ToArray());
        if (normalized.StartsWith("LSS", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized[3..], out var number)) return $"LSS{number}";
        return plantId;
    }
    private static string NormalizeCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private readonly record struct ReadingKey(string Parameter, int PlantId, DateTime Timestamp);
    private sealed record GraphPoint(double X, double Y, DateTime Timestamp, decimal Value);
}
