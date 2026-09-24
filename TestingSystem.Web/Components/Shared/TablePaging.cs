namespace TestingSystem.Web.Components.Shared;

public static class TablePaging
{
    public const int DefaultPageSize = 20;

    public static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> items, int page, int pageSize = DefaultPageSize)
    {
        if (items.Count == 0) return [];
        var safeSize = Math.Max(1, pageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)safeSize));
        var safePage = Math.Clamp(page, 1, totalPages);
        return items.Skip((safePage - 1) * safeSize).Take(safeSize).ToList();
    }
}
