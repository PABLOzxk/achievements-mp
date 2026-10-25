namespace AchievementsMP.Data;

internal static class Time
{
    public static long? ToUnix(DateTimeOffset? value) => value?.ToUnixTimeSeconds();
    public static long ToUnix(DateTimeOffset value) => value.ToUnixTimeSeconds();
    public static DateTimeOffset? FromUnix(long? value) => value is { } v ? DateTimeOffset.FromUnixTimeSeconds(v) : null;
}
