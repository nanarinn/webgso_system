using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Database.Entities;
using TestingSystem.Web.Models;

namespace TestingSystem.Web.Data;

public static class DummyApiDataSeeder
{
    private static readonly string[] DataTypes =
        ["Rolling 24 Hour", "Day Ahead", "Week Ahead", "4 Month Ahead"];

    private static readonly (string Name, string Unit)[] MmfParameters =
    [
        ("ActivePowerProduction", "MW"),
        ("AmbientTemp", "°C"),
        ("AtmPressure", "atm"),
        ("GHI", "W/m²"),
        ("POA", "W/m²"),
        ("WindSpeed", "m/s")
    ];

    private static readonly (string Name, string State, decimal Capacity)[] PlantDefinitions =
    [
        ("Northern Star Solar", "Kedah", 50m),
        ("Perak Green Energy", "Perak", 60m),
        ("Selangor Solar Park", "Selangor", 75m),
        ("Seremban Solar Farm", "Negeri Sembilan", 55m),
        ("Johor Sun Power", "Johor", 90m),
        ("Melaka Solar Valley", "Melaka", 65m),
        ("Pahang Renewable Plant", "Pahang", 80m),
        ("Terengganu Solar Coast", "Terengganu", 70m),
        ("Kelantan Solar Field", "Kelantan", 45m),
        ("Penang Solar Hub", "Penang", 58m)
    ];

    public static async Task SeedAsync(ApplicationDbContext database)
    {
        Console.WriteLine("Resetting previous DDQ and MMF test data...");
        await database.ReportDeliveryRecipients.ExecuteDeleteAsync();
        await database.ReportDeliveryAttempts.ExecuteDeleteAsync();
        await database.ReportScheduleRecipients.ExecuteDeleteAsync();
        await database.ReportSchedules.ExecuteDeleteAsync();
        await database.ReportExecutions.ExecuteDeleteAsync();
        await database.DataAnomalies.ExecuteDeleteAsync();
        await database.CollectionSchedules.ExecuteDeleteAsync();
        await database.SolarApiReadings.ExecuteDeleteAsync();
        await database.RawApiResponses.ExecuteDeleteAsync();
        await database.CollectionJobs.ExecuteDeleteAsync();
        await database.AuditEvents.ExecuteDeleteAsync();
        await database.LssPlants.Where(x => x.PlantId.StartsWith("SAMPLE-") || x.PlantId.StartsWith("DUMMY-"))
            .ExecuteDeleteAsync();

        var plants = new List<LssPlant>();
        for (var index = 0; index < PlantDefinitions.Length; index++)
        {
            var definition = PlantDefinitions[index];
            plants.Add(new LssPlant
            {
                PlantId = $"DUMMY-{index + 1:00}", Name = definition.Name,
                State = definition.State, CapacityMw = definition.Capacity,
                DdqApiEndpoint = $"https://simulated-api.local/ddq/DUMMY-{index + 1:00}",
                MmfApiEndpoint = $"https://simulated-api.local/mmf/DUMMY-{index + 1:00}",
                IsActive = true, ApiConnectionStatus = "Simulated API"
            });
        }
        database.LssPlants.AddRange(plants);
        await database.SaveChangesAsync();

        foreach (var plant in plants)
        {
            foreach (var dataType in DataTypes)
                database.PlantApiConfigurations.Add(new PlantApiConfiguration { PlantId = plant.Id, ApiType = "DDQ", DataType = dataType, ApiLink = $"https://simulated-api.local/ddq/{plant.PlantId}/{Uri.EscapeDataString(dataType)}" });
            database.PlantApiConfigurations.Add(new PlantApiConfiguration { PlantId = plant.Id, ApiType = "MMF", DataType = "MMF", ApiLink = $"https://simulated-api.local/mmf/{plant.PlantId}" });
        }
        await database.SaveChangesAsync();

        plants = await database.LssPlants
            .Where(x => x.IsActive && !x.PlantId.StartsWith("SAMPLE-"))
            .OrderBy(x => x.Name).ToListAsync();
        var today = DateTime.Today;
        var firstDay = new DateTime(today.Year, today.Month, Math.Min(13, today.Day));
        var submissionDays = Enumerable.Range(0, (today - firstDay).Days + 1)
            .Select(offset => firstDay.AddDays(offset)).ToList();
        const int periodsPerDay = 96;

        foreach (var plant in plants)
        {
            foreach (var dataType in DataTypes)
            {
                foreach (var day in submissionDays)
                {
                    var submission = day.AddHours(15);
                    var apiResponse = BuildApiResponse(plant, dataType, submission, day, periodsPerDay);
                    var rawJson = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions { WriteIndented = true });
                    var compactType = new string(dataType.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
                    var job = new CollectionJobEntity
                    {
                        CollectionId = $"API-DUMMY-{plant.PlantId}-{compactType}-{day:yyyyMMdd}", PlantId = plant.Id,
                        DataType = dataType, Mode = "Simulated API", SubmissionDate = DateOnly.FromDateTime(submission),
                        SubmissionDateTime = submission,
                        StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow, Status = "Completed"
                    };
                    database.CollectionJobs.Add(job);
                    await database.SaveChangesAsync();

                    database.RawApiResponses.Add(new RawApiResponse
                    {
                        CollectionJobId = job.Id, ReceivedAt = DateTimeOffset.UtcNow, HttpStatusCode = 200,
                        ContentType = "application/json", Payload = rawJson,
                        Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)))
                    });

                    // Translation stage: read the stored API contract and map it to the normalized SQL table.
                    var translated = JsonSerializer.Deserialize<DummyApiResponse>(rawJson)
                        ?? throw new InvalidDataException("The simulated API response could not be translated.");
                    database.SolarApiReadings.AddRange(translated.ForecastData.Select(row => new SolarApiReading
                    {
                        CollectionJobId = job.Id, PlantId = plant.Id, DataType = translated.DataType,
                        SubmissionDateTime = translated.SubmissionDateTime, IntervalStart = row.StartTime,
                        IntervalEnd = row.EndTime, IntervalMinutes = 15,
                        Parameter = "ActivePowerProduction", Value = row.ForecastValue,
                        Unit = row.ValueUnit, ErrorCode = 0, ErrorMessage = "OK", IsSynthetic = true,
                        SyntheticReason = "Generated by the local simulated API for complete daily system testing."
                    }));
                    await database.SaveChangesAsync();
                    database.ChangeTracker.Clear();
                }
                Console.WriteLine($"  {plant.PlantId} / {dataType}: {submissionDays.Count * periodsPerDay:N0} readings across {submissionDays.Count} submission day(s)");
            }
        }

        Console.WriteLine("Generating MMF sensor payloads and averaged readings...");
        foreach (var plant in plants)
        {
            foreach (var day in submissionDays)
            {
                var submission = day.AddHours(23).AddMinutes(45);
                var sensorRows = BuildMmfSensorReadings(plant, day, periodsPerDay);
                var apiResponse = new DummyMmfApiResponse(
                    "ChatGPT Local Simulated MMF API", plant.PlantId, plant.Name, submission, sensorRows);
                var rawJson = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions { WriteIndented = true });
                var job = new CollectionJobEntity
                {
                    CollectionId = $"API-DUMMY-{plant.PlantId}-MMF-{day:yyyyMMdd}", PlantId = plant.Id,
                    DataType = "MMF", Mode = "Simulated API", SubmissionDate = DateOnly.FromDateTime(submission),
                    SubmissionDateTime = submission,
                    StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow, Status = "Completed"
                };
                database.CollectionJobs.Add(job);
                await database.SaveChangesAsync();
                database.RawApiResponses.Add(new RawApiResponse
                {
                    CollectionJobId = job.Id, ReceivedAt = DateTimeOffset.UtcNow, HttpStatusCode = 200,
                    ContentType = "application/json", Payload = rawJson,
                    Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)))
                });

                // Translation stage: multiple physical sensors for the same parameter and timestamp
                // are averaged into one MMF reading per plant, parameter and 15-minute interval.
                var translated = JsonSerializer.Deserialize<DummyMmfApiResponse>(rawJson)
                    ?? throw new InvalidDataException("The simulated MMF API response could not be translated.");
                var averaged = translated.SensorReadings
                    .GroupBy(x => new { x.Timestamp, x.Parameter, x.Unit })
                    .Select(group => new SolarApiReading
                    {
                        CollectionJobId = job.Id, PlantId = plant.Id, DataType = "MMF",
                        SubmissionDateTime = translated.SubmissionDateTime,
                        IntervalStart = group.Key.Timestamp, IntervalEnd = group.Key.Timestamp.AddMinutes(15),
                        IntervalMinutes = 15, Parameter = group.Key.Parameter,
                        Value = decimal.Round(group.Average(x => x.Value), 6), Unit = group.Key.Unit,
                        ErrorCode = 0, ErrorMessage = "OK", IsSynthetic = true,
                        SyntheticReason = $"Average of {group.Count()} simulated MMF sensors."
                    });
                database.SolarApiReadings.AddRange(averaged);
                await database.SaveChangesAsync();
                database.ChangeTracker.Clear();
            }
            Console.WriteLine($"  {plant.PlantId} / MMF: {submissionDays.Count * periodsPerDay * MmfParameters.Length:N0} averaged readings across {submissionDays.Count} day(s)");
        }

        await SeedRelatedWorkflowAsync(database, plants, today);
    }

    private static async Task SeedRelatedWorkflowAsync(ApplicationDbContext database, List<LssPlant> plants, DateTime today)
    {
        foreach (var plant in plants.Take(4))
        {
            database.CollectionSchedules.Add(new CollectionSchedule
            {
                PlantId = plant.Id, ScheduleCode = $"SCH-DDQ-{plant.PlantId}", Name = $"{plant.Name} Daily DDQ",
                ApiType = "DDQ", DataType = "Rolling 24 Hour", Interval = "15 minutes", RunTime = "00:15",
                CronExpression = "*/15 * * * *", TimeZoneId = "Asia/Kuala_Lumpur", RetryCount = 3, IsActive = true
            });
            database.CollectionSchedules.Add(new CollectionSchedule
            {
                PlantId = plant.Id, ScheduleCode = $"SCH-MMF-{plant.PlantId}", Name = $"{plant.Name} MMF",
                ApiType = "MMF", DataType = "MMF", Interval = "15 minutes", RunTime = "00:00",
                CronExpression = "*/15 * * * *", TimeZoneId = "Asia/Kuala_Lumpur", RetryCount = 3, IsActive = true
            });
        }

        var reportSchedules = new[]
        {
            new ReportScheduleEntity { ScheduleCode = "RSC-DDQ-DAILY", Name = "Daily DDQ Operations Report", ApiType = "DDQ", DataType = "Rolling 24 Hour", IntervalMinutes = 15, ScheduleType = "Daily", ExecutionTime = "07:00", IsActive = true },
            new ReportScheduleEntity { ScheduleCode = "RSC-MMF-DAILY", Name = "Daily MMF Monitoring Report", ApiType = "MMF", DataType = "All MMF parameters", IntervalMinutes = 15, ScheduleType = "Daily", ExecutionTime = "07:15", IsActive = true }
        };
        foreach (var schedule in reportSchedules)
        {
            schedule.Recipients.Add(new ReportScheduleRecipient { Email = "operations@sblss.local" });
            schedule.Recipients.Add(new ReportScheduleRecipient { Email = "dataquality@sblss.local" });
            database.ReportSchedules.Add(schedule);
        }
        await database.SaveChangesAsync();

        foreach (var schedule in reportSchedules)
        {
            var delivery = new ReportDeliveryAttempt
            {
                DeliveryCode = $"RDL-{schedule.ApiType}-{today:yyyyMMdd}", ReportScheduleId = schedule.Id,
                ScheduleName = schedule.Name, ApiType = schedule.ApiType, GeneratedAt = today.AddHours(7),
                ReportStatus = "Generated", EmailStatus = "Sent", OverallStatus = "Delivered",
                Message = "Dummy workflow completed from PostgreSQL source data and delivered to all configured recipients."
            };
            foreach (var recipient in schedule.Recipients) delivery.Recipients.Add(new ReportDeliveryRecipient { Email = recipient.Email });
            database.ReportDeliveryAttempts.Add(delivery);
            database.ReportExecutions.Add(new ReportExecution
            {
                ReportId = $"RPT-{schedule.ApiType}-{today:yyyyMMdd}", ReportType = "Scheduled", DataType = schedule.ApiType,
                RequestedBy = "System Scheduler", RequestedAt = today.AddHours(7), GenerationStatus = "Completed", DeliveryStatus = "Sent",
                Name = schedule.Name, SubmissionDate = today.AddDays(-1), DataSelection = schedule.DataType,
                Description = $"{schedule.ApiType} report generated from all active LSS plant data in PostgreSQL.",
                PlantCount = plants.Count, RecordCount = schedule.ApiType == "MMF" ? plants.Count * 96 * MmfParameters.Length : plants.Count * 96
            });
        }

        var sampleDdqJob = await database.CollectionJobs.Where(x => x.DataType != "MMF").OrderBy(x => x.Id).FirstAsync();
        var sampleMmfJob = await database.CollectionJobs.Where(x => x.DataType == "MMF").OrderBy(x => x.Id).FirstAsync();
        database.DataAnomalies.AddRange(
            new DataAnomaly { CollectionJobId = sampleDdqJob.Id, DataType = "DDQ", FieldName = "Forecast value", OriginalValue = "Missing", Reason = "Expected interval reading was not supplied.", Status = "Open", DetectedAt = DateTimeOffset.Now.AddHours(-2) },
            new DataAnomaly { CollectionJobId = sampleMmfJob.Id, DataType = "MMF", FieldName = "GHI", OriginalValue = "1455.20", Reason = "Irradiance exceeded the configured 1,400 W/m² limit.", Status = "Open", DetectedAt = DateTimeOffset.Now.AddHours(-1) });
        database.AuditEvents.AddRange(
            new AuditEvent { OccurredAt = DateTimeOffset.Now.AddHours(-3), Actor = "System Scheduler", Action = "Collect DDQ and MMF data", EntityType = "CollectionJob", EntityReference = today.ToString("yyyy-MM-dd"), Result = "Success", Detail = "Completed the simulated API collection and normalization flow for all active LSS plants." },
            new AuditEvent { OccurredAt = DateTimeOffset.Now.AddHours(-2), Actor = "Data Quality Engine", Action = "Validate collected data", EntityType = "DataAnomaly", EntityReference = "DDQ/MMF", Result = "Warning", Detail = "Applied the fixed DDQ and MMF validation rules and recorded two review examples." },
            new AuditEvent { OccurredAt = DateTimeOffset.Now.AddHours(-1), Actor = "Report Scheduler", Action = "Generate and deliver reports", EntityType = "ReportExecution", EntityReference = today.ToString("yyyy-MM-dd"), Result = "Success", Detail = "Generated DDQ and MMF reports from PostgreSQL data and recorded recipient delivery results." });
        await database.SaveChangesAsync();
    }

    private static DummyApiResponse BuildApiResponse(
        LssPlant plant, string dataType, DateTime submission, DateTime rangeStart, int periods)
    {
        var typeFactor = dataType switch
        {
            "Day Ahead" => 0.97,
            "Week Ahead" => 0.93,
            "4 Month Ahead" => 0.88,
            _ => 1.0
        };
        var readings = Enumerable.Range(0, periods).Select(index =>
        {
            var timestamp = rangeStart.AddMinutes(index * 15);
            var hour = timestamp.TimeOfDay.TotalHours;
            var daylight = Math.Max(0, Math.Sin((hour - 6) / 12 * Math.PI));
            var variation = Math.Sin(index * 0.47 + plant.Id) * 1.8;
            var value = Math.Clamp(daylight * (double)plant.CapacityMw * 0.84 * typeFactor + variation,
                0, (double)plant.CapacityMw);
            return new DummyApiReading(timestamp, timestamp.AddMinutes(15), Math.Round((decimal)value, 2), "MW");
        }).ToList();
        return new DummyApiResponse("ChatGPT Local Simulated Solar API", plant.PlantId,
            plant.Name, dataType, submission, readings);
    }

    private sealed record DummyApiResponse(string Source, string PlantId, string PlantName,
        string DataType, DateTime SubmissionDateTime, List<DummyApiReading> ForecastData);
    private sealed record DummyApiReading(DateTime StartTime, DateTime EndTime,
        decimal ForecastValue, string ValueUnit);

    private static List<DummyMmfSensorReading> BuildMmfSensorReadings(
        LssPlant plant, DateTime rangeStart, int periods)
    {
        var rows = new List<DummyMmfSensorReading>(periods * MmfParameters.Length * 3);
        for (var index = 0; index < periods; index++)
        {
            var timestamp = rangeStart.AddMinutes(index * 15);
            var hour = timestamp.TimeOfDay.TotalHours;
            var daylight = Math.Max(0, Math.Sin((hour - 6) / 12 * Math.PI));
            foreach (var parameter in MmfParameters)
            {
                for (var sensor = 1; sensor <= 3; sensor++)
                {
                    var variation = Math.Sin(index * 0.31 + plant.Id * 0.17 + sensor) * 0.8;
                    var value = parameter.Name switch
                    {
                        "ActivePowerProduction" => Math.Clamp(daylight * (double)plant.CapacityMw * 0.82 + variation, 0, (double)plant.CapacityMw),
                        "AmbientTemp" => 25 + daylight * 9 + variation,
                        "AtmPressure" => 1.01 + variation * 0.004,
                        "GHI" => Math.Max(0, daylight * 920 + variation * 12),
                        "POA" => Math.Max(0, daylight * 1010 + variation * 14),
                        "WindSpeed" => Math.Max(0, 2.8 + Math.Sin(index * 0.19) * 1.4 + variation),
                        _ => 0
                    };
                    rows.Add(new DummyMmfSensorReading($"{plant.PlantId}-{parameter.Name}-S{sensor}",
                        parameter.Name, timestamp, Math.Round((decimal)value, 6), parameter.Unit));
                }
            }
        }
        return rows;
    }

    private sealed record DummyMmfApiResponse(string Source, string PlantId, string PlantName,
        DateTime SubmissionDateTime, List<DummyMmfSensorReading> SensorReadings);
    private sealed record DummyMmfSensorReading(string SensorId, string Parameter,
        DateTime Timestamp, decimal Value, string Unit);
}
