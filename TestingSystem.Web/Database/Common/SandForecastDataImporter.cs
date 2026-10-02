using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Data;

public static class SandForecastDataImporter
{
    private static readonly (string Sheet, string Path, string DataType)[] Sources =
    [
        ("Rolling24", "xl/worksheets/sheet2.xml", "Rolling 24 Hour"),
        ("DayAhead", "xl/worksheets/sheet3.xml", "Day Ahead"),
        ("WeekAhead", "xl/worksheets/sheet4.xml", "Week Ahead"),
        ("FourMonthsAhead", "xl/worksheets/sheet5.xml", "4 Month Ahead")
    ];

    public static async Task ImportAsync(ApplicationDbContext database, string workbookPath)
    {
        if (!File.Exists(workbookPath)) throw new FileNotFoundException("SAND forecast workbook was not found.", workbookPath);
        var fileBytes = await File.ReadAllBytesAsync(workbookPath);
        var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes));
        var submission = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day,
            DateTime.Now.Hour, DateTime.Now.Minute, 0);

        var plant = await database.LssPlants.FirstOrDefaultAsync(item => item.PlantId == "BDNS");
        if (plant is null)
        {
            plant = new LssPlant
            {
                PlantId = "BDNS", Name = "Bidor SAND Town", State = "Perak", CapacityMw = 30m,
                DdqApiEndpoint = "https://simulated-api.local/ddq/BDNS",
                MmfApiEndpoint = "https://simulated-api.local/mmf/BDNS",
                IsActive = true, ApiConnectionStatus = "Imported API data"
            };
            database.LssPlants.Add(plant);
        }
        else
        {
            plant.Name = "Bidor SAND Town";
            plant.State = "Perak";
            plant.CapacityMw = 30m;
            plant.IsActive = true;
            plant.ApiConnectionStatus = "Imported API data";
        }
        await database.SaveChangesAsync();

        using var archive = new ZipArchive(new MemoryStream(fileBytes), ZipArchiveMode.Read);
        foreach (var source in Sources)
        {
            var readings = ParseWorksheet(archive, source.Path);
            if (readings.Count == 0) throw new InvalidDataException($"Worksheet {source.Sheet} contains no forecast readings.");
            var response = new SandApiResponse("Bidor SAND Forecast API", plant.PlantId, plant.Name,
                source.DataType, submission, readings);
            var rawJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
            var translated = JsonSerializer.Deserialize<SandApiResponse>(rawJson)
                ?? throw new InvalidDataException($"Raw API response for {source.Sheet} could not be translated.");
            var compactType = new string(source.DataType.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var collectionId = $"SAND-{plant.PlantId}-{compactType}-{fileHash[..12]}";
            var existingJob = await database.CollectionJobs.FirstOrDefaultAsync(item => item.CollectionId == collectionId);
            if (existingJob is not null)
            {
                await database.SolarApiReadings.Where(item => item.CollectionJobId == existingJob.Id).ExecuteDeleteAsync();
                await database.RawApiResponses.Where(item => item.CollectionJobId == existingJob.Id).ExecuteDeleteAsync();
                database.CollectionJobs.Remove(existingJob);
                await database.SaveChangesAsync();
            }

            var job = new CollectionJobEntity
            {
                CollectionId = collectionId, PlantId = plant.Id, DataType = source.DataType,
                Mode = "SAND API workbook import", SubmissionDate = DateOnly.FromDateTime(submission),
                SubmissionDateTime = submission,
                StartedAt = DateTimeOffset.Now, CompletedAt = DateTimeOffset.Now, Status = "Completed"
            };
            database.CollectionJobs.Add(job);
            await database.SaveChangesAsync();
            database.RawApiResponses.Add(new RawApiResponse
            {
                CollectionJobId = job.Id, ReceivedAt = DateTimeOffset.Now, HttpStatusCode = 200,
                ContentType = "application/json", Payload = rawJson,
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)))
            });
            database.SolarApiReadings.AddRange(translated.ForecastData.Select(row => new SolarApiReading
            {
                CollectionJobId = job.Id, PlantId = plant.Id, DataType = translated.DataType,
                SubmissionDateTime = translated.SubmissionDateTime, IntervalStart = row.StartTime,
                IntervalEnd = row.EndTime, IntervalMinutes = row.IntervalMinutes,
                Parameter = row.ForecastType, Value = row.ForecastValue, Unit = row.Unit,
                ErrorCode = 0, ErrorMessage = "OK", IsSynthetic = false
            }));
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
            Console.WriteLine($"  {source.DataType}: {readings.Count:N0} reading(s)");
        }
    }

    private static List<SandApiReading> ParseWorksheet(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Workbook entry {path} was not found.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var result = new List<SandApiReading>();
        foreach (var row in document.Descendants(ns + "row").Skip(1))
        {
            var cells = row.Elements(ns + "c").ToDictionary(
                cell => new string(((string?)cell.Attribute("r") ?? string.Empty).TakeWhile(char.IsLetter).ToArray()),
                cell => CellText(cell, ns));
            if (!cells.TryGetValue("B", out var dateText) || !cells.TryGetValue("C", out var startText)
                || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)
                || !TimeOnly.TryParseExact(startText, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var startTime)
                || !decimal.TryParse(cells.GetValueOrDefault("F"), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var value)) continue;
            var start = date.ToDateTime(startTime);
            DateTime? end = null;
            if (TimeOnly.TryParseExact(cells.GetValueOrDefault("D"), "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var endTime))
            {
                end = date.ToDateTime(endTime);
                if (end <= start) end = end.Value.AddDays(1);
            }
            var minutes = int.TryParse(cells.GetValueOrDefault("E"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsedMinutes) ? parsedMinutes : 15;
            result.Add(new SandApiReading(cells.GetValueOrDefault("A") ?? "ActivePowerProduction",
                start, end ?? start.AddMinutes(minutes), minutes, value, cells.GetValueOrDefault("G") ?? "MW"));
        }
        return result;
    }

    private static string CellText(XElement cell, XNamespace ns) =>
        (string?)cell.Descendants(ns + "t").FirstOrDefault()
        ?? (string?)cell.Element(ns + "v") ?? string.Empty;

    private sealed record SandApiResponse(string Source, string PlantId, string PlantName,
        string DataType, DateTime SubmissionDateTime, List<SandApiReading> ForecastData);
    private sealed record SandApiReading(string ForecastType, DateTime StartTime, DateTime EndTime,
        int IntervalMinutes, decimal ForecastValue, string Unit);
}
