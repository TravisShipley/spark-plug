public static class SparkPlugBootContext
{
    private static GameSessionConfigAsset pendingSessionConfig;

    public static void SetPendingSession(GameSessionConfigAsset config)
    {
        pendingSessionConfig = config;
    }

    public static GameSessionConfigAsset ConsumePendingSession()
    {
        var config = pendingSessionConfig;
        pendingSessionConfig = null;
        return config;
    }

    public static void Clear()
    {
        pendingSessionConfig = null;
    }
}
