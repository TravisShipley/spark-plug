using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameSessionCatalog", menuName = "SparkPlug/Boot/Game Session Catalog")]
public sealed class GameSessionCatalog : ScriptableObject
{
    [SerializeField]
    private GameSessionConfigAsset[] sessions = Array.Empty<GameSessionConfigAsset>();

    public IReadOnlyList<GameSessionConfigAsset> Sessions => sessions ?? Array.Empty<GameSessionConfigAsset>();

    public bool TryGet(string sessionId, out GameSessionConfigAsset config)
    {
        config = null;
        var normalizedSessionId = Normalize(sessionId);
        if (string.IsNullOrEmpty(normalizedSessionId) || sessions == null)
            return false;

        for (int i = 0; i < sessions.Length; i++)
        {
            var entry = sessions[i];
            if (
                entry != null
                && string.Equals(entry.SessionId, normalizedSessionId, StringComparison.Ordinal)
            )
            {
                config = entry;
                return true;
            }
        }

        return false;
    }

    public IEnumerable<GameSessionConfigAsset> EnumerateValidSessions()
    {
        if (sessions == null)
            yield break;

        for (int i = 0; i < sessions.Length; i++)
        {
            var entry = sessions[i];
            if (entry != null)
                yield return entry;
        }
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
