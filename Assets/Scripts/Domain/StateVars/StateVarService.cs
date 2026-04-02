using System;
using System.Collections.Generic;
using UniRx;

public sealed class StateVarService : IStateVarService, IDisposable
{
    private const double Epsilon = 0.0000001d;

    private readonly struct StateVarKey : IEquatable<StateVarKey>
    {
        public readonly string ZoneId;
        public readonly string VarId;

        public StateVarKey(string zoneId, string varId)
        {
            ZoneId = zoneId;
            VarId = varId;
        }

        public bool Equals(StateVarKey other)
        {
            return string.Equals(ZoneId, other.ZoneId, StringComparison.Ordinal)
                && string.Equals(VarId, other.VarId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StateVarKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ZoneId != null ? ZoneId.GetHashCode() : 0) * 397)
                    ^ (VarId != null ? VarId.GetHashCode() : 0);
            }
        }
    }

    private readonly GameDefinitionService gameDefinitionService;
    private readonly SaveService saveService;
    private readonly Dictionary<StateVarKey, ReactiveProperty<double>> quantities = new();
    private readonly CompositeDisposable disposables = new();

    public StateVarService(GameDefinitionService gameDefinitionService, SaveService saveService)
    {
        this.gameDefinitionService =
            gameDefinitionService ?? throw new ArgumentNullException(nameof(gameDefinitionService));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));

        RefreshAll();
    }

    public double GetQuantity(string zoneId, string varId)
    {
        if (!TryCreateKey(zoneId, varId, out var key))
            return 0d;

        return EnsureProperty(key).Value;
    }

    public double GetCapacity(string zoneId, string varId)
    {
        if (!TryCreateKey(zoneId, varId, out var key))
            return 0d;

        return SanitizeNonNegative(saveService.GetZoneStateCapacity(key.ZoneId, key.VarId));
    }

    public IReadOnlyReactiveProperty<double> ObserveQuantity(string zoneId, string varId)
    {
        if (!TryCreateKey(zoneId, varId, out var key))
        {
            throw new InvalidOperationException(
                "StateVarService.ObserveQuantity: zoneId and varId are required."
            );
        }

        return EnsureProperty(key);
    }

    public void SetQuantity(string zoneId, string varId, double value)
    {
        if (!TryCreateKey(zoneId, varId, out var key))
            return;

        SetQuantity(key, value, requestSave: true);
    }

    public void AddQuantity(string zoneId, string varId, double delta)
    {
        if (!TryCreateKey(zoneId, varId, out var key))
            return;

        if (double.IsNaN(delta) || double.IsInfinity(delta) || Math.Abs(delta) < Epsilon)
            return;

        SetQuantity(key, EnsureProperty(key).Value + delta, requestSave: true);
    }

    public void RefreshAll()
    {
        var zones = gameDefinitionService.Zones;
        if (zones == null)
            return;

        for (int i = 0; i < zones.Count; i++)
        {
            var zoneId = NormalizeId(zones[i]?.id);
            if (string.IsNullOrEmpty(zoneId))
                continue;

            RefreshZone(zoneId);
        }
    }

    public void RefreshZone(string zoneId)
    {
        var normalizedZoneId = NormalizeId(zoneId);
        if (string.IsNullOrEmpty(normalizedZoneId))
            return;

        var stateVars = gameDefinitionService.StateVars;
        if (stateVars == null)
            return;

        for (int i = 0; i < stateVars.Count; i++)
        {
            var varId = NormalizeId(stateVars[i]?.id);
            if (string.IsNullOrEmpty(varId))
                continue;

            var key = new StateVarKey(normalizedZoneId, varId);
            SetQuantity(key, saveService.GetZoneStateVar(normalizedZoneId, varId), requestSave: false);
        }
    }

    public void Dispose()
    {
        quantities.Clear();
        disposables.Dispose();
    }

    private ReactiveProperty<double> EnsureProperty(StateVarKey key)
    {
        if (quantities.TryGetValue(key, out var quantity) && quantity != null)
            return quantity;

        quantity = new ReactiveProperty<double>(
            ClampToCapacity(key.ZoneId, key.VarId, saveService.GetZoneStateVar(key.ZoneId, key.VarId))
        ).AddTo(disposables);
        quantities[key] = quantity;
        return quantity;
    }

    private void SetQuantity(StateVarKey key, double value, bool requestSave)
    {
        var sanitized = ClampToCapacity(key.ZoneId, key.VarId, value);
        var property = EnsureProperty(key);
        var changed = Math.Abs(property.Value - sanitized) >= Epsilon;

        if (changed)
            property.Value = sanitized;

        if (Math.Abs(saveService.GetZoneStateVar(key.ZoneId, key.VarId) - sanitized) >= Epsilon)
            saveService.SetZoneStateVar(key.ZoneId, key.VarId, sanitized, requestSave);
        else if (changed && requestSave)
            saveService.RequestSave();
    }

    private double ClampToCapacity(string zoneId, string varId, double value)
    {
        var sanitized = SanitizeNonNegative(value);
        var capacity = SanitizeNonNegative(saveService.GetZoneStateCapacity(zoneId, varId));
        return sanitized <= capacity ? sanitized : capacity;
    }

    private static double SanitizeNonNegative(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0d;

        return Math.Max(0d, value);
    }

    private static bool TryCreateKey(string zoneId, string varId, out StateVarKey key)
    {
        var normalizedZoneId = NormalizeId(zoneId);
        var normalizedVarId = NormalizeId(varId);
        if (string.IsNullOrEmpty(normalizedZoneId) || string.IsNullOrEmpty(normalizedVarId))
        {
            key = default(StateVarKey);
            return false;
        }

        key = new StateVarKey(normalizedZoneId, normalizedVarId);
        return true;
    }

    private static string NormalizeId(string id)
    {
        return (id ?? string.Empty).Trim();
    }
}
