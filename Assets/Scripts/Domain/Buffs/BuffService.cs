using System;
using UniRx;
using UnityEngine;

public sealed class BuffService : IDisposable
{
    private readonly SaveService saveService;
    private readonly BuffCatalog buffCatalog;
    private readonly ModifierService modifierService;
    private readonly TickService tickService;
    private readonly CompositeDisposable disposables = new();
    private readonly Subject<Unit> changed = new();

    private readonly ReactiveProperty<bool> isActive = new(false);
    private readonly ReactiveProperty<long> remainingSeconds = new(0);

    private string activeBuffId;
    private long activeBuffExpiresAtUnixSeconds;

    public IReadOnlyReactiveProperty<bool> IsActive => isActive;
    public IReadOnlyReactiveProperty<long> RemainingSeconds => remainingSeconds;
    public IObservable<Unit> Changed => changed;

    public BuffService(
        SaveService saveService,
        BuffCatalog buffCatalog,
        ModifierService modifierService,
        TickService tickService
    )
    {
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.buffCatalog = buffCatalog ?? throw new ArgumentNullException(nameof(buffCatalog));
        this.modifierService =
            modifierService ?? throw new ArgumentNullException(nameof(modifierService));
        this.tickService = tickService ?? throw new ArgumentNullException(nameof(tickService));

        RestoreFromSave();
        this.tickService.OnTick.Subscribe(_ => OnTick()).AddTo(disposables);
    }

    public bool CanActivate(string buffId)
    {
        var requestedBuffId = NormalizeId(buffId);
        if (string.IsNullOrEmpty(requestedBuffId))
            return false;

        if (!buffCatalog.TryGet(requestedBuffId, out _))
            throw new InvalidOperationException($"BuffService: unknown buff id '{requestedBuffId}'.");

        return !isActive.Value;
    }

    public void Activate(string buffId)
    {
        var requestedBuffId = NormalizeRequiredId(buffId, nameof(buffId));
        var buff = buffCatalog.Get(requestedBuffId);

        if (isActive.Value)
            return;

        var now = GetCurrentUnixSeconds();
        activeBuffId = requestedBuffId;
        activeBuffExpiresAtUnixSeconds = now + Math.Max(1, buff.durationSeconds);

        saveService.SetActiveBuffState(activeBuffId, activeBuffExpiresAtUnixSeconds, requestSave: true);
        ApplyActiveBuffSource();
        modifierService.RebuildActiveModifiers($"buff:{activeBuffId}:activate");

        RefreshReactiveState(now);
        changed.OnNext(Unit.Default);
    }

    public void Dispose()
    {
        changed.OnCompleted();
        changed.Dispose();
        isActive.Dispose();
        remainingSeconds.Dispose();
        disposables.Dispose();
    }

    private void RestoreFromSave()
    {
        var now = GetCurrentUnixSeconds();
        var savedBuffId = NormalizeId(saveService.ActiveBuffId);
        var savedExpiresAt = Math.Max(0, saveService.ActiveBuffExpiresAtUnixSeconds);

        if (string.IsNullOrEmpty(savedBuffId) || savedExpiresAt <= now)
        {
            if (!string.IsNullOrEmpty(savedBuffId) || savedExpiresAt > 0)
                saveService.ClearActiveBuffState(requestSave: true);

            ClearActiveState();
            RefreshReactiveState(now);
            return;
        }

        if (!buffCatalog.TryGet(savedBuffId, out var buff) || buff == null)
        {
            Debug.LogWarning($"BuffService: save references missing buff '{savedBuffId}'. Clearing state.");
            saveService.ClearActiveBuffState(requestSave: true);
            ClearActiveState();
            RefreshReactiveState(now);
            return;
        }

        activeBuffId = savedBuffId;
        activeBuffExpiresAtUnixSeconds = savedExpiresAt;

        ApplyActiveBuffSource();
        modifierService.RebuildActiveModifiers($"buff:{activeBuffId}:restore");

        RefreshReactiveState(now);
        changed.OnNext(Unit.Default);
    }

    private void OnTick()
    {
        var now = GetCurrentUnixSeconds();
        if (!isActive.Value)
        {
            RefreshReactiveState(now);
            return;
        }

        if (now < activeBuffExpiresAtUnixSeconds)
        {
            RefreshReactiveState(now);
            return;
        }

        var sourceKey = BuildSourceKey(activeBuffId);
        modifierService.RemoveBuffModifierSource(sourceKey);
        modifierService.RebuildActiveModifiers($"buff:{activeBuffId}:expired");

        saveService.ClearActiveBuffState(requestSave: true);
        ClearActiveState();
        RefreshReactiveState(now);
        changed.OnNext(Unit.Default);
    }

    private void ApplyActiveBuffSource()
    {
        if (string.IsNullOrEmpty(activeBuffId))
            return;

        var buff = buffCatalog.Get(activeBuffId);
        modifierService.SetBuffModifierSource(BuildSourceKey(activeBuffId), activeBuffId, buff.effects);
    }

    private void ClearActiveState()
    {
        activeBuffId = string.Empty;
        activeBuffExpiresAtUnixSeconds = 0;
    }

    private void RefreshReactiveState(long now)
    {
        var active = !string.IsNullOrEmpty(activeBuffId) && activeBuffExpiresAtUnixSeconds > now;
        isActive.Value = active;
        remainingSeconds.Value = active ? Math.Max(0, activeBuffExpiresAtUnixSeconds - now) : 0;
    }

    private static string BuildSourceKey(string buffId)
    {
        return $"buff:{NormalizeId(buffId)}";
    }

    private static string NormalizeRequiredId(string raw, string paramName)
    {
        var id = NormalizeId(raw);
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Value cannot be null or empty.", paramName);

        return id;
    }

    private static string NormalizeId(string raw)
    {
        return (raw ?? string.Empty).Trim();
    }

    private static long GetCurrentUnixSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
