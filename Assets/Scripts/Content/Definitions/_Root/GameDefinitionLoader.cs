using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class GameDefinitionLoader
{
    public const string DefaultFilePath = "Assets/Data/game_definition.json";
    public const string DefaultAddressableKey = "game_definition";
    public const string DefaultResourcesFallbackKey = "Data/game_definition";

    public static GameDefinition LoadFromJsonText(string json, string source = "runtime JSON")
    {
        return LoadFromJson(json, source);
    }

    public static GameDefinition LoadFromFile(string projectRelativePath = DefaultFilePath)
    {
        var full = Path.GetFullPath(projectRelativePath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Content definition file not found: {full}");

        var json = File.ReadAllText(full);
        return LoadFromJson(json, $"file '{full}'");
    }

    public static GameDefinition LoadFromAddressable(
        string addressableKey = DefaultAddressableKey,
        string editorFallbackProjectRelativePath = DefaultFilePath
    )
    {
        var key = (addressableKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Addressable key is required for the content definition.");

#if UNITY_WEBGL
        // WebGL cannot do synchronous Addressables loading (WaitForCompletion).
        // Prefer a Resources TextAsset fallback for sync boot, otherwise require async boot.
        if (TryLoadJsonFromResources(DefaultResourcesFallbackKey, out var resourcesJson))
            return LoadFromJson(resourcesJson, $"Resources '{DefaultResourcesFallbackKey}'");

        // Optional: allow key-based Resources fallback too.
        if (TryLoadJsonFromResources(key, out resourcesJson))
            return LoadFromJson(resourcesJson, $"Resources '{key}'");

        throw new InvalidOperationException(
            $"Failed to load content definition '{key}' in WebGL. "
                + "WebGL does not support synchronous Addressables loading. "
                + "Use GameDefinitionLoader.LoadFromAddressableAsync(...) (coroutine) or provide a Resources TextAsset fallback."
        );
#else
        if (TryLoadJsonFromAddressables(key, out var json, out var loadError))
            return LoadFromJson(json, $"addressable '{key}'");

        // Optional sync fallback (useful for CI/local builds where Addressables content isn't built).
        if (TryLoadJsonFromResources(DefaultResourcesFallbackKey, out var resourcesJson2))
            return LoadFromJson(resourcesJson2, $"Resources '{DefaultResourcesFallbackKey}'");

#if UNITY_EDITOR
        Debug.LogWarning(
            $"GameDefinitionLoader: failed to load addressable '{key}'. {loadError} Falling back to '{editorFallbackProjectRelativePath}'."
        );
        return LoadFromFile(editorFallbackProjectRelativePath);
#else
        throw new InvalidOperationException(
            $"Failed to load content definition addressable '{key}'. {loadError}"
        );
#endif
#endif
    }

    /// <summary>
    /// WebGL-safe Addressables loader. This yields until the Addressables operation completes (no WaitForCompletion).
    ///
    /// Usage:
    /// StartCoroutine(GameDefinitionLoader.LoadFromAddressableAsync(
    ///     gd => { /* use gd */ },
    ///     ex => Debug.LogException(ex)
    /// ));
    /// </summary>
    public static System.Collections.IEnumerator LoadFromAddressableAsync(
        Action<GameDefinition> onLoaded,
        Action<Exception> onError,
        string addressableKey = DefaultAddressableKey,
        string resourcesFallbackKey = DefaultResourcesFallbackKey
    )
    {
        if (onLoaded == null)
            throw new ArgumentNullException(nameof(onLoaded));

        var key = (addressableKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key))
        {
            onError?.Invoke(
                new InvalidOperationException("Addressable key is required for the content definition.")
            );
            yield break;
        }

        // First: try Addressables asynchronously (works on WebGL).
        yield return TryLoadJsonFromAddressablesAsync(
            key,
            json =>
            {
                try
                {
                    var gd = LoadFromJson(json, $"addressable '{key}'");
                    onLoaded(gd);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                }
            },
            err =>
            {
                // Second: Resources fallback (sync).
                if (TryLoadJsonFromResources(resourcesFallbackKey, out var resourcesJson))
                {
                    try
                    {
                        var gd = LoadFromJson(resourcesJson, $"Resources '{resourcesFallbackKey}'");
                        onLoaded(gd);
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex);
                    }

                    return;
                }

                onError?.Invoke(
                    new InvalidOperationException(
                        $"Failed to load content definition addressable '{key}'. {err}"
                    )
                );
            }
        );
    }

    private static GameDefinition LoadFromJson(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Content definition JSON is empty ({source}).");
        try
        {
            // UnityEngine.JsonUtility expects the JSON to map to the class.
            var gd = JsonUtility.FromJson<GameDefinition>(json);
            if (gd == null)
                throw new InvalidOperationException("Failed to deserialize content definition JSON.");

            // Fail loud on missing required roots.
            if (gd.nodes == null || gd.nodes.Count == 0)
                throw new InvalidOperationException("Content definition: 'nodes' is missing or empty.");

            if (gd.nodeInstances == null || gd.nodeInstances.Count == 0)
                throw new InvalidOperationException(
                    "Content definition: 'nodeInstances' is missing or empty."
                );

            // Optional roots (may be empty, but should not be null if present in schema).
            if (gd.upgrades == null)
                gd.upgrades = new List<UpgradeDefinition>();

            if (gd.modifiers == null)
                gd.modifiers = new List<ModifierDefinition>();

            if (gd.nodeInputs == null)
                gd.nodeInputs = new List<NodeInputDefinition>();

            if (gd.unlockGraph == null)
                gd.unlockGraph = new List<UnlockGraphDefinition>();

            if (gd.milestones == null)
                gd.milestones = new List<MilestoneDefinition>();

            if (gd.buffs == null)
                gd.buffs = new List<BuffDefinition>();

            if (gd.buyModes == null)
                gd.buyModes = new List<BuyModeDefinition>();

            if (gd.triggers == null)
                gd.triggers = new List<TriggerDefinition>();

            if (gd.rewardPools == null)
                gd.rewardPools = new List<RewardPoolDefinition>();

            if (gd.computedVars == null)
                gd.computedVars = new List<ComputedVarDefinition>();

            NormalizeBuffEffects(gd.buffs);
            NormalizeParameterizedPaths(gd);

            GameDefinitionValidator.Validate(gd);
            return gd;
        }
        catch (Exception ex)
        {
            var wrapped = new InvalidOperationException(
                $"Failed to parse/validate content definition JSON from {source}: {ex.Message}",
                ex
            );

            Debug.LogError(wrapped.Message);

#if UNITY_EDITOR
            // Fail loud while iterating on content: stop entering play mode on invalid packs.
            if (UnityEditor.EditorApplication.isPlaying)
                UnityEditor.EditorApplication.isPlaying = false;
#endif

            throw wrapped;
        }
    }

    private static bool TryLoadJsonFromResources(string keyOrPath, out string json)
    {
        json = null;

        var raw = (keyOrPath ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return false;

        // Resources.Load expects a path without extension.
        // We try a few reasonable variants so users can drop a TextAsset into Resources.
        var candidates = new List<string>();
        candidates.Add(raw);

        // Strip leading slashes.
        if (raw.StartsWith("/", StringComparison.Ordinal))
            candidates.Add(raw.TrimStart('/'));

        // Strip extension if present.
        if (raw.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            candidates.Add(raw.Substring(0, raw.Length - ".json".Length));

        // Common convention: Resources/Data/game_definition.json -> key "Data/game_definition".
        if (string.Equals(raw, DefaultAddressableKey, StringComparison.Ordinal))
            candidates.Add("Data/game_definition");

        for (int i = 0; i < candidates.Count; i++)
        {
            var path = candidates[i];
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var ta = Resources.Load<TextAsset>(path);
            if (ta == null)
                continue;

            var txt = ta.text;
            if (string.IsNullOrWhiteSpace(txt))
                continue;

            json = txt;
            return true;
        }

        return false;
    }

    private static bool TryLoadJsonFromAddressables(
        string addressableKey,
        out string json,
        out string error
    )
    {
        json = null;
        error = null;

#if UNITY_WEBGL
        error =
            "WebGLPlayer does not support synchronous Addressable loading (WaitForCompletion). Use LoadFromAddressableAsync(...) instead.";
        return false;
#else
        var addressablesType = Type.GetType(
            "UnityEngine.AddressableAssets.Addressables, Unity.Addressables"
        );

        if (addressablesType == null)
        {
            error = "Addressables package is not available.";
            return false;
        }

        var loadMethod = addressablesType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m =>
                string.Equals(m.Name, "LoadAssetAsync", StringComparison.Ordinal)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1
            )
            // Prefer the overload: LoadAssetAsync<T>(object key)
            .OrderByDescending(m =>
            {
                var p = m.GetParameters()[0].ParameterType;
                if (p == typeof(object))
                    return 3;
                if (p == typeof(string))
                    return 2;
                if (
                    string.Equals(
                        p.FullName,
                        "UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation",
                        StringComparison.Ordinal
                    )
                )
                    return 0;
                return 1;
            })
            .FirstOrDefault();
        if (loadMethod == null)
        {
            error = "Addressables.LoadAssetAsync<T>(object key) overload was not found.";
            return false;
        }

        object handle = null;
        try
        {
            try
            {
                handle = loadMethod
                    .MakeGenericMethod(typeof(TextAsset))
                    .Invoke(null, new object[] { (object)addressableKey });
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException;
                error =
                    $"Addressables.LoadAssetAsync invocation threw: {inner?.GetType().Name ?? "TargetInvocationException"}: {inner?.Message ?? tie.Message}";
                return false;
            }
            if (handle == null)
            {
                error = "Addressables returned a null operation handle.";
                return false;
            }

            var handleType = handle.GetType();

            var waitMethod = handleType.GetMethod(
                "WaitForCompletion",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (waitMethod != null)
            {
                try
                {
                    waitMethod.Invoke(handle, null);
                }
                catch (TargetInvocationException tie)
                {
                    var inner = tie.InnerException;
                    error =
                        $"Addressables.WaitForCompletion threw: {inner?.GetType().Name ?? "TargetInvocationException"}: {inner?.Message ?? tie.Message}";
                    return false;
                }
            }

            var status = handleType
                .GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(handle)
                ?.ToString();
            if (!string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var operationException =
                    handleType
                        .GetProperty(
                            "OperationException",
                            BindingFlags.Public | BindingFlags.Instance
                        )
                        ?.GetValue(handle) as Exception;
                error =
                    operationException?.Message
                    ?? $"Addressables load status was '{status ?? "Unknown"}'.";
                return false;
            }

            var textAsset =
                handleType
                    .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(handle) as TextAsset;
            if (textAsset == null)
            {
                error = "Loaded addressable is not a TextAsset.";
                return false;
            }

            json = textAsset.text;
            return true;
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException;
            error =
                $"Invocation threw: {inner?.GetType().Name ?? "TargetInvocationException"}: {inner?.Message ?? tie.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
        finally
        {
            ReleaseAddressablesHandle(addressablesType, handle);
        }
#endif
    }

    private static System.Collections.IEnumerator TryLoadJsonFromAddressablesAsync(
        string addressableKey,
        Action<string> onLoaded,
        Action<string> onError
    )
    {
        var key = (addressableKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key))
        {
            onError?.Invoke("Addressable key is empty.");
            yield break;
        }

        var addressablesType = Type.GetType(
            "UnityEngine.AddressableAssets.Addressables, Unity.Addressables"
        );
        if (addressablesType == null)
        {
            onError?.Invoke("Addressables package is not available.");
            yield break;
        }

        var loadMethod = addressablesType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m =>
                string.Equals(m.Name, "LoadAssetAsync", StringComparison.Ordinal)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1
            )
            // Prefer the overload: LoadAssetAsync<T>(object key)
            .OrderByDescending(m =>
            {
                var p = m.GetParameters()[0].ParameterType;
                if (p == typeof(object))
                    return 3;
                if (p == typeof(string))
                    return 2;
                if (
                    string.Equals(
                        p.FullName,
                        "UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation",
                        StringComparison.Ordinal
                    )
                )
                    return 0;
                return 1;
            })
            .FirstOrDefault();

        if (loadMethod == null)
        {
            onError?.Invoke("Addressables.LoadAssetAsync<T>(object key) overload was not found.");
            yield break;
        }

        object handle = null;
        Type handleType = null;
        PropertyInfo isDoneProp = null;

        try
        {
            handle = loadMethod
                .MakeGenericMethod(typeof(TextAsset))
                .Invoke(null, new object[] { (object)key });
            if (handle == null)
            {
                onError?.Invoke("Addressables returned a null operation handle.");
                yield break;
            }

            handleType = handle.GetType();
            isDoneProp = handleType.GetProperty(
                "IsDone",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (isDoneProp == null)
            {
                onError?.Invoke("Addressables handle did not expose IsDone.");
                yield break;
            }
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException;
            onError?.Invoke(
                $"Addressables.LoadAssetAsync invocation threw: {inner?.GetType().Name ?? "TargetInvocationException"}: {inner?.Message ?? tie.Message}"
            );
            yield break;
        }
        catch (Exception ex)
        {
            onError?.Invoke($"{ex.GetType().Name}: {ex.Message}");
            yield break;
        }

        try
        {
            while (true)
            {
                var isDoneObj = isDoneProp.GetValue(handle);
                var isDone = isDoneObj is bool b && b;
                if (isDone)
                    break;

                yield return null;
            }

            var status = handleType
                .GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(handle)
                ?.ToString();

            if (!string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var operationException =
                    handleType
                        .GetProperty(
                            "OperationException",
                            BindingFlags.Public | BindingFlags.Instance
                        )
                        ?.GetValue(handle) as Exception;

                onError?.Invoke(
                    operationException?.Message
                        ?? $"Addressables load status was '{status ?? "Unknown"}'."
                );
                yield break;
            }

            var textAsset =
                handleType
                    .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(handle) as TextAsset;

            if (textAsset == null)
            {
                onError?.Invoke("Loaded addressable is not a TextAsset.");
                yield break;
            }

            var json = textAsset.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                onError?.Invoke("Loaded TextAsset was empty.");
                yield break;
            }

            onLoaded?.Invoke(json);
        }
        finally
        {
            ReleaseAddressablesHandle(addressablesType, handle);
        }
    }

    private static void ReleaseAddressablesHandle(Type addressablesType, object handle)
    {
        if (addressablesType == null || handle == null)
            return;

        var releaseMethod = addressablesType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (
                    !string.Equals(m.Name, "Release", StringComparison.Ordinal) || m.IsGenericMethod
                )
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length == 1
                    && parameters[0]
                        .ParameterType.Name.Contains(
                            "AsyncOperationHandle",
                            StringComparison.Ordinal
                        );
            });
        if (releaseMethod == null)
            return;

        try
        {
            releaseMethod.Invoke(null, new[] { handle });
        }
        catch
        {
            // Non-fatal cleanup failure.
        }
    }

    private static void NormalizeBuffEffects(IReadOnlyList<BuffDefinition> buffs)
    {
        if (buffs == null || buffs.Count == 0)
            return;

        for (int i = 0; i < buffs.Count; i++)
        {
            var buff = buffs[i];
            if (buff == null)
                continue;

            if (buff.effects != null && buff.effects.Length > 0)
                continue;

            var rawEffectsJson = (buff.effects_json ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(rawEffectsJson))
                continue;

            buff.effects = ParseEffectsJson(rawEffectsJson, buff.id);
        }
    }

    private static EffectItem[] ParseEffectsJson(string effectsJson, string buffId)
    {
        try
        {
            var raw = (effectsJson ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw))
                return Array.Empty<EffectItem>();

            if (raw.StartsWith("[", StringComparison.Ordinal))
            {
                var wrapped = "{\"items\":" + raw + "}";
                var list = JsonUtility.FromJson<EffectItemList>(wrapped);
                return list?.items ?? Array.Empty<EffectItem>();
            }

            var single = JsonUtility.FromJson<EffectItem>(raw);
            if (single != null && !string.IsNullOrWhiteSpace(single.modifierId))
                return new[] { single };

            var parsedList = JsonUtility.FromJson<EffectItemList>(raw);
            return parsedList?.items ?? Array.Empty<EffectItem>();
        }
        catch (Exception ex)
        {
            var id = string.IsNullOrWhiteSpace(buffId) ? "unknown" : buffId.Trim();
            throw new InvalidOperationException(
                $"Buff '{id}' has invalid effects_json. Ensure it is valid JSON for effects[].modifierId. {ex.Message}"
            );
        }
    }

    [Serializable]
    private sealed class EffectItemList
    {
        public EffectItem[] items;
    }

    private static void NormalizeParameterizedPaths(GameDefinition definition)
    {
        if (definition == null)
            return;

        if (definition.modifiers != null)
        {
            for (int i = 0; i < definition.modifiers.Count; i++)
            {
                var modifier = definition.modifiers[i];
                if (modifier == null)
                    continue;

                var rawTarget = (modifier.target ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(rawTarget))
                    continue;

                if (
                    !ParameterizedPathParser.TryCanonicalizeModifierParameterizedPath(
                        rawTarget,
                        out var canonicalTarget,
                        out var usedLegacyFormat
                    )
                )
                {
                    continue;
                }

                if (string.Equals(rawTarget, canonicalTarget, StringComparison.Ordinal))
                    continue;

                modifier.target = canonicalTarget;

#if UNITY_EDITOR
                if (usedLegacyFormat)
                {
                    Debug.LogWarning(
                        $"GameDefinitionLoader: modifier '{modifier.id}' target '{rawTarget}' normalized to '{canonicalTarget}'. Prefer bracket form."
                    );
                }
#endif
            }
        }

        if (definition.computedVars != null)
        {
            for (int i = 0; i < definition.computedVars.Count; i++)
            {
                var computedVar = definition.computedVars[i];
                if (computedVar?.dependsOn == null)
                    continue;

                for (int d = 0; d < computedVar.dependsOn.Length; d++)
                {
                    var raw = (computedVar.dependsOn[d] ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    if (
                        !ParameterizedPathParser.TryCanonicalizeFormulaParameterizedPath(
                            raw,
                            out var canonical,
                            out var usedLegacyFormat
                        )
                    )
                    {
                        continue;
                    }

                    if (string.Equals(raw, canonical, StringComparison.Ordinal))
                        continue;

                    computedVar.dependsOn[d] = canonical;

#if UNITY_EDITOR
                    if (usedLegacyFormat)
                    {
                        Debug.LogWarning(
                            $"GameDefinitionLoader: computedVars[{i}] dependsOn '{raw}' normalized to '{canonical}'. Prefer bracket form."
                        );
                    }
#endif
                }
            }
        }

        if (definition.prestige?.formula != null)
        {
            NormalizeFormulaPath(
                ref definition.prestige.formula.basedOn,
                "prestige.formula.basedOn"
            );
        }

        var metaUpgrades = definition.prestige?.metaUpgrades;
        if (metaUpgrades == null)
            return;

        for (int i = 0; i < metaUpgrades.Length; i++)
        {
            var metaUpgrade = metaUpgrades[i];
            if (metaUpgrade?.computed == null)
                continue;

            NormalizeFormulaPath(
                ref metaUpgrade.computed.basedOn,
                $"prestige.metaUpgrades[{i}].computed.basedOn"
            );
        }
    }

    private static void NormalizeFormulaPath(ref string path, string context)
    {
        var raw = (path ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return;

        if (
            !ParameterizedPathParser.TryCanonicalizeFormulaParameterizedPath(
                raw,
                out var canonical,
                out var usedLegacyFormat
            )
        )
        {
            return;
        }

        if (string.Equals(raw, canonical, StringComparison.Ordinal))
            return;

        path = canonical;

#if UNITY_EDITOR
        if (usedLegacyFormat)
        {
            Debug.LogWarning(
                $"GameDefinitionLoader: {context} '{raw}' normalized to '{canonical}'. Prefer bracket form."
            );
        }
#endif
    }
}
