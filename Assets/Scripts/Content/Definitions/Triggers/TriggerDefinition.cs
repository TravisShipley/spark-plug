using System;

[Serializable]
public sealed class TriggerDefinition
{
    public string id;
    public string @event;
    public string eventType;
    public TriggerScopeDefinition scope;
    public TriggerConditionDefinition[] conditions;
    public TriggerActionDefinition[] actions;
}
