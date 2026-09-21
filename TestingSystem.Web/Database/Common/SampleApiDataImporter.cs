using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Data;

public static partial class SampleApiDataImporter
{
    private static readonly (string File, string DataType)[] Sources =
    [
        ("Sample_rolling_sent.xlsx", "Rolling 24 Hour"),
        ("Sample_dayahead_sent.xlsx", "Day Ahead"),
        ("Sample_weekahead_sent.xlsx", "Week Ahead"),
        ("Sample_monthahead_sent.xlsx", "4 Month Ahead"),
        ("Sample_mmf_sent.xlsx", "MMF")
    ];

    public static async Task ImportAsync(ApplicationDbContext database, IConfiguration configuration)
    {
        var folder = configuration["SampleApiImport:Folder"];
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Sample API import folder was not found: {folder}");
        Console.WriteLine($"Sample import folder: {folder}");

        var plants = await EnsureSamplePlantsAsync(database);
        var workbooksFound = 0;
        foreach (var (fileName, dataType) in Sources)
        {
            var path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
            {
                Console.WriteLine($"Sample file not found, skipped: {path}");
                continue;
            }
            workbooksFound++;
            Console.WriteLine($"Importing sample API workbook: {fileName}");
            await ImportWorkbookAsync(database, path, dataType, plants);
        }

        if (workbooksFound == 0)
            throw new FileNotFoundException($"None of the five sample workbooks were found in {folder}.");

        // A submission timestamp belongs to the API/import event, not to the
        // forecast interval inside the payload. Keep one stable Malaysia-time
        // timestamp for every record produced by the same imported sheet.
        await database.Database.ExecuteSqlRawAsync("""
            DECLARE @SampleSubmission datetimeoffset =
                (SELECT MIN([StartedAt]) FROM [CollectionJobs] WHERE [Mode] = N'Excel sample import');

            UPDATE reading
            SET [SubmissionDateTime] = CONVERT(datetime2,
                SWITCHOFFSET(@SampleSubmission, '+08:00'))
            FROM [SolarApiReadings] reading
            INNER JOIN [CollectionJobs] job ON job.[Id] = reading.[CollectionJobId]
            WHERE job.[Mode] = N'Excel sample import';

            UPDATE job
            SET [SubmissionDate] = CONVERT(date, SWITCHOFFSET(@SampleSubmission, '+08:00'))
            FROM [CollectionJobs] job
            WHERE job.[Mode] = N'Excel sample import';
            """);

        await EnsureCompleteTestMatrixAsync(database, plants);
    }

    public static async Task ImportMmfAsync(ApplicationDbContext database, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("MMF sample workbook was not found.", path);
        var strategy = database.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        await using var transaction = await database.Database.BeginTransactionAsync();
        try
        {
        var oldJobs = await database.CollectionJobs.Where(x => x.DataType == "MMF").Select(x => x.Id).ToListAsync();
        await database.SolarApiReadings.Where(x => x.DataType == "MMF").ExecuteDeleteAsync();
        if (oldJobs.Count > 0)
        {
            await database.RawApiResponses.Where(x => oldJobs.Contains(x.CollectionJobId)).ExecuteDeleteAsync();
            await database.CollectionJobs.Where(x => oldJobs.Contains(x.Id)).ExecuteDeleteAsync();
        }
        await database.LssPlants.Where(x => x.PlantId.StartsWith("SAMPLE-"))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        var ddqPlantIds = await database.SolarApiReadings.AsNoTracking()
            .Where(x => x.DataType != "MMF").Select(x => x.PlantId).Distinct().ToListAsync();
        var plants = await database.LssPlants.Where(x => x.IsActive && ddqPlantIds.Contains(x.Id)
                && !x.PlantId.StartsWith("SAMPLE-"))
            .OrderBy(x => x.Name).ToListAsync();
        if (plants.Count == 0) throw new InvalidOperationException("No active DDQ plants are available for MMF generation.");
        await CompleteMmfPlantsThroughRawApiAsync(database, path, plants);
        var newJobs = await database.CollectionJobs.CountAsync(x => x.DataType == "MMF" && x.Mode == "Simulated MMF API");
        var newJobIds = await database.CollectionJobs.Where(x => x.DataType == "MMF" && x.Mode == "Simulated MMF API")
            .Select(x => x.Id).ToListAsync();
        var rawResponses = await database.RawApiResponses.CountAsync(x => newJobIds.Contains(x.CollectionJobId));
        var readings = await database.SolarApiReadings.CountAsync(x => x.DataType == "MMF");
        var coveredPlants = await database.SolarApiReadings.Where(x => x.DataType == "MMF").Select(x => x.PlantId).Distinct().CountAsync();
        var parameterValues = await database.SolarApiReadings.Where(x => x.DataType == "MMF")
            .Select(x => x.Parameter).ToListAsync();
        var parameters = parameterValues.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Console.WriteLine();
        Console.WriteLine("MMF import verification");
        Console.WriteLine($"  DDQ plants covered: {coveredPlants:N0} / {plants.Count:N0}");
        Console.WriteLine($"  Collection jobs:     {newJobs:N0}");
        Console.WriteLine($"  Raw API responses:   {rawResponses:N0}");
        Console.WriteLine($"  Parameters:          {parameters:N0}");
        Console.WriteLine($"  Averaged readings:   {readings:N0}");
        if (newJobs != plants.Count || rawResponses != plants.Count || coveredPlants != plants.Count
            || parameters < 14 || readings < plants.Count * 14 * 288)
            throw new InvalidOperationException("MMF verification failed. The database was not changed.");
        await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        });
    }

    private static async Task<Dictionary<int, LssPlant>> EnsureSamplePlantsAsync(ApplicationDbContext database)
    {
        var numbers = Enumerable.Range(1, 10);
        foreach (var number in numbers)
        {
            var code = $"SAMPLE-{number:00}";
            if (await database.LssPlants.AnyAsync(x => x.PlantId == code)) continue;
            database.LssPlants.Add(new LssPlant
            {
                PlantId = code, Name = $"Sample Plant {number}", State = "Sample",
                CapacityMw = 100, IsActive = true, ApiConnectionStatus = "Imported sample"
            });
        }
        await database.SaveChangesAsync();
        var oldDemoIds = new[] { "PLT-BDR-01", "PLT-KLM-02", "PLT-SPG-03", "PLT-PGR-04" };
        await database.LssPlants.Where(x => oldDemoIds.Contains(x.PlantId))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        return await database.LssPlants.Where(x => x.PlantId.StartsWith("SAMPLE-"))
            .ToDictionaryAsync(x => int.Parse(x.PlantId[7..], CultureInfo.InvariantCulture));
    }

    private static async Task ImportWorkbookAsync(
        ApplicationDbContext database, string path, string dataType, Dictionary<int, LssPlant> plants)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes));
        var submission = DateTime.Now;

        var sheets = ReadSheets(bytes).ToList();
        Console.WriteLine($"  Workbook sheets found: {sheets.Count}");
        if (sheets.Count == 0)
            throw new InvalidDataException($"No readable worksheets were found in {path}.");
        var matchedSheets = 0;
        foreach (var sheet in sheets)
        {
            var numberMatch = SheetNumberRegex().Match(sheet.Name);
            if (!numberMatch.Success
                || !int.TryParse(numberMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var plantNumber))
            {
                Console.WriteLine($"  Sheet skipped because its plant number is unknown: {sheet.Name}");
                continue;
            }
            var plantCode = $"SAMPLE-{plantNumber:00}";
            var plant = await database.LssPlants.FirstOrDefaultAsync(x => x.PlantId == plantCode);
            if (plant is null)
                throw new InvalidOperationException($"Worksheet {sheet.Name} requires missing plant {plantCode}.");
            matchedSheets++;

            var sheetHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sheet.Payload)));
            var parserVersion = dataType == "MMF" ? "-AVERAGED4" : sheet.Payload.TrimStart().StartsWith('{') ? "-JSON2" : string.Empty;
            var collectionId = $"SAMPLE-{dataType.Replace(" ", "").ToUpperInvariant()}-{plantNumber:00}-{sheetHash[..12]}{parserVersion}";
            var existingJob = await database.CollectionJobs.FirstOrDefaultAsync(x => x.CollectionId == collectionId);
            if (existingJob is not null && await database.SolarApiReadings.AnyAsync(x => x.CollectionJobId == existingJob.Id)) continue;
            if (existingJob is not null)
            {
                await database.RawApiResponses.Where(x => x.CollectionJobId == existingJob.Id).ExecuteDeleteAsync();
                database.CollectionJobs.Remove(existingJob);
                await database.SaveChangesAsync();
            }

            var job = new CollectionJobEntity
            {
                CollectionId = collectionId, PlantId = plant.Id, DataType = dataType,
                Mode = "Excel sample import", SubmissionDate = DateOnly.FromDateTime(submission),
                StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow, Status = "Completed"
            };
            database.CollectionJobs.Add(job);
            await database.SaveChangesAsync();
            database.RawApiResponses.Add(new RawApiResponse
            {
                CollectionJobId = job.Id, ReceivedAt = DateTimeOffset.UtcNow, HttpStatusCode = 200,
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Payload = sheet.Payload, Sha256 = fileHash
            });

            var readings = ParseForecast(sheet.Payload, job.Id, plant.Id, dataType, submission);
            database.SolarApiReadings.AddRange(readings);
            await database.SaveChangesAsync();
            Console.WriteLine($"  {sheet.Name}: {readings.Count:N0} normalized reading(s)");
            database.ChangeTracker.Clear();
        }
        if (matchedSheets == 0)
            throw new InvalidDataException($"No worksheet in {Path.GetFileName(path)} matched a SAMPLE plant.");
    }

    private static List<SolarApiReading> ParseForecast(
        string payload, long jobId, int plantId, string dataType, DateTime submission)
    {
        if (dataType == "MMF") return ParseMmf(payload, jobId, plantId, submission);
        if (payload.TrimStart().StartsWith('{'))
        {
            try { return ParseJsonForecast(payload, jobId, plantId, dataType, submission); }
            catch (JsonException exception)
            {
                Console.WriteLine($"  Vendor JSON is not fully valid ({exception.Message}); using tolerant line parser.");
            }
        }
        var result = new List<SolarApiReading>();
        DateTime? start = null, end = null;
        int? minutes = null, error = null;
        string parameter = dataType, unit = "MW";
        foreach (var line in payload.Split('\n'))
        {
            if (TryTag(line, "IntervalStartTime", out var text) || TryJson(line, "StartTime", out text))
                start = ParseDate(text);
            else if (TryTag(line, "IntervalEndTime", out text) || TryJson(line, "EndTime", out text))
                end = ParseDate(text);
            else if (TryTag(line, "IntervalLength", out text) && int.TryParse(text, out var parsedMinutes))
                minutes = parsedMinutes;
            else if (TryTag(line, "ForecastResultParameter", out text)) parameter = text;
            else if (TryTag(line, "DataErrorCode", out text) && int.TryParse(text, out var parsedError)) error = parsedError;
            else if (TryTag(line, "ValueUnit", out text) || TryJson(line, "ValueUnit", out text)) unit = text;
            else if ((TryTag(line, "ForecastValue", out text) || TryJson(line, "ForecastValue", out text))
                     && start.HasValue && decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                result.Add(new SolarApiReading
                {
                    CollectionJobId = jobId, PlantId = plantId, DataType = dataType,
                    SubmissionDateTime = submission, IntervalStart = start.Value, IntervalEnd = end,
                    IntervalMinutes = minutes, Parameter = parameter, Value = value, Unit = unit,
                    ErrorCode = error, IsSynthetic = false
                });
            }
        }
        return result;
    }

    private static List<SolarApiReading> ParseJsonForecast(
        string payload, long jobId, int plantId, string dataType, DateTime submission)
    {
        var result = new List<SolarApiReading>();
        var firstBrace = payload.IndexOf('{');
        var lastBrace = payload.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace) return result;
        var json = payload[firstBrace..(lastBrace + 1)];
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var root = document.RootElement;
        if (root.TryGetProperty("ForecastResponse", out var response)) root = response;
        if (!root.TryGetProperty("ForecastData", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("StartTime", out var startValue)
                || !DateTime.TryParse(startValue.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var start)) continue;
            DateTime? end = null;
            if (row.TryGetProperty("EndTime", out var endValue)
                && DateTime.TryParse(endValue.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var parsedEnd)) end = parsedEnd;
            if (!row.TryGetProperty("ForecastValue", out var forecastValue)
                || !decimal.TryParse(forecastValue.ToString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var value)) continue;
            var minutes = end.HasValue ? (int?)(end.Value - start).TotalMinutes : null;
            result.Add(new SolarApiReading
            {
                CollectionJobId = jobId, PlantId = plantId, DataType = dataType,
                SubmissionDateTime = submission, IntervalStart = start, IntervalEnd = end,
                IntervalMinutes = minutes, Parameter = dataType, Value = value,
                Unit = row.TryGetProperty("ValueUnit", out var unit) ? unit.GetString() ?? "MW" : "MW",
                IsSynthetic = false
            });
        }
        return result;
    }

    private static List<SolarApiReading> ParseMmf(
        string payload, long jobId, int plantId, DateTime submission)
    {
        if (payload.TrimStart().StartsWith('{'))
            return ParseMmfJson(payload, jobId, plantId, submission);
        var candidates = new List<(DateTime Time, string Parameter, string Unit, decimal Value)>();
        DateTime? time = null;
        string parameter = "MMF", unit = string.Empty;
        var values = new List<decimal>();
        void Flush()
        {
            if (!time.HasValue || values.Count == 0) return;
            candidates.AddRange(values.Select(value => (time.Value, parameter, unit, value)));
            values.Clear();
        }
        foreach (var line in payload.Split('\n'))
        {
            if (TryTag(line, "MeasurementTime", out var text) || TryJson(line, "Time", out text)
                || TryTag(line, "IntervalStartTime", out text))
            {
                Flush(); time = ParseDate(text);
            }
            else if (TryTag(line, "ResultParameter", out text) || TryJson(line, "Parameter", out text)
                || TryTag(line, "ForecastResultParameter", out text))
            {
                Flush(); parameter = text;
            }
            else if (TryTag(line, "Unit", out text) || TryJson(line, "Unit", out text)
                || TryTag(line, "ValueUnit", out text)) unit = text;
            else
            {
                var match = MmfValueRegex().Match(line);
                if (match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var mmfValue)) values.Add(mmfValue);
                else if ((TryTag(line, "ForecastValue", out text) || TryJson(line, "ForecastValue", out text))
                         && decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var forecastValue))
                    values.Add(forecastValue);
            }
        }
        Flush();
        return candidates.GroupBy(x => new { x.Time, x.Parameter, x.Unit }).Select(group =>
        {
            var valid = group.Where(x => x.Value != 0).Select(x => x.Value).ToList();
            var average = MmfSensorAveraging.AverageNonZero(group.Select(x => x.Value));
            return new SolarApiReading
            {
                CollectionJobId = jobId, PlantId = plantId, DataType = "MMF",
                SubmissionDateTime = submission, IntervalStart = group.Key.Time,
                IntervalMinutes = 15, Parameter = group.Key.Parameter, Value = decimal.Round(average, 6),
                Unit = group.Key.Unit, IsSynthetic = false,
                SyntheticReason = $"Average of {valid.Count} valid MMF sensor reading(s); zero placeholders excluded."
            };
        }).ToList();
    }

    private static List<SolarApiReading> ParseMmfJson(
        string payload, long jobId, int plantId, DateTime submission)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.TryGetProperty("MMFResponse", out var response)) root = response;
        if (!root.TryGetProperty("MMFData", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MMF JSON response does not contain an MMFData array.");
        var result = new List<SolarApiReading>();
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("Parameter", out var parameterElement)
                || !row.TryGetProperty("Time", out var timeElement)
                || !DateTime.TryParse(timeElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var time)) continue;
            var sensorValues = row.EnumerateObject()
                .Where(property => property.Name.StartsWith("MMF", StringComparison.OrdinalIgnoreCase)
                    && property.Name.Length > 3 && property.Name[3..].All(char.IsDigit))
                .Select(property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() : property.Value.ToString())
                .Select(value => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var parsed) ? (decimal?)parsed : null)
                .Where(value => value.HasValue).Select(value => value!.Value).ToList();
            if (sensorValues.Count == 0) continue;
            var validValues = sensorValues.Where(value => value != 0).ToList();
            var average = MmfSensorAveraging.AverageNonZero(sensorValues);
            result.Add(new SolarApiReading
            {
                CollectionJobId = jobId, PlantId = plantId, DataType = "MMF",
                SubmissionDateTime = submission, IntervalStart = time, IntervalMinutes = 15,
                Parameter = parameterElement.GetString() ?? "MMF", Value = decimal.Round(average, 6),
                Unit = row.TryGetProperty("Unit", out var unit) ? unit.GetString() ?? "" : "",
                IsSynthetic = false,
                SyntheticReason = $"Average of {validValues.Count} valid values from {sensorValues.Count} MMF sensor(s); zero placeholders excluded."
            });
        }
        return result;
    }

    private static async Task CompleteMmfPlantsThroughRawApiAsync(ApplicationDbContext database, string path,
        IReadOnlyList<LssPlant> plants)
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var rangeStart = DateTime.Today.AddDays(-2);
        var parameters = new (string Name, string Unit)[]
        {
            ("AirPressure", "hPa"), ("AmbientTemp", "C"), ("AtmPressure", "atm"),
            ("HorIrradiance", "W/m2"), ("Humidity", "%"), ("IncIrradiance", "W/m2"),
            ("MWPerArea", "MW/m2"), ("PVTemperature", "C"), ("Precipitation", "mm"),
            ("RelativeHumidity", "%"), ("TotalRadiation", "W/m2"),
            ("WindDirection", "degree"), ("WindDirectionStdDev", "degree"), ("WindSpeed", "m/s")
        };
        for (var plantIndex = 0; plantIndex < plants.Count; plantIndex++)
        {
            var plant = plants[plantIndex];
            var number = plantIndex + 1;
            var submission = DateTime.Now;
            var sensorRows = new List<Dictionary<string, string>>();
            for (var interval = 0; interval < 3 * 96; interval++)
            {
                var timestamp = rangeStart.AddMinutes(interval * 15);
                foreach (var (name, unit) in parameters)
                {
                    var row = new Dictionary<string, string>
                    {
                        ["Parameter"] = name, ["Time"] = timestamp.ToString("O"), ["Unit"] = unit
                    };
                    var hour = timestamp.TimeOfDay.TotalHours;
                    var daylight = Math.Max(0, Math.Sin((hour - 6) / 12 * Math.PI));
                    for (var sensor = 1; sensor <= 5; sensor++)
                    {
                        var variation = Math.Sin(interval * .19 + number + sensor) * .03;
                        var value = name switch
                        {
                            "AirPressure" => 1008 + number * .25 + sensor * .08 + variation,
                            "AtmPressure" => .994 + number * .0002 + sensor * .00005 + variation / 100,
                            "AmbientTemp" => 25 + daylight * 8 + number * .08 + variation * 8,
                            "PVTemperature" => 26 + daylight * 22 + number * .1 + variation * 10,
                            "HorIrradiance" => daylight * 850 * (1 + variation),
                            "IncIrradiance" => daylight * 930 * (1 + variation),
                            "TotalRadiation" => daylight * 900 * (1 + variation),
                            "MWPerArea" => daylight * .00012 * (1 + variation),
                            "Humidity" or "RelativeHumidity" => 84 - daylight * 30 + number * .2 + variation * 10,
                            "Precipitation" => interval % 173 is 0 or 1 ? .5 + sensor * .08 : 0,
                            "WindDirection" => (120 + interval * 1.7 + number * 8 + sensor * 3) % 360,
                            "WindDirectionStdDev" => 4 + sensor * .3 + Math.Abs(variation * 10),
                            "WindSpeed" => 2.5 + daylight * 2 + number * .05 + sensor * .08 + variation,
                            _ => 0
                        };
                        row[$"MMF{sensor}"] = Math.Max(0, value).ToString("0.######", CultureInfo.InvariantCulture);
                    }
                    sensorRows.Add(row);
                }
            }
            var payload = JsonSerializer.Serialize(new
            {
                MMFResponse = new { Source = "ChatGPT simulated MMF API", SourceWorkbookSha256 = sourceHash, PlantName = plant.Name, MMFData = sensorRows }
            }, new JsonSerializerOptions { WriteIndented = true });
            var job = new CollectionJobEntity
            {
                CollectionId = $"MMF-API-TEST-{plant.PlantId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                PlantId = plant.Id, DataType = "MMF", Mode = "Simulated MMF API",
                SubmissionDate = DateOnly.FromDateTime(submission), StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow, Status = "Completed"
            };
            database.CollectionJobs.Add(job); await database.SaveChangesAsync();
            database.RawApiResponses.Add(new RawApiResponse
            {
                CollectionJobId = job.Id, ReceivedAt = DateTimeOffset.UtcNow, HttpStatusCode = 200,
                ContentType = "application/json",
                Payload = payload,
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            });
            var readings = ParseMmf(payload, job.Id, plant.Id, submission);
            foreach (var reading in readings)
            {
                reading.IsSynthetic = true;
                reading.SyntheticReason = "Complete 15-minute simulated API dataset based on the supplied MMF contract; five sensor values averaged during translation.";
            }
            database.SolarApiReadings.AddRange(readings); await database.SaveChangesAsync(); database.ChangeTracker.Clear();
            Console.WriteLine($"  {plant.PlantId}: raw API saved, {readings.Count:N0} complete averaged MMF reading(s) translated");
        }
    }

    private static async Task EnsureCompleteTestMatrixAsync(
        ApplicationDbContext database, Dictionary<int, LssPlant> plants)
    {
        string[] dataTypes = ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];
        foreach (var dataType in dataTypes)
        {
            var realReadings = await database.SolarApiReadings.AsNoTracking()
                .Where(x => x.DataType == dataType && !x.IsSynthetic)
                .OrderBy(x => x.IntervalStart).ToListAsync();
            if (realReadings.Count == 0) continue;

            var timeline = realReadings.GroupBy(x => x.IntervalStart)
                .Select(group => new
                {
                    Time = group.Key,
                    Value = group.Average(x => x.Value),
                    Example = group.First()
                }).OrderBy(x => x.Time).ToList();

            foreach (var plant in plants.Values.OrderBy(x => x.PlantId))
            {
                var existingTimes = (await database.SolarApiReadings.AsNoTracking()
                    .Where(x => x.DataType == dataType && x.PlantId == plant.Id)
                    .Select(x => x.IntervalStart).Distinct().ToListAsync()).ToHashSet();
                var missing = timeline.Where(x => !existingTimes.Contains(x.Time)).ToList();
                if (missing.Count == 0) continue;

                var compactType = new string(dataType.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
                var collectionId = $"SYNTH-{compactType}-{plant.PlantId}";
                var job = await database.CollectionJobs.FirstOrDefaultAsync(x => x.CollectionId == collectionId);
                if (job is null)
                {
                    job = new CollectionJobEntity
                    {
                        CollectionId = collectionId, PlantId = plant.Id, DataType = dataType,
                        Mode = "Synthetic test completion",
                        SubmissionDate = DateOnly.FromDateTime(timeline[0].Example.SubmissionDateTime),
                        StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
                        Status = "Completed"
                    };
                    database.CollectionJobs.Add(job);
                    await database.SaveChangesAsync();
                }

                database.SolarApiReadings.AddRange(missing.Select(x => new SolarApiReading
                {
                    CollectionJobId = job.Id, PlantId = plant.Id, DataType = dataType,
                    SubmissionDateTime = x.Example.SubmissionDateTime,
                    IntervalStart = x.Time, IntervalEnd = x.Example.IntervalEnd,
                    IntervalMinutes = x.Example.IntervalMinutes, Parameter = x.Example.Parameter,
                    Value = x.Value, Unit = x.Example.Unit, IsSynthetic = true,
                    SyntheticReason = "Missing test interval filled with the mean of real plant readings at the same timestamp."
                }));
                await database.SaveChangesAsync();
                Console.WriteLine($"  {dataType} / {plant.PlantId}: added {missing.Count:N0} synthetic test reading(s)");
                database.ChangeTracker.Clear();
            }
        }
    }

    private static IEnumerable<(string Name, string Payload)> ReadSheets(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var shared = ReadSharedStrings(archive);
        var workbook = XDocument.Load(archive.GetEntry("xl/workbook.xml")!.Open());
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var relationships = XDocument.Load(archive.GetEntry("xl/_rels/workbook.xml.rels")!.Open());
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var targets = relationships.Descendants(packageRel + "Relationship")
            .ToDictionary(x => (string)x.Attribute("Id")!, x => (string)x.Attribute("Target")!);
        foreach (var sheet in workbook.Descendants(main + "sheet"))
        {
            var name = (string)sheet.Attribute("name")!;
            var target = targets[(string)sheet.Attribute(rel + "id")!].Replace("\\", "/");
            target = target.TrimStart('/');
            if (!target.StartsWith("xl/")) target = "xl/" + target;
            var document = XDocument.Load(archive.GetEntry(target)!.Open());
            var lines = document.Descendants(main + "c").Select(cell =>
            {
                var value = cell.Element(main + "v")?.Value ?? cell.Element(main + "is")?.Value ?? string.Empty;
                return (string?)cell.Attribute("t") == "s" && int.TryParse(value, out var index) ? shared[index] : value;
            });
            yield return (name, string.Join('\n', lines));
        }
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        var document = XDocument.Load(entry.Open());
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(main + "si").Select(x => string.Concat(x.Descendants(main + "t").Select(t => t.Value))).ToList();
    }

    private static bool TryTag(string line, string tag, out string value)
    {
        var match = Regex.Match(line, $@"<(?:\w+:)?{tag}[^>]*>(.*?)</(?:\w+:)?{tag}>", RegexOptions.IgnoreCase);
        value = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        return match.Success;
    }

    private static bool TryJson(string line, string key, out string value)
    {
        var match = Regex.Match(line, $@"[""']{key}[""']\s*:\s*[""']?([^,""'}}]+)", RegexOptions.IgnoreCase);
        value = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        return match.Success;
    }

    private static DateTime? ParseDate(string value) => DateTime.TryParse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result) ? result : null;

    [GeneratedRegex(@"^\s*(\d+)\.?")]
    private static partial Regex SheetNumberRegex();
    [GeneratedRegex(@"(?:<(?:\w+:)?MMF\d+[^>]*>|[""']MMF\d+[""']\s*:\s*[""']?)([-+0-9.Ee]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MmfValueRegex();
}
