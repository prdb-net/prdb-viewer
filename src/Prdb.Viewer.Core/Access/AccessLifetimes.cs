namespace Prdb.Viewer.Core.Access;

public static class AccessLifetimes
{
    public static TimeSpan BootstrapAuthorization { get; } = TimeSpan.FromMinutes(30);

    public static TimeSpan RecoveryCode { get; } = TimeSpan.FromMinutes(30);

    public static TimeSpan Session { get; } = TimeSpan.FromDays(30);
}
