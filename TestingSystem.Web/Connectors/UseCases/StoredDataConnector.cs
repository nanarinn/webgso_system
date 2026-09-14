using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TestingSystem.Web.Data;
using TestingSystem.Web.Database.Entities;

namespace TestingSystem.Web.Services;

public interface IStoredDataService
{
    Task<List<T>> LoadAsync<T>(string category, CancellationToken cancellationToken = default);
    Task SaveAsync<T>(string category, string key, T value, CancellationToken cancellationToken = default);
    Task SeedAsync<T>(string category, IEnumerable<(string Key, T Value)> records,
        CancellationToken cancellationToken = default);
}

public sealed class StoredDataService(IDbContextFactory<ApplicationDbContext> contextFactory) : IStoredDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<T>> LoadAsync<T>(string category, CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var values = await database.StoredDataRecords.AsNoTracking()
            .Where(x => x.Category == category).OrderByDescending(x => x.UpdatedAt)
            .Select(x => x.JsonData).ToListAsync(cancellationToken);
        return values.Select(value => JsonSerializer.Deserialize<T>(value, JsonOptions))
            .Where(value => value is not null).Cast<T>().ToList();
    }

    public async Task SaveAsync<T>(string category, string key, T value, CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await database.StoredDataRecords.SingleOrDefaultAsync(
            x => x.Category == category && x.RecordKey == key, cancellationToken);
        if (record is null)
        {
            record = new StoredDataRecord { Category = category, RecordKey = key };
            database.StoredDataRecords.Add(record);
        }
        record.JsonData = JsonSerializer.Serialize(value, JsonOptions);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedAsync<T>(string category, IEnumerable<(string Key, T Value)> records,
        CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await database.StoredDataRecords.AnyAsync(x => x.Category == category, cancellationToken)) return;
        database.StoredDataRecords.AddRange(records.Select(record => new StoredDataRecord
        {
            Category = category,
            RecordKey = record.Key,
            JsonData = JsonSerializer.Serialize(record.Value, JsonOptions)
        }));
        await database.SaveChangesAsync(cancellationToken);
    }
}
