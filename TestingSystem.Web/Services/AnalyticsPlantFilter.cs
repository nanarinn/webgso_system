using TestingSystem.Web.Models;

namespace TestingSystem.Web.Services;

public static class AnalyticsPlantFilter
{
    public static string RegionFor(string state) => state switch
    {
        "Kedah" or "Perlis" or "Penang" or "Perak" => "Northern",
        "Selangor" or "Negeri Sembilan" => "Central",
        "Johor" or "Melaka" => "Southern",
        "Pahang" or "Terengganu" or "Kelantan" => "East Coast",
        _ => "Not assigned"
    };

    public static IEnumerable<LssPlant> Match(IEnumerable<LssPlant> plants, HashSet<int> ids, string state, string region = "") =>
        plants.Where(p => p.IsActive && (ids.Count == 0 || ids.Contains(p.Id))
            && (state.Length == 0 || p.State == state)
            && (region.Length == 0 || RegionFor(p.State) == region));
}
