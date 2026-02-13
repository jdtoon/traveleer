namespace saas.Infrastructure.Services;

internal static class DurationParser
{
    public static TimeSpan ParseOrDefault(string? value, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().ToLowerInvariant();
        if (TimeSpan.TryParse(trimmed, out var ts))
            return ts;

        if (trimmed.EndsWith("ms") && double.TryParse(trimmed[..^2], out var millis))
            return TimeSpan.FromMilliseconds(millis);

        if (trimmed.EndsWith("s") && double.TryParse(trimmed[..^1], out var seconds))
            return TimeSpan.FromSeconds(seconds);

        if (trimmed.EndsWith("m") && double.TryParse(trimmed[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);

        if (trimmed.EndsWith("h") && double.TryParse(trimmed[..^1], out var hours))
            return TimeSpan.FromHours(hours);

        return fallback;
    }
}
