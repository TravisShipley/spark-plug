using System;

[Serializable]
public sealed class TriggerDefinition
{
    public string id;
    public string @event;
    public string eventType;
    public TriggerScope scope;
    public TriggerCondition[] conditions;
    public TriggerAction[] actions;
}
