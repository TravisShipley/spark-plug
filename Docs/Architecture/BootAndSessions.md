---
document_role: design
topic: architecture
audience: ai, developers
scope: system:boot
status: active
---

# Spark Plug Boot And Session Flow

This document describes how Spark Plug selects a playable prototype, loads its definition, starts the runtime, and isolates save data.

## 1. Purpose

Spark Plug no longer assumes a single global definition file or a single gameplay scene.

The boot flow is now session-based so that:

- one prototype may have its own scene
- multiple prototypes may share the same scene
- launcher UI and direct scene play use the same runtime path
- runtime services do not depend on scene names or `ScriptableObject` assets
- save data is isolated per prototype session

## 2. Key Types

### `GameSessionConfigAsset`

Authoring asset used by scenes, launcher UI, and tools.

Fields:

- `sessionId`
- `displayName`
- `sceneName`
- `gameDefinitionJson`
- `saveSlotId`
- `resetSaveOnBoot`
- `verboseLogging`

Rules:

- `sessionId` is the stable internal identity for a prototype session
- `displayName` is user-facing only
- `sceneName` is the target scene to load
- `gameDefinitionJson` points at the imported `TextAsset` to boot
- `saveSlotId` is optional and usually stays `default`

### `GameSessionRequest`

Plain runtime payload derived from `GameSessionConfigAsset`.

Purpose:

- strip Unity authoring concerns before runtime boot
- pass only simple runtime data into boot logic

### `SparkPlugRuntimeConfig`

Immutable runtime boot config passed into `GameCompositionRoot`.

Contains:

- `SessionId`
- `DisplayName`
- `SaveSlotId`
- `ResetSaveOnBoot`
- `VerboseLogging`
- `GameDefinition`

### `SparkPlugBootContext`

Static transient handoff used when one scene selects a session and then loads another scene.

Purpose:

- launcher/menu stores a pending session config before scene load
- target scene consumes it exactly once during bootstrap

### `GameSessionBootstrapper`

Scene entry point that resolves which session to boot.

Resolution order:

1. `SparkPlugBootContext.ConsumePendingSession()`
2. serialized `defaultSessionConfig`

After resolution it:

1. validates the config
2. builds a `GameSessionRequest`
3. parses the selected `gameDefinitionJson`
4. builds `SparkPlugRuntimeConfig`
5. calls `GameCompositionRoot.BeginBootstrap(...)`

### `PrototypeLaunchService`

Static helper for launcher UI and dev tools.

Responsibilities:

- validate a `GameSessionConfigAsset`
- store it in `SparkPlugBootContext`
- load the config's target scene
- return to the bootstrap scene when exiting a prototype

### `GameSessionCatalog`

Simple registry of known `GameSessionConfigAsset` entries.

Current purpose:

- optional launcher/tooling lookup by `sessionId`
- shared place to keep the playable session list

## 3. Boot Pipeline

### Direct play from a prototype scene

```text
Press Play in Unity
  -> scene contains GameSessionBootstrapper
  -> bootstrapper uses defaultSessionConfig
  -> bootstrapper builds runtime config
  -> GameCompositionRoot begins runtime bootstrap
```

### Launch from Bootstrap scene or future menu

```text
Launcher UI
  -> PrototypeLaunchService.Launch(config)
  -> SparkPlugBootContext.SetPendingSession(config)
  -> SceneManager.LoadScene(config.SceneName)
  -> GameSessionBootstrapper consumes pending session
  -> GameCompositionRoot begins runtime bootstrap
```

### Exit from a prototype

```text
Esc key or explicit return button
  -> PrototypeReturnToBootstrapController
  -> PrototypeLaunchService.ReturnToBootstrap("Bootstrap")
  -> SparkPlugBootContext.Clear()
  -> SceneManager.LoadScene("Bootstrap")
```

`UiScreenManager` still consumes `Esc` first for dismissible modal screens. Prototype exit only happens when there is no open screen consuming that keypress.

## 4. Save Isolation

Save keys are namespaced by session and save slot:

```text
sparkplug.{sessionId}.{saveSlotId}
```

Examples:

- `sparkplug.proto_llama.default`
- `sparkplug.proto_orange.default`
- `sparkplug.proto_balance_fast.test_a`

Behavior:

- different `sessionId` values do not share saves
- different `saveSlotId` values inside the same session do not share saves
- changing `sessionId` effectively creates a new save namespace

### When to use `saveSlotId`

Most sessions should use:

- `saveSlotId = default`

Use a different `saveSlotId` only when one prototype intentionally needs multiple save buckets, such as:

- QA scenarios
- temporary balance branches
- alternate profiles inside the same prototype

## 5. Naming Guidance

Treat `sessionId` as a stable internal key.

Recommended style:

- `proto_llama`
- `proto_orange`
- `proto_balance_fast`

Avoid:

- display labels with spaces
- scene names reused as the only identity
- IDs that may be renamed casually

`displayName` can be more human-friendly, for example:

- `Llama Prototype`
- `Orange Test`
- `Fast Balance Pass`

These example names are illustrative only. Current prototype names in the repository are not considered stable API.

## 6. Unity Hookup

### Bootstrap scene

Recommended contents:

- launcher UI only
- one `PrototypeLaunchButton` per launcher button, or equivalent custom UI
- optional `GameSessionCatalog` asset reference for future dynamic population

For each launcher button:

1. add `PrototypeLaunchButton`
2. assign a `GameSessionConfigAsset`
3. optionally allow the component to use `displayName` as the label

### Prototype scenes

Each playable prototype scene should contain:

- `GameCompositionRoot`
- `GameSessionBootstrapper`
- `PrototypeReturnToBootstrapController`

Inspector setup:

1. assign `defaultSessionConfig` on `GameSessionBootstrapper`
2. keep `GameCompositionRoot` wired as normal
3. assign `UiScreenManager` on `PrototypeReturnToBootstrapController` if present in scene
4. ensure the scene named in the session config is listed in Build Settings

### Build Settings

Every scene referenced by a `GameSessionConfigAsset.sceneName` must be present in Build Settings.

That includes:

- the bootstrap scene
- each playable prototype scene

## 7. Imported Definitions

Imported definition JSON is now expected to live under:

- `Assets/Data/Definitions/`

Examples:

- `Assets/Data/Definitions/proto_llama.json`
- `Assets/Data/Definitions/proto_orange.json`

Important:

- runtime identity comes from `sessionId`, not the filename
- multiple sessions may point at the same imported `TextAsset`
- different scenes may point at different definitions

## 8. Current Constraints

- runtime systems must not branch on scene names
- runtime systems must not depend on `GameSessionConfigAsset`
- `GameSessionBootstrapper` is the scene-level selector
- `GameCompositionRoot` is the runtime composition root
- `SaveService` remains the only runtime authority over persisted player facts

## 9. Related Docs

- `Overview.md`
- `SystemMap.md`
- `ArchitectureRules.md`
- `../Process/DataAuthority.md`
- `../Process/ContentImportWorkflow.md`
