using System;
using UnityEngine;

[DefaultExecutionOrder(-900)]
public sealed class GameSessionBootstrapper : MonoBehaviour
{
    [Header("Session")]
    [SerializeField]
    private GameSessionConfigAsset defaultSessionConfig;

    [Header("Runtime")]
    [SerializeField]
    private GameCompositionRoot compositionRoot;

    private void Awake()
    {
        if (!TryResolveCompositionRoot(out var root))
        {
            enabled = false;
            return;
        }

        var effectiveConfig = ResolveEffectiveConfig();
        if (effectiveConfig == null)
        {
            Debug.LogError(
                "GameSessionBootstrapper: No session config was provided. Assign a default session config or launch through PrototypeLaunchService.",
                this
            );
            enabled = false;
            return;
        }

        try
        {
            var request = effectiveConfig.ToRequest();
            var definition = GameDefinitionLoader.LoadFromJsonText(
                request.DefinitionJson,
                $"TextAsset '{effectiveConfig.GameDefinitionJson.name}' for session '{request.SessionId}'"
            );
            var runtimeConfig = new SparkPlugRuntimeConfig(
                request.SessionId,
                request.DisplayName,
                request.SaveSlotId,
                request.ResetSaveOnBoot,
                request.VerboseLogging,
                definition
            );

            if (runtimeConfig.VerboseLogging)
            {
                Debug.Log(
                    $"GameSessionBootstrapper: Booting session '{runtimeConfig.SessionId}' into scene '{gameObject.scene.name}'.",
                    this
                );
            }

            root.BeginBootstrap(runtimeConfig, effectiveConfig);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
            enabled = false;
        }
    }

    private GameSessionConfigAsset ResolveEffectiveConfig()
    {
        return SparkPlugBootContext.ConsumePendingSession() ?? defaultSessionConfig;
    }

    private bool TryResolveCompositionRoot(out GameCompositionRoot root)
    {
        root = compositionRoot;
        if (root != null)
            return true;

        root = GetComponent<GameCompositionRoot>();
        if (root != null)
        {
            compositionRoot = root;
            return true;
        }

        Debug.LogError(
            "GameSessionBootstrapper: GameCompositionRoot is not assigned and could not be found on the same GameObject.",
            this
        );
        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (compositionRoot == null)
            compositionRoot = GetComponent<GameCompositionRoot>();
    }
#endif
}
