public static class SparkPlugSaveKey
{
    public const string Prefix = "sparkplug";
    public const string DefaultSessionId = "default";
    public const string DefaultSaveSlotId = "default";

    public static string Compose(string sessionId, string saveSlotId)
    {
        var normalizedSessionId = NormalizeSegment(sessionId, DefaultSessionId);
        var normalizedSaveSlotId = NormalizeSegment(saveSlotId, DefaultSaveSlotId);
        return $"{Prefix}.{normalizedSessionId}.{normalizedSaveSlotId}";
    }

    private static string NormalizeSegment(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
