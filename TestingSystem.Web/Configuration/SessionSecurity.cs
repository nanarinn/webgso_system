namespace TestingSystem.Web.Configuration;

public static class SessionSecurity
{
    public const int IdleTimeoutMinutes = 15;
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(IdleTimeoutMinutes);
}
