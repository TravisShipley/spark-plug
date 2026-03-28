using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class ComputedVarService
{
    private readonly GameDefinitionService gameDefinitionService;
    private readonly SaveService saveService;
    private readonly WalletService walletService;
    private readonly Dictionary<string, List<ComputedVarDefinition>> computedVarsById = new(
        StringComparer.Ordinal
    );

    public ComputedVarService(
        GameDefinitionService gameDefinitionService,
        SaveService saveService,
        WalletService walletService
    )
    {
        this.gameDefinitionService =
            gameDefinitionService ?? throw new ArgumentNullException(nameof(gameDefinitionService));
        this.saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        this.walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));

        var computedVars = this.gameDefinitionService.Definition?.computedVars;
        if (computedVars == null)
            return;

        for (int i = 0; i < computedVars.Count; i++)
        {
            var computedVar = computedVars[i];
            var id = NormalizeId(computedVar?.id);
            if (string.IsNullOrEmpty(id))
                continue;

            if (!computedVarsById.TryGetValue(id, out var entries))
            {
                entries = new List<ComputedVarDefinition>();
                computedVarsById[id] = entries;
            }

            entries.Add(computedVar);
        }
    }

    public bool TryEvaluate(string varId, string zoneId, out double value)
    {
        value = 0d;
        var id = NormalizeId(varId);
        if (string.IsNullOrEmpty(id))
            return false;

        if (!TryResolveDefinition(id, NormalizeId(zoneId), out var definition) || definition == null)
            return false;

        var stack = new HashSet<string>(StringComparer.Ordinal);
        if (!TryEvaluate(definition, NormalizeId(zoneId), stack, out value))
            value = 0d;

        return true;
    }

    public double EvaluateOrZero(string varId, string zoneId)
    {
        return TryEvaluate(varId, zoneId, out var value) ? value : 0d;
    }

    public double ResolvePathOrZero(string rawPath, string zoneId)
    {
        return ResolvePathOrZero(rawPath, NormalizeId(zoneId), new HashSet<string>(StringComparer.Ordinal));
    }

    private bool TryEvaluate(
        ComputedVarDefinition definition,
        string zoneId,
        HashSet<string> stack,
        out double value
    )
    {
        value = 0d;
        if (definition?.expression == null)
            return false;

        var key = BuildEvaluationKey(definition.id, zoneId);
        if (!stack.Add(key))
            throw new InvalidOperationException($"ComputedVarService: cyclical computed var '{key}'.");

        try
        {
            value = EvaluateExpression(definition.expression, zoneId, stack);
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = 0d;
            return true;
        }
        finally
        {
            stack.Remove(key);
        }
    }

    private double EvaluateExpression(
        ComputedExpressionDefinition expression,
        string zoneId,
        HashSet<string> stack
    )
    {
        if (expression == null)
            return 0d;

        var type = NormalizeId(expression.type).ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
            return 0d;

        switch (type)
        {
            case "constant":
                return EvaluateArgument(expression.args, 0, zoneId, stack);
            case "add":
                return SumArguments(expression.args, zoneId, stack);
            case "multiply":
                return MultiplyArguments(expression.args, zoneId, stack);
            case "power":
                return Math.Pow(
                    EvaluateArgument(expression.args, 0, zoneId, stack),
                    EvaluateArgument(expression.args, 1, zoneId, stack)
                );
            case "log10":
                var logInput = EvaluateArgument(expression.args, 0, zoneId, stack);
                return logInput > 0d ? Math.Log10(logInput) : 0d;
            case "min":
                return ReduceArguments(expression.args, zoneId, stack, Math.Min);
            case "max":
                return ReduceArguments(expression.args, zoneId, stack, Math.Max);
            case "clamp":
                return Math.Clamp(
                    EvaluateArgument(expression.args, 0, zoneId, stack),
                    EvaluateArgument(expression.args, 1, zoneId, stack),
                    EvaluateArgument(expression.args, 2, zoneId, stack)
                );
            case "if":
                return EvaluateArgument(expression.args, 0, zoneId, stack) > 0d
                    ? EvaluateArgument(expression.args, 1, zoneId, stack)
                    : EvaluateArgument(expression.args, 2, zoneId, stack);
            default:
                throw new InvalidOperationException(
                    $"ComputedVarService: unsupported expression type '{expression.type}'."
                );
        }
    }

    private double ResolvePathOrZero(string rawPath, string zoneId, HashSet<string> stack)
    {
        var path = NormalizeId(rawPath);
        if (string.IsNullOrEmpty(path))
            return 0d;

        if (
            double.TryParse(
                path,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var literal
            )
        )
        {
            return literal;
        }

        if (ParameterizedPathParser.TryParseFormulaParameterizedPath(path, out var parsed))
        {
            var parameterId = NormalizeId(parsed.ParameterId);
            switch (NormalizeId(parsed.CanonicalBaseName))
            {
                case "resource":
                    return walletService.GetBalance(parameterId);
                case "lifetimeEarnings":
                    return saveService.GetLifetimeEarnings(parameterId);
                case "stateQuantity":
                    return saveService.GetZoneStateVar(zoneId, parameterId);
                case "var":
                    if (
                        TryResolveDefinition(parameterId, zoneId, out var referenced)
                        && referenced != null
                        && TryEvaluate(referenced, zoneId, stack, out var value)
                    )
                    {
                        return value;
                    }

                    return 0d;
            }
        }

        return 0d;
    }

    private bool TryResolveDefinition(string varId, string zoneId, out ComputedVarDefinition definition)
    {
        definition = null;
        if (!computedVarsById.TryGetValue(varId, out var entries) || entries == null || entries.Count == 0)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            var candidateZoneId = NormalizeId(entries[i]?.zoneId);
            if (string.Equals(candidateZoneId, zoneId, StringComparison.Ordinal))
            {
                definition = entries[i];
                return true;
            }
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var candidateZoneId = NormalizeId(entries[i]?.zoneId);
            if (string.IsNullOrEmpty(candidateZoneId))
            {
                definition = entries[i];
                return true;
            }
        }

        definition = entries[0];
        return true;
    }

    private static double EvaluateArgument(
        List<ComputedExpressionArgument> args,
        int index,
        string zoneId,
        HashSet<string> stack,
        ComputedVarService service = null
    )
    {
        if (service == null)
            throw new InvalidOperationException("ComputedVarService: evaluation service is required.");

        if (args == null || index < 0 || index >= args.Count)
            return 0d;

        var arg = args[index];
        if (arg == null)
            return 0d;

        if (arg.ExpressionValue != null)
            return service.EvaluateExpression(arg.ExpressionValue, zoneId, stack);

        if (arg.IsNumber)
            return arg.NumberValue;

        return service.ResolvePathOrZero(arg.PathValue, zoneId, stack);
    }

    private double EvaluateArgument(List<ComputedExpressionArgument> args, int index, string zoneId, HashSet<string> stack)
    {
        return EvaluateArgument(args, index, zoneId, stack, this);
    }

    private double SumArguments(List<ComputedExpressionArgument> args, string zoneId, HashSet<string> stack)
    {
        double total = 0d;
        if (args == null)
            return total;

        for (int i = 0; i < args.Count; i++)
            total += EvaluateArgument(args, i, zoneId, stack);

        return total;
    }

    private double MultiplyArguments(List<ComputedExpressionArgument> args, string zoneId, HashSet<string> stack)
    {
        if (args == null || args.Count == 0)
            return 0d;

        double total = 1d;
        for (int i = 0; i < args.Count; i++)
            total *= EvaluateArgument(args, i, zoneId, stack);

        return total;
    }

    private double ReduceArguments(
        List<ComputedExpressionArgument> args,
        string zoneId,
        HashSet<string> stack,
        Func<double, double, double> reducer
    )
    {
        if (args == null || args.Count == 0)
            return 0d;

        var value = EvaluateArgument(args, 0, zoneId, stack);
        for (int i = 1; i < args.Count; i++)
            value = reducer(value, EvaluateArgument(args, i, zoneId, stack));

        return value;
    }

    private static string BuildEvaluationKey(string varId, string zoneId)
    {
        return $"{NormalizeId(zoneId)}::{NormalizeId(varId)}";
    }

    private static string NormalizeId(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}
