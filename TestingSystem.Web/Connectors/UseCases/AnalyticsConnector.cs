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
        Match(plants, ids,
            string.IsNullOrWhiteSpace(state) ? [] : new HashSet<string>([state], StringComparer.OrdinalIgnoreCase),
            string.IsNullOrWhiteSpace(region) ? [] : new HashSet<string>([region], StringComparer.OrdinalIgnoreCase));

    public static IEnumerable<LssPlant> Match(IEnumerable<LssPlant> plants, HashSet<int> ids,
        IReadOnlySet<string> states, IReadOnlySet<string> regions) =>
        plants.Where(plant => plant.IsActive && (ids.Count == 0 || ids.Contains(plant.Id))
            && (states.Count == 0 || states.Contains(plant.State))
            && (regions.Count == 0 || regions.Contains(RegionFor(plant.State))));
}
