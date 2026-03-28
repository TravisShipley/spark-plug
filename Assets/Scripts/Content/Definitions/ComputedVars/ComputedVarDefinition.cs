using System;
using System.Collections.Generic;

[Serializable]
public sealed class ComputedVarDefinition
{
    public string id;
    public string displayName;
    public string zoneId;
    public string[] dependsOn;
    public string[] tags;

    [NonSerialized]
    public ComputedExpressionDefinition expression;
}

public sealed class ComputedExpressionDefinition
{
    public string type;
    public readonly List<ComputedExpressionArgument> args = new();
}

public sealed class ComputedExpressionArgument
{
    public bool IsNumber;
    public double NumberValue;
    public string PathValue;
    public ComputedExpressionDefinition ExpressionValue;
}
