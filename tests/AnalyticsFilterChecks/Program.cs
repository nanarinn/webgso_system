using TestingSystem.Web.Models;
using TestingSystem.Web.Services;
using TestingSystem.Web.Data;
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
if (PlantGraphSeries.Points([0, 50, 100], 0, 100) != "72.0,325.0 521.0,185.0 970.0,45.0")
    throw new Exception("Shared graph scale failed");
if (PlantGraphSeries.Points([-5, 0, 5], -5, 5) != "72.0,325.0 521.0,185.0 970.0,45.0")
    throw new Exception("Signed variance scale failed");
if (PlantGraphSeries.Points([0], 0, 0) != "72.0,325.0")
    throw new Exception("Single zero point failed");
Console.WriteLine("PASS: Graph scale, negative variance and zero series");
if (MmfSensorAveraging.AverageNonZero([20m, 22m, 0m, 24m, 0m]) != 22m)
    throw new Exception("MMF averaging must exclude zero placeholders");
if (MmfSensorAveraging.AverageNonZero([0m, 0m, 0m]) != 0m)
    throw new Exception("MMF averaging must keep an all-zero sensor interval as zero");
if (MmfSensorAveraging.AverageNonZero([1.1234567m, 2.1234567m]) != 1.623457m)
    throw new Exception("MMF averaging must round normalized values to six decimals");
Console.WriteLine("PASS: MMF multi-sensor averaging and zero-placeholder rules");
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
