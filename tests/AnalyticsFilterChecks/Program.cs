using TestingSystem.Web.Models;
using TestingSystem.Web.Services;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Components.Frontend.UseCases;
using System.IO.Compression;

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
var workbookBytes = DdqExcelWorkbookExporter.Build([
    new DdqWorkbookSheet("AmbientTemp", "AMBIENT TEMP", [("Data Interval", "15 minutes")],
        ["Plant Name", "Plant ID / Code", "14/09 00:00"],
        [new object?[] { "Bidor SAND Town", "BDNS", 22m }], 2),
    new DdqWorkbookSheet("Anomaly Report", "ANOMALY REPORT", [], ["Parameter", "Reason"], [], 2)
]);
using var workbookStream = new MemoryStream(workbookBytes);
using var workbookArchive = new ZipArchive(workbookStream, ZipArchiveMode.Read);
if (workbookArchive.GetEntry("xl/worksheets/sheet1.xml") is null || workbookArchive.GetEntry("xl/worksheets/sheet2.xml") is null)
    throw new Exception("MMF Excel export must include parameter and anomaly worksheets");
using var worksheetReader = new StreamReader(workbookArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
var worksheetXml = worksheetReader.ReadToEnd();
if (!worksheetXml.Contains("xSplit=\"2\"") || !worksheetXml.Contains("topLeftCell=\"C7\""))
    throw new Exception("MMF Excel export must freeze the plant name and code columns");
Console.WriteLine("PASS: MMF Excel workbook sheets and frozen plant columns");
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
if (!CollectionSchedulePolicy.IsDue("Rolling 24 Hour", "15 minutes", "00:15", "", "", scheduleNow, scheduleNow.AddMinutes(-15))
    || CollectionSchedulePolicy.IsDue("Rolling 24 Hour", "15 minutes", "00:15", "", "", scheduleNow, scheduleNow.AddMinutes(-14)))
    throw new Exception("Interval collection schedule timing failed");
if (!CollectionSchedulePolicy.IsDue("Daily", "15 minutes", "10:00", "", "", scheduleNow, null)
    || CollectionSchedulePolicy.IsDue("Daily", "15 minutes", "11:00", "", "", scheduleNow, null))
    throw new Exception("Daily collection schedule timing failed");
if (!CollectionSchedulePolicy.IsDue("4 Month", "15 minutes", "10:00", "23", "1,5,9", scheduleNow, null))
    throw new Exception("Four-month collection schedule timing failed");
Console.WriteLine("PASS: Collection schedule timing and API endpoint mapping");
var translatedDdq = ScheduledApiPayloadTranslator.Translate(
    """{"submissionDateTime":"2026-09-23T10:00:00+08:00","forecastData":[{"startTime":"2026-09-24T00:00:00+08:00","endTime":"2026-09-24T00:15:00+08:00","forecastValue":12.5,"valueUnit":"MW"}]}""",
    "DDQ", "Day Ahead", 1, 100, scheduleNow, 15);
if (translatedDdq.Readings.Count != 1 || translatedDdq.Readings[0].Value != 12.5m
    || translatedDdq.Readings[0].Parameter != "forecastValue")
    throw new Exception("Scheduled DDQ payload translation failed");
var translatedMmf = ScheduledApiPayloadTranslator.Translate(
    """{"sensorReadings":[{"timestamp":"2026-09-23T10:00:00+08:00","parameter":"GHI","value":400,"unit":"W/m²"},{"timestamp":"2026-09-23T10:00:00+08:00","parameter":"GHI","value":500,"unit":"W/m²"}]}""",
    "MMF", "MMF", 1, 101, scheduleNow, 15);
if (translatedMmf.Readings.Count != 1 || translatedMmf.Readings[0].Value != 450m
    || translatedMmf.Readings[0].Parameter != "GHI")
    throw new Exception("Scheduled MMF sensor averaging failed");
var translatedXml = ScheduledApiPayloadTranslator.Translate(
    """<response><reading><timestamp>2026-09-23T10:00:00+08:00</timestamp><parameter>GHI</parameter><value>725.5</value><unit>W/m2</unit></reading></response>""",
    "MMF", "MMF", 1, 102, scheduleNow, 15, "application/xml");
if (translatedXml.PayloadFormat != "XML" || translatedXml.Readings.Count != 1 || translatedXml.Readings[0].Value != 725.5m)
    throw new Exception("Scheduled XML payload translation failed");
var translatedCsv = ScheduledApiPayloadTranslator.Translate(
    "timestamp,forecastValue,valueUnit\n2026-09-23T10:00:00+08:00,18.75,MW",
    "DDQ", "Day Ahead", 1, 103, scheduleNow, 15, "text/csv");
if (translatedCsv.PayloadFormat != "CSV" || translatedCsv.Readings.Count != 1 || translatedCsv.Readings[0].Value != 18.75m)
    throw new Exception("Scheduled CSV payload translation failed");
try
{
    ScheduledApiPayloadTranslator.Translate("<html><body>Sign in</body></html>", "DDQ", "Day Ahead", 1, 104, scheduleNow, 15, "text/html");
    throw new Exception("HTML API response should not be accepted as measurement data");
}
catch (FormatException exception) when (exception.Message.Contains("HTML page")) { }
Console.WriteLine("PASS: Scheduled JSON, XML and CSV payload translation with clear HTML rejection");
