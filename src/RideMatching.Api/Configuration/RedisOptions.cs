namespace RideMatching.Api.Configuration;

/// <summary>Redis connection configuration bound from the "Redis" section.</summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// StackExchange.Redis connection string, e.g. "localhost:6379".
    /// Bound from configuration; no hard-coded fallback (startup fails fast if missing).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
