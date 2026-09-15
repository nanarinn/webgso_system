namespace TestingSystem.Web.Models;

public sealed class PlantApiConfiguration
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public LssPlant Plant { get; set; } = null!;
    public string ApiType { get; set; } = "DDQ";
    public string DataType { get; set; } = string.Empty;
    public string ApiLink { get; set; } = string.Empty;
}
