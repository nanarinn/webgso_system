using TestingSystem.Web.Models;
using TestingSystem.Web.Services;
using TestingSystem.Web.Components.Shared;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Components.Frontend.UseCases;
using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;

var repositoryRoot = FindRepositoryRoot();
var frontendRoot = Path.Combine(repositoryRoot, "TestingSystem.Web", "Components", "Frontend");
var forbiddenFrontendReferences = new[]
{
    "ApplicationDbContext",
    "IDbContextFactory<",
    "Microsoft.EntityFrameworkCore",
    "TestingSystem.Web.Data"
};
var frontendDatabaseReferences = Directory
    .EnumerateFiles(frontendRoot, "*.razor", SearchOption.AllDirectories)
    .Select(path => new { Path = path, Content = File.ReadAllText(path) })
    .Where(file => forbiddenFrontendReferences.Any(reference =>
        file.Content.Contains(reference, StringComparison.Ordinal)))
    .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
    .ToArray();
if (frontendDatabaseReferences.Length > 0)
    throw new Exception($"Frontend Razor files must call backend logic instead of SQL directly: {string.Join(", ", frontendDatabaseReferences)}");
Console.WriteLine("PASS: Frontend Razor components do not access SQL or EF Core directly");

var expectedMmfParameters = new[]
{
    "AirPressure", "AmbientTemp", "AtmPressure", "HorIrradiance", "Humidity", "IncIrradiance",
    "MWPerArea", "Precipitation", "PVTemperature", "TotRadiation", "WindDir", "WindDirSD", "WindSpeed"
};
if (!MmfParameterCatalog.Names.SequenceEqual(expectedMmfParameters)
    || MmfParameterCatalog.UnitFor("winddir") != "o"
    || MmfParameterCatalog.DisplayName("AMBIENTTEMP") != "AmbientTemp")
    throw new Exception("MMF parameter filter must follow the supplied API parameter names, order and units");
Console.WriteLine("PASS: MMF parameter catalog follows the supplied API payload");

LssPlant[] plants = [
    new() { Id = 1, Name = "Same name", State = "Perak" },
    new() { Id = 2, Name = "Same name", State = "Kedah" },
    new() { Id = 3, State = "Johor" },
    new() { Id = 4, State = "Johor", IsActive = false }
];
void Check(string name, HashSet<int> ids, string state, string region, params int[] expected)
{
    var actual = AnalyticsPlantFilter.Match(plants, ids, state, region).Select(p => p.Id);
    if (!actual.SequenceEqual(expected)) throw new Exception($"Failed: {name}");
    Console.WriteLine($"PASS: {name}");
}
Check("No selection includes active plants", [], "", "", 1, 2, 3);
Check("Multiple IDs and duplicate names", [1, 2], "", "", 1, 2);
Check("State intersects plant selection", [1, 2], "Perak", "", 1);
Check("Region intersects plant selection", [1, 3], "", "Northern", 1);
var multipleStates = AnalyticsPlantFilter.Match(plants, [],
    new HashSet<string>(["Perak", "Johor"], StringComparer.OrdinalIgnoreCase),
    new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Select(plant => plant.Id);
if (!multipleStates.SequenceEqual([1, 3])) throw new Exception("Multiple-state filtering failed");
var multipleRegions = AnalyticsPlantFilter.Match(plants, [],
    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    new HashSet<string>(["Northern", "Southern"], StringComparer.OrdinalIgnoreCase)).Select(plant => plant.Id);
if (!multipleRegions.SequenceEqual([1, 2, 3])) throw new Exception("Multiple-region filtering failed");
Console.WriteLine("PASS: Multiple state and region selections");
Check("Conflicting filters return no results", [1], "Johor", "");
Check("Inactive plants are excluded", [4], "", "");
Check("Unknown plant is excluded", [99], "", "");
if (AnalyticsPlantFilter.Match([], [], "").Any()) throw new Exception("Empty registry failed");
Console.WriteLine("PASS: Empty registry");
if (MmfSensorAveraging.AverageNonZero([20m, 22m, 0m, 24m, 0m]) != 22m)
    throw new Exception("MMF averaging must exclude zero placeholders");
if (MmfSensorAveraging.AverageNonZero([0m, 0m, 0m]) != 0m)
    throw new Exception("MMF averaging must keep an all-zero sensor interval as zero");
if (MmfSensorAveraging.AverageNonZero([1.1234567m, 2.1234567m]) != 1.623457m)
    throw new Exception("MMF averaging must round normalized values to six decimals");
Console.WriteLine("PASS: MMF multi-sensor averaging and zero-placeholder rules");
var locationTimestamp = new DateTime(2026, 9, 22, 12, 0, 0);
var locationReadings = new[]
{
    new SolarApiReading { Id = 1, PlantId = 10, DataType = "MMF", Parameter = "GHI", Unit = "W/m²", IntervalStart = locationTimestamp, Value = 400m },
    new SolarApiReading { Id = 2, PlantId = 10, DataType = "MMF", Parameter = "GHI", Unit = "W/m²", IntervalStart = locationTimestamp, Value = 500m },
    new SolarApiReading { Id = 3, PlantId = 20, DataType = "MMF", Parameter = "GHI", Unit = "W/m²", IntervalStart = locationTimestamp, Value = 700m }
};
var locationAverage = MmfLocationAveraging.AverageLatestByLocation(locationReadings, -1);
if (locationAverage.Count != 1 || locationAverage[0].PlantId != -1 || locationAverage[0].Value != 600m)
    throw new Exception("MMF state and region aggregation must average the latest reading from each plant");
Console.WriteLine("PASS: MMF state and region averages use the latest reading from each plant");
var separateLocationReadings = locationReadings.Append(new SolarApiReading
{
    Id = 4, PlantId = 30, DataType = "MMF", Parameter = "GHI", Unit = "W/m²",
    IntervalStart = locationTimestamp, Value = 900m
}).ToList();
var firstLocationAverage = MmfLocationAveraging.AverageLatestByLocation(
    separateLocationReadings.Where(reading => reading.PlantId is 10 or 20), -1);
var secondLocationAverage = MmfLocationAveraging.AverageLatestByLocation(
    separateLocationReadings.Where(reading => reading.PlantId == 30), -2);
if (firstLocationAverage.Single().Value != 600m || secondLocationAverage.Single().Value != 900m
    || firstLocationAverage.Single().PlantId != -1 || secondLocationAverage.Single().PlantId != -2)
    throw new Exception("Each selected MMF state or region must produce a separate average row");
Console.WriteLine("PASS: Multiple MMF states and regions retain separate average rows");
var workbookBytes = DdqExcelWorkbookExporter.Build([
    new DdqWorkbookSheet("AmbientTemp", "AMBIENT TEMP", [("Data Interval", "15 minutes")],
        ["LSS", "HR1", "HR2"],
        [new object?[] { "LSS1", 22m, 23m }], 1),
    new DdqWorkbookSheet("Anomaly Report", "ANOMALY REPORT", [], ["Parameter", "Reason"], [], 1)
]);
using var workbookStream = new MemoryStream(workbookBytes);
using var workbookArchive = new ZipArchive(workbookStream, ZipArchiveMode.Read);
if (workbookArchive.GetEntry("xl/worksheets/sheet1.xml") is null || workbookArchive.GetEntry("xl/worksheets/sheet2.xml") is null)
    throw new Exception("MMF Excel export must include parameter and anomaly worksheets");
using var worksheetReader = new StreamReader(workbookArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
var worksheetXml = worksheetReader.ReadToEnd();
if (!worksheetXml.Contains("xSplit=\"1\"") || !worksheetXml.Contains("topLeftCell=\"B7\"")
    || !worksheetXml.Contains("HR1") || !worksheetXml.Contains("HR2"))
    throw new Exception("MMF Excel export must freeze the scope column and use interval-based HR headings");
Console.WriteLine("PASS: MMF Excel workbook scope column and HR interval headings");
var mmfTestPayload = MmfTestApiPayload.Build("LSS1", new DateOnly(2026, 9, 24), 60);
if (mmfTestPayload.SensorReadings.Count != 24 * 13 * 5
    || mmfTestPayload.SensorReadings.Select(reading => reading.Parameter).Distinct().Count() != 13)
    throw new Exception("MMF test API must provide five sensors for all 13 sample parameters at every interval");
var translatedMmfTestPayload = ScheduledApiPayloadTranslator.Translate(JsonSerializer.Serialize(mmfTestPayload),
    "MMF", "MMF", 1, 120, new DateTime(2026, 9, 24, 23, 59, 0), 60, "application/json");
if (translatedMmfTestPayload.Readings.Count != 24 * 13
    || translatedMmfTestPayload.Readings.Any(reading => reading.IntervalMinutes != 60)
    || !translatedMmfTestPayload.Readings.Any(reading => reading.Parameter == "AmbientTemp"))
    throw new Exception("MMF test API response must translate into one averaged reading per parameter and interval");
Console.WriteLine("PASS: MMF sample API payload and scheduled collection translation");
var ddqFreezeBytes = DdqExcelWorkbookExporter.Build([
    new DdqWorkbookSheet("Day Ahead", "SOLAR DATA SUBMISSION", [],
        ["Submission Date & Time", "Year", "Month", "Day", "Period", "LSS1"],
        [new object?[] { "24/09/2026", 2026, 9, 24, 1, 7.4m }], 5)
]);
using var ddqFreezeStream = new MemoryStream(ddqFreezeBytes);
using var ddqFreezeArchive = new ZipArchive(ddqFreezeStream, ZipArchiveMode.Read);
using var ddqFreezeReader = new StreamReader(ddqFreezeArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
var ddqFreezeXml = ddqFreezeReader.ReadToEnd();
if (!ddqFreezeXml.Contains("xSplit=\"5\"") || !ddqFreezeXml.Contains("topLeftCell=\"F7\""))
    throw new Exception("DDQ Excel export must freeze Submission Date, Year, Month, Day and Period");
Console.WriteLine("PASS: DDQ Excel export freezes the five reference columns");
if (AnalyticsGraphScale.NiceUpperBound(0.996m) != 1m || AnalyticsGraphScale.TickFormat(1m) != "N2")
    throw new Exception("MMF atmospheric pressure scale failed");
if (AnalyticsGraphScale.NiceUpperBound(1010m) != 1200m || AnalyticsGraphScale.NiceUpperBound(35m) != 50m)
    throw new Exception("MMF large-value graph scale failed");
if (AnalyticsGraphScale.NiceUpperBound(0.00012m) != 0.00012m || AnalyticsGraphScale.TickFormat(0.00012m) != "N6")
    throw new Exception("MMF small-value graph scale failed");
Console.WriteLine("PASS: MMF unit-aware graph scale for pressure, temperature and small values");
var atmosphericBounds = AnalyticsGraphScale.Bounds([0.994m, 0.996m, 0.998m]);
if (atmosphericBounds.Minimum >= 0.994m || atmosphericBounds.Maximum <= 0.998m
    || atmosphericBounds.Minimum == 0)
    throw new Exception("MMF graph bounds must zoom around atmospheric pressure values");
var irradianceBounds = AnalyticsGraphScale.Bounds([0m, 450m, 930m]);
if (irradianceBounds.Minimum != 0 || irradianceBounds.Maximum < 930m)
    throw new Exception("MMF graph bounds must keep a zero baseline for irradiance");
Console.WriteLine("PASS: MMF dynamic graph bounds for atmospheric and zero-based parameters");
if (CollectionSchedulePolicy.ApiDataType("DDQ", "Daily") != "Day Ahead"
    || CollectionSchedulePolicy.ApiDataType("DDQ", "Weekly") != "Week Ahead"
    || CollectionSchedulePolicy.ApiDataType("DDQ", "4 Month") != "4 Month Ahead"
    || CollectionSchedulePolicy.ApiDataType("MMF", "MMF") != "MMF")
    throw new Exception("Collection schedule API data-type mapping failed");
var scheduleNow = new DateTime(2026, 9, 23, 10, 30, 0);
if (!CollectionSchedulePolicy.IsDue("Rolling 24 Hour", "15 minutes", "10:00", "", "", scheduleNow, null)
    || CollectionSchedulePolicy.IsDue("Rolling 24 Hour", "15 minutes", "11:00", "", "", scheduleNow, null)
    || CollectionSchedulePolicy.IsDue("Rolling 24 Hour", "15 minutes", "10:00", "", "", scheduleNow,
        new DateTime(2026, 9, 23, 10, 0, 0)))
    throw new Exception("Rolling 24 Hour daily schedule timing failed");
if (!CollectionSchedulePolicy.IsDue("Daily", "15 minutes", "10:00", "", "", scheduleNow, null)
    || CollectionSchedulePolicy.IsDue("Daily", "15 minutes", "11:00", "", "", scheduleNow, null))
    throw new Exception("Daily collection schedule timing failed");
if (!CollectionSchedulePolicy.IsDue("Daily", "15 minutes", "10:00", "", "", scheduleNow,
        new DateTime(2026, 9, 23, 9, 55, 0))
    || CollectionSchedulePolicy.IsDue("Daily", "15 minutes", "10:00", "", "", scheduleNow,
        new DateTime(2026, 9, 23, 10, 0, 0)))
    throw new Exception("An edited schedule must rerun after its new time, but only once for that time slot");
if (!CollectionSchedulePolicy.IsDue("4 Month", "15 minutes", "10:00", "23", "1,5,9", scheduleNow, null))
    throw new Exception("Four-month collection schedule timing failed");
if (CollectionSchedulePolicy.SubmissionDateTime("Daily", "10:00", scheduleNow)
        != new DateTime(2026, 9, 23, 10, 0, 0)
    || CollectionSchedulePolicy.SubmissionDateTime("Rolling 24 Hour", "10:00",
        scheduleNow.AddSeconds(37)) != new DateTime(2026, 9, 23, 10, 0, 0))
    throw new Exception("Collection schedule must produce the authoritative submission date and minute.");
Console.WriteLine("PASS: Collection schedule timing and API endpoint mapping");
var referenceSubmission = new DateTime(2026, 9, 19, 9, 0, 0);
var dayAheadWindow = DdqDatasetPolicy.ReferenceWindow("Day Ahead", referenceSubmission);
var weekAheadWindow = DdqDatasetPolicy.ReferenceWindow("Week Ahead", referenceSubmission);
var fourMonthWindow = DdqDatasetPolicy.ReferenceWindow("4 Month Ahead", referenceSubmission);
if (DdqDatasetPolicy.Get("Day Ahead").ReferenceTemplateIntervalMinutes != 30
    || dayAheadWindow.Start != new DateTime(2026, 9, 19) || dayAheadWindow.EndExclusive != new DateTime(2026, 9, 24))
    throw new Exception("Day Ahead must represent all five forecast days at 30-minute intervals");
if (DdqDatasetPolicy.Get("Week Ahead").ReferenceTemplateIntervalMinutes != 60
    || weekAheadWindow.Start != new DateTime(2026, 9, 19) || weekAheadWindow.EndExclusive != new DateTime(2026, 9, 28))
    throw new Exception("Week Ahead reference horizon failed");
if (DdqDatasetPolicy.Get("4 Month Ahead").ReferenceTemplateIntervalMinutes != 60
    || fourMonthWindow.Start != new DateTime(2026, 9, 1) || fourMonthWindow.EndExclusive != new DateTime(2027, 1, 1))
    throw new Exception("Four Month Ahead must represent four complete calendar months");
Console.WriteLine("PASS: DDQ Day Ahead, Week Ahead and Four Month Ahead reference horizons");
var translatedDdq = ScheduledApiPayloadTranslator.Translate(
    """{"submissionDateTime":"2026-09-23T10:00:00+08:00","forecastData":[{"startTime":"2026-09-24T00:00:00+08:00","endTime":"2026-09-24T00:15:00+08:00","forecastValue":12.5,"valueUnit":"MW"}]}""",
    "DDQ", "Day Ahead", 1, 100, scheduleNow, 15);
if (translatedDdq.Readings.Count != 1 || translatedDdq.Readings[0].Value != 12.5m
    || translatedDdq.Readings[0].Parameter != "forecastValue"
    || translatedDdq.Readings[0].SubmissionDateTime != scheduleNow)
    throw new Exception("Scheduled DDQ payload translation failed");
if (translatedDdq.Issues.Any(issue => issue.RuleCode == "DDQ-009"))
    throw new Exception("A response must not be rejected merely because its API-defined horizon differs from a template");
var translatedMmf = ScheduledApiPayloadTranslator.Translate(
    """{"sensorReadings":[{"timestamp":"2026-09-23T10:00:00+08:00","parameter":"GHI","value":400,"unit":"W/m²"},{"timestamp":"2026-09-23T10:00:00+08:00","parameter":"GHI","value":500,"unit":"W/m²"}]}""",
    "MMF", "MMF", 1, 101, scheduleNow, 15);
if (translatedMmf.Readings.Count != 1 || translatedMmf.Readings[0].Value != 450m
    || translatedMmf.Readings[0].Parameter != "GHI")
    throw new Exception("Scheduled MMF sensor averaging failed");
var translatedNamedMmf = ScheduledApiPayloadTranslator.Translate(
    """{"MMFResponse":{"PlantName":"LSS-026","MMFData":[{"Parameter":"IncIrradiance","Time":"2026-09-24T12:00:00","Unit":"W/m2","MMF1":"100","MMF2":"200"},{"Parameter":"AirPressure","Time":"2026-09-24T12:00:00","Unit":"hPa","MMF1":"1006.157"}]}}""",
    "MMF", "MMF", 1, 121, scheduleNow, 30, "application/json");
if (translatedNamedMmf.Readings.Count != 2
    || translatedNamedMmf.Readings.Any(reading => reading.Parameter == "MMF1" || reading.Parameter == "MMF2")
    || translatedNamedMmf.Readings.Single(reading => reading.Parameter == "IncIrradiance").Value != 150m
    || translatedNamedMmf.Readings.Single(reading => reading.Parameter == "AirPressure").Value != 1006.157m)
    throw new Exception("Named MMF payloads must retain Parameter and average MMF1..MMFn sensor values");
Console.WriteLine("PASS: Named MMF payload retains parameter names instead of sensor field names");
var translatedXml = ScheduledApiPayloadTranslator.Translate(
    """<response><reading><timestamp>2026-09-23T10:00:00+08:00</timestamp><parameter>GHI</parameter><value>725.5</value><unit>W/m2</unit></reading></response>""",
    "MMF", "MMF", 1, 102, scheduleNow, 15, "application/xml");
if (translatedXml.PayloadFormat != "XML" || translatedXml.Readings.Count != 1 || translatedXml.Readings[0].Value != 725.5m)
    throw new Exception("Scheduled XML payload translation failed");
var forecastXml = ScheduledApiPayloadTranslator.Translate(
    """<PlantForecastIntervals><PlantForecastIntervalNode><IntervalStartTime>2026-09-24T00:00:00</IntervalStartTime><IntervalEndTime>2026-09-24T00:15:00</IntervalEndTime><IntervalLength>15</IntervalLength><ForecastResultParameter>DayAhead</ForecastResultParameter><ForecastValue>7.4</ForecastValue><ValueUnit>MW</ValueUnit></PlantForecastIntervalNode><PlantForecastIntervalNode><IntervalStartTime>2026-09-24T00:15:00</IntervalStartTime><IntervalEndTime>2026-09-24T00:30:00</IntervalEndTime><IntervalLength>15</IntervalLength><ForecastResultParameter>DayAhead</ForecastResultParameter><ForecastValue>7.8</ForecastValue><ValueUnit>MW</ValueUnit></PlantForecastIntervalNode></PlantForecastIntervals>""",
    "DDQ", "Day Ahead", 1, 115, referenceSubmission, 30, "application/xml");
if (forecastXml.Readings.Count != 2 || forecastXml.Readings.Any(reading => reading.IntervalMinutes != 15)
    || forecastXml.Readings[0].Parameter != "DayAhead" || forecastXml.Readings[1].Value != 7.8m)
    throw new Exception("The LSS forecast XML contract must retain every API timestamp and detect its 15-minute interval");
var translatedSoapDayAhead = ScheduledApiPayloadTranslator.Translate(
    """<s:Envelope xmlns:s="urn:soap"><s:Body><DayAheadResponse><PlantForecastIntervals><PlantForecastIntervalNode><IntervalStartTime>2026-08-30T00:00:00</IntervalStartTime><IntervalEndTime>2026-08-30T00:15:00</IntervalEndTime><IntervalLength>15</IntervalLength><Forecast><PlantForecastNode><DataErrorCode>0</DataErrorCode><DataErrorMessage>OK</DataErrorMessage><ForecastResultParameter>DayAhead</ForecastResultParameter><ForecastValue>7.25</ForecastValue><ValueUnit>MW</ValueUnit></PlantForecastNode></Forecast></PlantForecastIntervalNode></PlantForecastIntervals></DayAheadResponse></s:Body></s:Envelope>""",
    "DDQ", "Day Ahead", 1, 105, scheduleNow, 15, "application/soap+xml");
if (translatedSoapDayAhead.Readings.Count != 1
    || translatedSoapDayAhead.Readings[0].IntervalStart != new DateTime(2026, 8, 30, 0, 0, 0)
    || translatedSoapDayAhead.Readings[0].IntervalEnd != new DateTime(2026, 8, 30, 0, 15, 0)
    || translatedSoapDayAhead.Readings[0].Parameter != "DayAhead"
    || translatedSoapDayAhead.Readings[0].Value != 7.25m
    || translatedSoapDayAhead.Readings[0].Unit != "MW")
    throw new Exception("Nested SOAP Day Ahead payload translation failed");
var translatedFlatDayAhead = ScheduledApiPayloadTranslator.Translate(
    """<PlantForecastIntervals><PlantForecastIntervalNode><IntervalStartTime>2026-08-31T00:00:00</IntervalStartTime><IntervalEndTime>2026-08-31T00:15:00</IntervalEndTime><IntervalLength>15</IntervalLength><ForecastResultParameter>DayAhead</ForecastResultParameter><ForecastValue>8.5</ForecastValue><ValueUnit>MW</ValueUnit></PlantForecastIntervalNode></PlantForecastIntervals>""",
    "DDQ", "Day Ahead", 1, 106, scheduleNow, 15, "application/xml");
if (translatedFlatDayAhead.Readings.Count != 1
    || translatedFlatDayAhead.Readings[0].IntervalStart != new DateTime(2026, 8, 31, 0, 0, 0)
    || translatedFlatDayAhead.Readings[0].Parameter != "DayAhead"
    || translatedFlatDayAhead.Readings[0].Value != 8.5m)
    throw new Exception("Flat XML Day Ahead payload translation failed");
var translatedCsv = ScheduledApiPayloadTranslator.Translate(
    "timestamp,forecastValue,valueUnit\n2026-09-23T10:00:00+08:00,18.75,MW",
    "DDQ", "Day Ahead", 1, 103, scheduleNow, 15, "text/csv");
if (translatedCsv.PayloadFormat != "CSV" || translatedCsv.Readings.Count != 1 || translatedCsv.Readings[0].Value != 18.75m)
    throw new Exception("Scheduled CSV payload translation failed");
var translatedDayAheadMatrix = ScheduledApiPayloadTranslator.Translate(
    "Year,Month,Day,Period,LSS1\n2026,9,19,1,0\n2026,9,19,2,1.25\n2026,9,23,48,2.5",
    "DDQ", "Day Ahead", 1, 106, referenceSubmission, 30, "text/csv");
if (translatedDayAheadMatrix.Readings.Count != 3
    || translatedDayAheadMatrix.Readings[0].IntervalStart != new DateTime(2026, 9, 19, 0, 0, 0)
    || translatedDayAheadMatrix.Readings[1].IntervalStart != new DateTime(2026, 9, 19, 0, 30, 0)
    || translatedDayAheadMatrix.Readings[2].IntervalStart != new DateTime(2026, 9, 23, 23, 30, 0)
    || translatedDayAheadMatrix.Readings.Any(reading => reading.IntervalMinutes != 30))
    throw new Exception("Day Ahead matrix translation must preserve all five-day periods and source interval");
var detectedHourly = ScheduledApiPayloadTranslator.Translate(
    "timestamp,forecastValue\n2026-09-19T00:00:00+08:00,1\n2026-09-19T01:00:00+08:00,2",
    "DDQ", "Week Ahead", 1, 107, referenceSubmission, 15, "text/csv");
if (detectedHourly.Readings.Count != 2 || detectedHourly.Readings.Any(reading => reading.IntervalMinutes != 60))
    throw new Exception("API translation must detect and preserve the actual source interval");
var plantColumnJson = ScheduledApiPayloadTranslator.Translate(
    """{"submissionDateTime":"2026-09-24T10:00:00+08:00","data":[{"Data Timestamp":"24/09/2026 00:00","LSS-001":7.4},{"Data Timestamp":"24/09/2026 00:30","LSS-001":7.8}]}""",
    "DDQ", "Day Ahead", 1, 111, referenceSubmission, 30, "application/json");
if (plantColumnJson.Readings.Count != 2 || plantColumnJson.Readings.Any(reading => reading.Parameter != "LSS-001")
    || plantColumnJson.Readings[^1].IntervalStart != new DateTime(2026, 9, 24, 0, 30, 0))
    throw new Exception("JSON rows with a plant-code measurement column must retain every timestamp");
var columnarJson = ScheduledApiPayloadTranslator.Translate(
    """{"Submission Date & Time":["24/09/2026 10:00","24/09/2026 10:00"],"Data Timestamp":["24/09/2026 00:00","24/09/2026 00:30"],"Year":[2026,2026],"Month":[9,9],"Day":[24,24],"Period":[1,2],"LSS-001":[7.4,7.8]}""",
    "DDQ", "Day Ahead", 1, 112, referenceSubmission, 30, "application/json");
if (columnarJson.Readings.Count != 2 || columnarJson.Readings[0].Value != 7.4m
    || columnarJson.Readings[1].Value != 7.8m)
    throw new Exception("Column-oriented JSON API data must be expanded into individual readings");
var matrixJson = ScheduledApiPayloadTranslator.Translate(
    """{"columns":["Submission Date & Time","Data Timestamp","Year","Month","Day","Period","LSS-001"],"data":[["24/09/2026 10:00","24/09/2026 00:00",2026,9,24,1,7.4],["24/09/2026 10:00","24/09/2026 00:30",2026,9,24,2,7.8]]}""",
    "DDQ", "Day Ahead", 1, 114, referenceSubmission, 30, "application/json");
if (matrixJson.Readings.Count != 2 || matrixJson.Readings[0].IntervalStart != new DateTime(2026, 9, 24, 0, 0, 0)
    || matrixJson.Readings[1].IntervalStart != new DateTime(2026, 9, 24, 0, 30, 0))
    throw new Exception("Matrix-oriented JSON API data must be expanded into individual readings");
var timestampKeyedJson = ScheduledApiPayloadTranslator.Translate(
    """{"values":{"2026-09-24T00:00:00+08:00":7.4,"2026-09-24T00:30:00+08:00":7.8}}""",
    "DDQ", "Day Ahead", 1, 113, referenceSubmission, 30, "application/json");
if (timestampKeyedJson.Readings.Count != 2 || timestampKeyedJson.Readings.Any(reading => reading.Parameter != "values"))
    throw new Exception("Timestamp-keyed JSON values must retain every reading");
Console.WriteLine("PASS: DDQ matrix payload and source interval detection");
var fiveDayCsv = new System.Text.StringBuilder("Year,Month,Day,Period,LSS1\n");
for (var dayOffset = 0; dayOffset < 5; dayOffset++)
    for (var period = 1; period <= 48; period++)
    {
        var date = new DateTime(2026, 9, 19).AddDays(dayOffset);
        fiveDayCsv.AppendLine($"{date.Year},{date.Month},{date.Day},{period},{dayOffset * 48 + period}");
    }
var completeFiveDay = ScheduledApiPayloadTranslator.Translate(fiveDayCsv.ToString(),
    "DDQ", "Day Ahead", 1, 108, referenceSubmission, 30, "text/csv");
if (completeFiveDay.Readings.Count != 240
    || completeFiveDay.Readings[^1].IntervalStart != new DateTime(2026, 9, 23, 23, 30, 0)
    || completeFiveDay.Issues.Any(issue => issue.RuleCode == "DDQ-009"))
    throw new Exception("The complete five-day API payload must keep all 240 half-hour rows");
var weekAheadCsv = new System.Text.StringBuilder("Year,Month,Day,Period,LSS1\n");
for (var dayOffset = 0; dayOffset < 9; dayOffset++)
    for (var period = 1; period <= 24; period++)
    {
        var date = new DateTime(2026, 9, 19).AddDays(dayOffset);
        weekAheadCsv.AppendLine($"{date.Year},{date.Month},{date.Day},{period},{period}");
    }
var completeWeekAhead = ScheduledApiPayloadTranslator.Translate(weekAheadCsv.ToString(),
    "DDQ", "Week Ahead", 1, 110, referenceSubmission, 60, "text/csv");
if (completeWeekAhead.Readings.Count != 216
    || completeWeekAhead.Readings[^1].IntervalStart != new DateTime(2026, 9, 27, 23, 0, 0)
    || completeWeekAhead.Issues.Any(issue => issue.RuleCode == "DDQ-009"))
    throw new Exception("The complete supplied Week Ahead API payload must keep all 216 hourly rows");
var fourMonthCsv = new System.Text.StringBuilder("Year,Month,Day,Period,LSS1\n");
for (var date = new DateTime(2026, 9, 1); date < new DateTime(2027, 1, 1); date = date.AddDays(1))
    for (var period = 1; period <= 24; period++)
        fourMonthCsv.AppendLine($"{date.Year},{date.Month},{date.Day},{period},{period}");
var completeFourMonth = ScheduledApiPayloadTranslator.Translate(fourMonthCsv.ToString(),
    "DDQ", "4 Month Ahead", 1, 109, referenceSubmission, 60, "text/csv");
if (completeFourMonth.Readings.Count != 2928
    || completeFourMonth.Readings[^1].IntervalStart != new DateTime(2026, 12, 31, 23, 0, 0)
    || completeFourMonth.Issues.Any(issue => issue.RuleCode == "DDQ-009"))
    throw new Exception("The complete four-month API payload must keep all 2,928 hourly rows");
Console.WriteLine("PASS: Full Day Ahead, Week Ahead and Four Month Ahead payloads are retained");
var translatedWithoutTimestamp = ScheduledApiPayloadTranslator.Translate(
    """{"forecastValue":9.25,"valueUnit":"MW"}""",
    "DDQ", "Rolling 24 Hour", 1, 105, new DateTime(2026, 9, 23, 10, 32, 47), 15, "application/json");
if (translatedWithoutTimestamp.Readings.Count != 1
    || translatedWithoutTimestamp.Readings[0].IntervalStart != new DateTime(2026, 9, 23, 10, 30, 0))
    throw new Exception("Timestamp-free API readings must align with the selected filter interval");
try
{
    ScheduledApiPayloadTranslator.Translate("<html><body>Sign in</body></html>", "DDQ", "Day Ahead", 1, 104, scheduleNow, 15, "text/html");
    throw new Exception("HTML API response should not be accepted as measurement data");
}
catch (FormatException exception) when (exception.Message.Contains("HTML page")) { }
Console.WriteLine("PASS: Scheduled JSON, XML and CSV payload translation with clear HTML rejection");
var paginationItems = Enumerable.Range(1, 250).ToList();
var paginationPageOne = TablePaging.Slice(paginationItems, 1);
var paginationPageTwo = TablePaging.Slice(paginationItems, 2);
var paginationLastPage = TablePaging.Slice(paginationItems, 13);
if (paginationPageOne.Count != 20 || paginationPageOne[0] != 1 || paginationPageOne[^1] != 20
    || paginationPageTwo.Count != 20 || paginationPageTwo[0] != 21 || paginationPageTwo[^1] != 40
    || paginationLastPage.Count != 10 || paginationLastPage[0] != 241 || paginationLastPage[^1] != 250)
    throw new Exception("Shared table pagination must return 20 records per page and preserve row order");
Console.WriteLine("PASS: Shared table pagination returns 20 records per page");
if (ManualDdqCollectionPolicy.IsSupported("Rolling 24 Hour")
    || !ManualDdqCollectionPolicy.IsSupported("Day Ahead")
    || !ManualDdqCollectionPolicy.IsSupported("Week Ahead")
    || !ManualDdqCollectionPolicy.IsSupported("4 Month Ahead"))
    throw new Exception("Manual DDQ must exclude Rolling 24 Hour and allow the three forward forecast data types");
if (ManualDdqCollectionPolicy.Evaluate(0, ["Running"]) != ManualCollectionExistingState.ScheduledRunning
    || ManualDdqCollectionPolicy.Evaluate(0, ["Queued"]) != ManualCollectionExistingState.ScheduledRunning
    || ManualDdqCollectionPolicy.Evaluate(0, ["Processing"]) != ManualCollectionExistingState.ScheduledRunning
    || ManualDdqCollectionPolicy.Evaluate(0, ["Failed"]) != ManualCollectionExistingState.ScheduledFailed
    || ManualDdqCollectionPolicy.Evaluate(48, []) != ManualCollectionExistingState.ExistingData
    || ManualDdqCollectionPolicy.Evaluate(0, []) != ManualCollectionExistingState.None)
    throw new Exception("Manual DDQ replacement checks must distinguish running, failed, existing and new datasets");
Console.WriteLine("PASS: Manual DDQ type restrictions and replacement confirmation states");
var generatedCommandId = CollectionCommandIdentity.Create("DDQ", new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
var requestedCommandId = CollectionCommandIdentity.Create("DDQ", DateTimeOffset.UtcNow, new string('X', 80));
if (generatedCommandId != "CMD-DDQ-20260930120000000" || requestedCommandId.Length != 60)
    throw new Exception("Manual collection IDs must use CMD-API-yyyyMMddHHmmssfff and remain within the SQL column limit");
Console.WriteLine("PASS: Manual collection command IDs use the compact timestamp format");
if (CollectionProcessingPolicy.NormalizeConcurrency(0) != 1
    || CollectionProcessingPolicy.NormalizeConcurrency(6) != 6
    || CollectionProcessingPolicy.NormalizeConcurrency(20) != 8)
    throw new Exception("Collection processing concurrency must remain bounded between one and eight workers.");
Console.WriteLine("PASS: Collection processing uses bounded parallel workers");
if (ManualBatchCollectionPolicy.StoredDataType("MMF", "All MMF parameters") != "MMF"
    || ManualBatchCollectionPolicy.StoredDataType("DDQ", " Rolling 24 Hour ") != "Rolling 24 Hour"
    || ManualBatchCollectionPolicy.Mode("MMF") != "Manual MMF"
    || ManualBatchCollectionPolicy.Mode("DDQ") != "Manual DDQ")
    throw new Exception("Run-all manual collection must map every DDQ and MMF endpoint to its stored dataset");
Console.WriteLine("PASS: Run-all manual collection maps DDQ and MMF endpoint jobs correctly");
var completePlantApiConfiguration = LssPlantApiConfigurationPolicy.Validate(
    [new PlantApiEndpointInput("Rolling 24 Hour", "https://api.example.com/ddq")],
    "https://api.example.com/mmf");
if (completePlantApiConfiguration is not null
    || LssPlantApiConfigurationPolicy.Validate([], "https://api.example.com/mmf") is null
    || LssPlantApiConfigurationPolicy.Validate(
        [new PlantApiEndpointInput("Rolling 24 Hour", "https://api.example.com/ddq")], string.Empty) is null)
    throw new Exception("LSS plant configuration must require both DDQ and MMF endpoints");
Console.WriteLine("PASS: LSS plant configuration requires DDQ and MMF endpoints together");
var verifiedMmfApi = LssApiPayloadVerifier.Verify(
    """{"timestamp":"2026-09-28T00:00:00","parameter":"GHI","value":725.5,"unit":"W/m2"}""",
    "application/json", "MMF", "MMF", 15);
var rejectedWebPage = LssApiPayloadVerifier.Verify(
    "<html><body><h1>Administration</h1></body></html>",
    "text/html", "DDQ", "Rolling 24 Hour", 15);
if (!verifiedMmfApi.IsValid || verifiedMmfApi.ReadingCount != 1 || rejectedWebPage.IsValid)
    throw new Exception("API verification must accept readable measurement payloads and reject ordinary web pages");
Console.WriteLine("PASS: Plant API verification rejects HTTP 200 web pages without DDQ or MMF data");
if (!ReportScheduleTimePolicy.TryNormalize("10:53 AM", out var morningScheduleTime)
    || morningScheduleTime != "10:53"
    || !ReportScheduleTimePolicy.TryNormalize("10:53 PM", out var eveningScheduleTime)
    || eveningScheduleTime != "22:53"
    || !ReportScheduleTimePolicy.TryNormalize("07:05", out var twentyFourHourScheduleTime)
    || twentyFourHourScheduleTime != "07:05"
    || ReportScheduleTimePolicy.TryNormalize("not-a-time", out _))
    throw new Exception("Report schedule times must be normalized to the five-character 24-hour MS SQL format");
Console.WriteLine("PASS: Report schedule time accepts browser AM/PM values and stores HH:mm");
var rollingReportSlot = ReportScheduleDuePolicy.GetDueSlot("Rolling 24 Hour", 15, "07:00", "", "",
    new DateTime(2026, 9, 28, 10, 53, 22));
var dailyReportSlot = ReportScheduleDuePolicy.GetDueSlot("Daily", 15, "10:53", "", "",
    new DateTime(2026, 9, 28, 10, 54, 0));
var earlyDailySlot = ReportScheduleDuePolicy.GetDueSlot("Daily", 15, "10:53", "", "",
    new DateTime(2026, 9, 28, 10, 52, 59));
var wrongWeeklyDay = ReportScheduleDuePolicy.GetDueSlot("Weekly", 15, "10:00", "Tuesday", "",
    new DateTime(2026, 9, 28, 11, 0, 0));
var selectedFourMonthSlot = ReportScheduleDuePolicy.GetDueSlot("4 Month", 15, "10:00", "28", "1,5,9",
    new DateTime(2026, 9, 28, 11, 0, 0));
if (rollingReportSlot != "202609281045" || dailyReportSlot != "202609281053" || earlyDailySlot is not null
    || wrongWeeklyDay is not null || selectedFourMonthSlot != "202609281000")
    throw new Exception("Report schedules must calculate one deterministic Malaysia-time delivery slot");
Console.WriteLine("PASS: Scheduled report due slots support rolling, daily, weekly and selected-month delivery");
var firstReportUser = new ClaimsPrincipal(new ClaimsIdentity(
    [new Claim(ClaimTypes.NameIdentifier, "user-one"), new Claim(ClaimTypes.Email, "one@example.com")], "test"));
var secondReportUser = new ClaimsPrincipal(new ClaimsIdentity(
    [new Claim(ClaimTypes.NameIdentifier, "user-two"), new Claim(ClaimTypes.Email, "two@example.com")], "test"));
var firstRequesterKey = OnDemandReportOwnership.GetRequesterKey(firstReportUser);
var secondRequesterKey = OnDemandReportOwnership.GetRequesterKey(secondReportUser);
if (firstRequesterKey == secondRequesterKey
    || !OnDemandReportOwnership.IsOwner(firstRequesterKey, firstRequesterKey)
    || OnDemandReportOwnership.IsOwner(firstRequesterKey, secondRequesterKey)
    || OnDemandReportOwnership.IsOwner("Current user", firstRequesterKey))
    throw new Exception("On-demand report ownership must isolate each signed-in user and reject legacy unowned records");
Console.WriteLine("PASS: On-demand analysis records are isolated by signed-in user");
var reportReferenceOne = OnDemandReportReference.Create("DDQ", new DateTime(2026, 9, 24, 15, 0, 0));
var reportReferenceTwo = OnDemandReportReference.Create("DDQ", new DateTime(2026, 9, 24, 15, 0, 0));
if (reportReferenceOne == reportReferenceTwo || reportReferenceOne.Length > 60 || reportReferenceTwo.Length > 60)
    throw new Exception("On-demand reports must use unique database-safe references");
Console.WriteLine("PASS: On-demand report references remain unique after data resets");
var exportPlants = new List<LssPlant>
{
    new() { Id = 10, PlantId = "LSS-001", Name = "Plant One", State = "Selangor" },
    new() { Id = 20, PlantId = "LSS-002", Name = "Plant Two", State = "Johor" }
};
var exportTimestamp = new DateTime(2026, 9, 24, 0, 30, 0);
var exportSubmission = new DateTime(2026, 9, 24, 10, 40, 0);
var ddqOnDemandCsv = OnDemandCsvExporter.BuildDdq("Day Ahead", exportSubmission, exportPlants,
[
    new SolarApiReading { Id = 1, PlantId = 10, DataType = "Day Ahead", IntervalStart = exportTimestamp, Value = 7.4m },
    new SolarApiReading { Id = 2, PlantId = 20, DataType = "Day Ahead", IntervalStart = exportTimestamp, Value = 8.6m }
]);
if (!ddqOnDemandCsv.Contains("\"Submission Date & Time\",\"Year\",\"Month\",\"Day\",\"Period\",\"LSS1\",\"LSS2\"")
    || !ddqOnDemandCsv.Contains("\"24/09/2026 10:40:00\",\"2026\",\"9\",\"24\",\"2\",\"7.4\",\"8.6\""))
    throw new Exception("On-demand DDQ CSV must use the DDQ Analytics pivot layout");
var mmfOnDemandCsv = OnDemandCsvExporter.BuildMmf(exportSubmission, exportPlants,
[
    new SolarApiReading { Id = 3, PlantId = 10, DataType = "MMF", Parameter = "AmbientTemp", Unit = "C", IntervalStart = exportTimestamp, IntervalMinutes = 30, Value = 24.2m },
    new SolarApiReading { Id = 4, PlantId = 20, DataType = "MMF", Parameter = "AmbientTemp", Unit = "C", IntervalStart = exportTimestamp, IntervalMinutes = 30, Value = 25.1m }
], exportTimestamp, exportTimestamp.AddHours(1));
if (!mmfOnDemandCsv.Contains("\"LSS\",\"HR1\"")
    || !mmfOnDemandCsv.Contains("\"Data Interval\",\"30 minutes\"")
    || !mmfOnDemandCsv.Contains("\"Data Timestamp Range\",\"24/09/2026 00:30:00 - 24/09/2026 01:30:00\"")
    || mmfOnDemandCsv.Contains("Submission Date & Time")
    || !mmfOnDemandCsv.Contains("\"LSS1\",\"24.2\""))
    throw new Exception("On-demand MMF CSV must use the MMF Analytics HR layout and data timestamp range without submission date and time.");
Console.WriteLine("PASS: On-demand DDQ and MMF CSV exports match the analytics layouts");

var intervalFilteredCsv = OnDemandCsvExporter.BuildDdq("Day Ahead", exportSubmission, [exportPlants[0]],
[
    new SolarApiReading { Id = 5, PlantId = 10, DataType = "Day Ahead", IntervalStart = exportTimestamp, Value = 7.4m },
    new SolarApiReading { Id = 6, PlantId = 10, DataType = "Day Ahead", IntervalStart = exportTimestamp.AddMinutes(30), Value = 8.1m }
], 60);
if (!intervalFilteredCsv.Contains("\"Data Interval\",\"1 hour\"")
    || intervalFilteredCsv.Contains("\"7.4\"") || !intervalFilteredCsv.Contains("\"8.1\""))
    throw new Exception("On-demand DDQ export must apply the user-selected data interval.");
Console.WriteLine("PASS: On-demand DDQ export applies the selected data interval");

var configuredQualityRule = new ValidationRule
{
    Code = "USR-MMF-0001", DataType = "MMF", FieldName = "GHI", RuleSource = "Configured",
    IsActive = true, MinimumValue = 0m, MaximumValue = 100m, ChangeThreshold = 20m, CheckMissing = true
};
var qualityReadings = new List<SolarApiReading>
{
    new() { Parameter = "GHI", Value = -1m, IntervalStart = new DateTime(2026, 9, 24, 0, 0, 0), IntervalMinutes = 15 },
    new() { Parameter = "GHI", Value = 50m, IntervalStart = new DateTime(2026, 9, 24, 0, 15, 0), IntervalMinutes = 15 },
    new() { Parameter = "GHI", Value = 150m, IntervalStart = new DateTime(2026, 9, 24, 0, 45, 0), IntervalMinutes = 15 }
};
var qualityIssues = ConfiguredValidationRuleEvaluator.Evaluate([configuredQualityRule], qualityReadings, "MMF");
if (!qualityIssues.Any(issue => issue.Reason.Contains("minimum"))
    || !qualityIssues.Any(issue => issue.Reason.Contains("maximum"))
    || qualityIssues.Count(issue => issue.Reason.Contains("hysteresis")) != 2
    || !qualityIssues.Any(issue => issue.Reason.Contains("Missing value")))
    throw new Exception("Configured quality rules must enforce minimum, maximum, hysteresis and missing intervals.");
var inactiveQualityIssues = ConfiguredValidationRuleEvaluator.Evaluate(
    [new ValidationRule { Code = "USR-MMF-0002", DataType = "MMF", FieldName = "*",
        RuleSource = "Configured", IsActive = false, MaximumValue = 1m }], qualityReadings, "MMF");
if (inactiveQualityIssues.Count != 0)
    throw new Exception("Inactive configured quality rules must not produce anomalies.");
Console.WriteLine("PASS: Configured data-quality rules enforce all checks and respect active status");

var mmfDefaultQuality = MmfSensorQualityPolicy.Evaluate("GHI",
    new DateTime(2026, 9, 24, 12, 0, 0), [700m, 710m, 2500m],
    [new ValidationRule { Code = "MMF-005", DataType = "MMF", IsActive = true, RuleSource = "Fixed" }]);
if (mmfDefaultQuality.ValidValues.Count != 3 || mmfDefaultQuality.Issues.Count != 0)
    throw new Exception("MMF must not apply a preconfigured sensor range without an administrator-created parameter rule.");
var configuredMmfRange = MmfSensorQualityPolicy.Evaluate("GHI",
    new DateTime(2026, 9, 24, 12, 0, 0), [700m, 710m, 2500m],
    [new ValidationRule { Code = "USR-MMF-0003", DataType = "MMF", FieldName = "GHI",
        IsActive = true, RuleSource = "Configured", MinimumValue = 0m, MaximumValue = 1000m }]);
if (!configuredMmfRange.ValidValues.SequenceEqual([700m, 710m])
    || !configuredMmfRange.Issues.Any(issue => issue.RuleCode == "USR-MMF-0003"))
    throw new Exception("MMF averaging must apply the administrator's configured range for the selected API parameter.");
var configuredMmfSensorChange = ConfiguredValidationRuleEvaluator.Evaluate(
    [new ValidationRule { Code = "USR-MMF-0004", DataType = "MMF", FieldName = "WindSpeed",
        IsActive = true, RuleSource = "Configured", ChangeThreshold = 5m }],
    [new SolarApiReading { Parameter = "WindSpeed", Value = 2m, IntervalStart = new DateTime(2026, 9, 24, 10, 0, 0) },
     new SolarApiReading { Parameter = "WindSpeed", Value = 12m, IntervalStart = new DateTime(2026, 9, 24, 10, 15, 0) }], "MMF");
if (!configuredMmfSensorChange.Any(issue => issue.RuleCode == "USR-MMF-0004"))
    throw new Exception("MMF change-rate checks must be evaluated from the administrator's configured parameter rule.");
Console.WriteLine("PASS: MMF parameter limits and change rates are applied only from administrator-configured rules");

const string rawMmfQualityPayload = """
[
  { "timestamp": "2026-09-24T12:00:00", "parameter": "GHI", "value": 700, "unit": "W/m2" },
  { "timestamp": "2026-09-24T12:00:00", "parameter": "GHI", "value": 710, "unit": "W/m2" },
  { "timestamp": "2026-09-24T12:00:00", "parameter": "GHI", "value": 2500, "unit": "W/m2" }
]
""";
var mmfFixedRules = Array.Empty<ValidationRule>();
var storedMmfTranslation = ScheduledApiPayloadTranslator.Translate(rawMmfQualityPayload, "MMF", "MMF",
    1, 400, new DateTime(2026, 9, 24, 12, 0, 0), 15, "application/json", mmfFixedRules);
var reportMmfTranslation = ScheduledApiPayloadTranslator.Translate(rawMmfQualityPayload, "MMF", "MMF",
    1, 400, new DateTime(2026, 9, 24, 12, 0, 0), 15, "application/json", mmfFixedRules,
    applyValidation: true);
if (storedMmfTranslation.Readings.Single().Value != decimal.Round((700m + 710m + 2500m) / 3m, 6)
    || reportMmfTranslation.Readings.Single().Value != decimal.Round((700m + 710m + 2500m) / 3m, 6)
    || reportMmfTranslation.Issues.Count != 0)
    throw new Exception("MMF values must remain unfiltered until an administrator creates a parameter rule.");
var configuredMmfTranslation = ScheduledApiPayloadTranslator.Translate(rawMmfQualityPayload, "MMF", "MMF",
    1, 401, new DateTime(2026, 9, 24, 12, 0, 0), 15, "application/json",
    [new ValidationRule { Code = "USR-MMF-0003", DataType = "MMF", FieldName = "GHI",
        IsActive = true, RuleSource = "Configured", MinimumValue = 0m, MaximumValue = 1000m }], applyValidation: true);
if (configuredMmfTranslation.Readings.Single().Value != 705m
    || !configuredMmfTranslation.Issues.Any(issue => issue.RuleCode == "USR-MMF-0003"))
    throw new Exception("MMF reporting must exclude only readings outside the administrator-configured parameter range.");
Console.WriteLine("PASS: MMF collection stores original readings and reports apply only configured parameter limits");

var reportPlant = new LssPlant { Id = 1, PlantId = "LSS-001", Name = "Report Plant", CapacityMw = 100m };
var reportSubmission = new DateTime(2026, 9, 24);
var currentDdq = new List<SolarApiReading>
{
    new() { Id = 10, PlantId = 1, DataType = "Day Ahead", SubmissionDateTime = reportSubmission,
        IntervalStart = reportSubmission, IntervalMinutes = 30, Parameter = "MW", Value = -5m, Unit = "MW" },
    new() { Id = 11, PlantId = 1, DataType = "Day Ahead", SubmissionDateTime = reportSubmission,
        IntervalStart = reportSubmission.AddMinutes(60), IntervalMinutes = 30, Parameter = "MW", Value = 30m, Unit = "MW" }
};
var previousDdq = new List<SolarApiReading>
{
    new() { Id = 9, PlantId = 1, DataType = "Day Ahead", SubmissionDateTime = reportSubmission.AddDays(-1),
        IntervalStart = reportSubmission, IntervalMinutes = 30, Parameter = "MW", Value = 25m, Unit = "MW" }
};
var preparedDdq = ReportSubstitutionPolicy.PrepareDdq("Day Ahead", reportSubmission,
    [reportPlant], currentDdq, previousDdq);
if (preparedDdq.Readings.Single(item => item.IntervalStart == reportSubmission).Value != 25m
    || !preparedDdq.Readings.Single(item => item.IntervalStart == reportSubmission).IsSynthetic
    || preparedDdq.Summary.PreviousDayValues != 1
    || preparedDdq.Summary.ZeroFallbackValues == 0
    || currentDdq[0].Value != -5m)
    throw new Exception("DDQ email preparation must use previous-day then zero without modifying stored readings.");
Console.WriteLine("PASS: DDQ report-only substitution uses previous-day then zero and preserves database values");

var currentMmf = new List<SolarApiReading>
{
    new() { Id = 20, PlantId = 1, DataType = "MMF", SubmissionDateTime = reportSubmission,
        IntervalStart = reportSubmission, IntervalMinutes = 15, Parameter = "GHI", Value = 700m, Unit = "W/m2" }
};
var previousMmf = new List<SolarApiReading>
{
    new() { Id = 19, PlantId = 1, DataType = "MMF", SubmissionDateTime = reportSubmission.AddDays(-1),
        IntervalStart = reportSubmission.AddDays(-1).AddMinutes(15), IntervalMinutes = 15,
        Parameter = "GHI", Value = 650m, Unit = "W/m2" }
};
var preparedMmf = ReportSubstitutionPolicy.PrepareMmf(reportSubmission, 15,
    [reportPlant], currentMmf, previousMmf);
if (preparedMmf.Readings.Single(item => item.IntervalStart == reportSubmission).Value != 700m
    || preparedMmf.Readings.Single(item => item.IntervalStart == reportSubmission.AddMinutes(15)).Value != 650m
    || preparedMmf.Readings.Single(item => item.IntervalStart == reportSubmission.AddMinutes(30)).Value != 0m)
    throw new Exception("MMF email preparation must retain valid averages and use previous-day then zero for missing averages.");
Console.WriteLine("PASS: MMF report-only substitution uses valid average, previous-day and zero fallback");

var reportAnomalyCsv = ReportAnomalyCsvExporter.Build("DDQ", "Day Ahead", reportSubmission,
    [reportPlant], currentDdq, preparedDdq,
    [new ReportDetectedAnomaly("COL-001", reportPlant.Id, "MW", "-5",
        "Active power cannot be below 0 MW.", "Open", DateTimeOffset.UtcNow)]);
if (!reportAnomalyCsv.Contains("\"Validation anomaly\"")
    || !reportAnomalyCsv.Contains("\"Report substitution\"")
    || !reportAnomalyCsv.Contains("\"-5\",\"25\"")
    || !reportAnomalyCsv.Contains("previous-day submission used"))
    throw new Exception("Scheduled anomaly report must include validation anomalies and applied substitutions.");
Console.WriteLine("PASS: Scheduled anomaly report includes validation anomalies and report substitutions");

const string payloadSubmissionTimes = """
[
  { "submissionDateTime": "2026-09-24T10:10:00", "timestamp": "2026-09-25T00:00:00", "parameter": "MW", "value": 10 },
  { "submissionDateTime": "2026-09-24T10:40:00", "timestamp": "2026-09-25T00:00:00", "parameter": "MW", "value": 11 }
]
""";
var scheduleSubmission1010 = new DateTime(2026, 9, 24, 10, 10, 0);
var scheduledTranslation = ScheduledApiPayloadTranslator.Translate(payloadSubmissionTimes,
    "DDQ", "Day Ahead", 1, 500, scheduleSubmission1010, 30, "application/json");
const string duplicateSubmissionTime = """
[
  { "submissionDateTime": "2026-09-24T10:10:00", "timestamp": "2026-09-25T00:00:00", "parameter": "MW", "value": 10 },
  { "submissionDateTime": "2026-09-24T10:10:00", "timestamp": "2026-09-25T00:00:00", "parameter": "MW", "value": 11 }
]
""";
var duplicateSubmissionTranslation = ScheduledApiPayloadTranslator.Translate(duplicateSubmissionTime,
    "DDQ", "Day Ahead", 1, 501, reportSubmission, 30, "application/json");
if (scheduledTranslation.Readings.Count != 1
    || scheduledTranslation.Readings[0].SubmissionDateTime != scheduleSubmission1010
    || !scheduledTranslation.Issues.Any(issue => issue.RuleCode == "DDQ-002")
    || !duplicateSubmissionTranslation.Issues.Any(issue => issue.RuleCode == "DDQ-002"
        && issue.Reason.Contains("24/09/2026 00:00:00")))
    throw new Exception("Collection schedule time must be authoritative and duplicate validation must use it.");
Console.WriteLine("PASS: Submission date and time comes from the collection schedule and drives duplicate validation");

if (TimeDisplay.TwentyFourHour(new TimeOnly(0, 0)) != "00:00"
    || TimeDisplay.TwentyFourHour(new TimeOnly(13, 5)) != "13:05"
    || TimeDisplay.TwentyFourHour("23:45") != "23:45")
    throw new Exception("Calendar and schedule time must be displayed using the 24-hour format.");
Console.WriteLine("PASS: Calendar and schedule fields use the 24-hour display");

static string FindRepositoryRoot()
{
    foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(startingPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TestingSystem.Web", "TestingSystem.Web.csproj")))
                return current.FullName;

            current = current.Parent;
        }
    }

    throw new DirectoryNotFoundException("Could not locate the SBLSS repository root for the frontend architecture check.");
}
