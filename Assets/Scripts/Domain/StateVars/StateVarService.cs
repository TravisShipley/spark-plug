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

    private readonly struct StateVarTransferKey : IEquatable<StateVarTransferKey>
    {
        public readonly string ZoneId;
        public readonly string FromVarId;
        public readonly string ToVarId;

        public StateVarTransferKey(string zoneId, string fromVarId, string toVarId)
        {
            ZoneId = zoneId;
            FromVarId = fromVarId;
            ToVarId = toVarId;
        }

        public bool Equals(StateVarTransferKey other)
        {
            return string.Equals(ZoneId, other.ZoneId, StringComparison.Ordinal)
                && string.Equals(FromVarId, other.FromVarId, StringComparison.Ordinal)
                && string.Equals(ToVarId, other.ToVarId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StateVarTransferKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ZoneId != null ? ZoneId.GetHashCode() : 0;
                hash = (hash * 397) ^ (FromVarId != null ? FromVarId.GetHashCode() : 0);
                hash = (hash * 397) ^ (ToVarId != null ? ToVarId.GetHashCode() : 0);
                return hash;
            }
        }
    }

    private sealed class StateVarTransferDefinition
    {
        public string zoneId;
        public string fromVarId;
        public string toVarId;
        public double ratePerSecond;
    }

    private readonly GameDefinitionService gameDefinitionService;
    private readonly SaveService saveService;
    private readonly Dictionary<StateVarKey, ReactiveProperty<double>> quantities = new();
    private readonly Dictionary<StateVarTransferKey, StateVarTransferDefinition> transfers = new();
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

    // Example:
    // stateVarService.RegisterTransfer("zone.main", "llamaBuffer", "llamas", 10d);
    public void RegisterTransfer(string zoneId, string fromVarId, string toVarId, double ratePerSecond)
    {
        if (
            !TryCreateTransferKey(zoneId, fromVarId, toVarId, out var key)
            || ratePerSecond <= Epsilon
            || double.IsNaN(ratePerSecond)
            || double.IsInfinity(ratePerSecond)
        )
        {
            return;
        }

        transfers[key] = new StateVarTransferDefinition
        {
            zoneId = key.ZoneId,
            fromVarId = key.FromVarId,
            toVarId = key.ToVarId,
            ratePerSecond = ratePerSecond,
        };
    }

    public void UnregisterTransfer(string zoneId, string fromVarId, string toVarId)
    {
        if (!TryCreateTransferKey(zoneId, fromVarId, toVarId, out var key))
            return;

        transfers.Remove(key);
    }

    public void TickTransfers(double deltaTimeSeconds)
    {
        if (
            deltaTimeSeconds <= 0d
            || double.IsNaN(deltaTimeSeconds)
            || double.IsInfinity(deltaTimeSeconds)
            || transfers.Count == 0
        )
        {
            return;
        }

        var anyChanged = false;
        foreach (var entry in transfers)
        {
            var transfer = entry.Value;
            if (transfer == null)
                continue;

            var fromCurrent = GetQuantity(transfer.zoneId, transfer.fromVarId);
            if (fromCurrent <= Epsilon)
                continue;

            var toCurrent = GetQuantity(transfer.zoneId, transfer.toVarId);
            var toCapacity = GetCapacity(transfer.zoneId, transfer.toVarId);
            var freeTo = Math.Max(0d, toCapacity - toCurrent);
            if (freeTo <= Epsilon)
                continue;

            var maxThisTick = transfer.ratePerSecond * deltaTimeSeconds;
            if (maxThisTick <= Epsilon || double.IsNaN(maxThisTick) || double.IsInfinity(maxThisTick))
                continue;

            var amount = Math.Min(fromCurrent, Math.Min(freeTo, maxThisTick));
            if (amount <= Epsilon)
                continue;

            if (
                !TryCreateKey(transfer.zoneId, transfer.fromVarId, out var fromKey)
                || !TryCreateKey(transfer.zoneId, transfer.toVarId, out var toKey)
            )
            {
                continue;
            }

            SetQuantity(fromKey, fromCurrent - amount, requestSave: false);
            SetQuantity(toKey, toCurrent + amount, requestSave: false);
            anyChanged = true;
        }

        if (anyChanged)
            saveService.RequestSave();
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
        transfers.Clear();
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

    private static bool TryCreateTransferKey(
        string zoneId,
        string fromVarId,
        string toVarId,
        out StateVarTransferKey key
    )
    {
        var normalizedZoneId = NormalizeId(zoneId);
        var normalizedFromVarId = NormalizeId(fromVarId);
        var normalizedToVarId = NormalizeId(toVarId);
        if (
            string.IsNullOrEmpty(normalizedZoneId)
            || string.IsNullOrEmpty(normalizedFromVarId)
            || string.IsNullOrEmpty(normalizedToVarId)
            || string.Equals(normalizedFromVarId, normalizedToVarId, StringComparison.Ordinal)
        )
        {
            key = default(StateVarTransferKey);
            return false;
        }

        key = new StateVarTransferKey(normalizedZoneId, normalizedFromVarId, normalizedToVarId);
        return true;
    }

    private static string NormalizeId(string id)
    {
        return (id ?? string.Empty).Trim();
    }
}
