namespace TestingSystem.Web.Configuration;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = "webgso_system";
    public string User { get; set; } = "webgso_app";
}
