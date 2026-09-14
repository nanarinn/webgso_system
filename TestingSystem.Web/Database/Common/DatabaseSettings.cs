namespace TestingSystem.Web.Configuration;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 1433;
    public string Name { get; set; } = "TestingSystemDb";
    public string User { get; set; } = "sa";
    public bool TrustServerCertificate { get; set; } = true;
}
