using TestingSystem.Web.Models;
using TestingSystem.Web.Services;

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
