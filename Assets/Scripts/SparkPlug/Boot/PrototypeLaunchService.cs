using UnityEngine;
using UnityEngine.SceneManagement;

public static class PrototypeLaunchService
{
    public const string DefaultBootstrapSceneName = "Bootstrap";

    public static void Launch(GameSessionConfigAsset config)
    {
        if (config == null)
        {
            Debug.LogError("PrototypeLaunchService: session config is null.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.SceneName))
        {
            Debug.LogError(
                $"PrototypeLaunchService: session '{config.name}' is missing a target scene name."
            );
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(config.SceneName))
        {
            Debug.LogError(
                $"PrototypeLaunchService: scene '{config.SceneName}' is not available to load for session '{config.SessionId}'."
            );
            return;
        }

        SparkPlugBootContext.SetPendingSession(config);
        SceneManager.LoadScene(config.SceneName);
    }

    public static void ReturnToBootstrap(string sceneName = DefaultBootstrapSceneName)
    {
        var normalizedSceneName = string.IsNullOrWhiteSpace(sceneName)
            ? DefaultBootstrapSceneName
            : sceneName.Trim();
        if (!Application.CanStreamedLevelBeLoaded(normalizedSceneName))
        {
            Debug.LogError(
                $"PrototypeLaunchService: bootstrap scene '{normalizedSceneName}' is not available to load."
            );
            return;
        }

        SparkPlugBootContext.Clear();
        SceneManager.LoadScene(normalizedSceneName);
    }
}
