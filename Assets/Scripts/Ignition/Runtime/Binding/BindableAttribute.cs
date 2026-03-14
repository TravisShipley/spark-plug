using System;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class BindableAttribute : Attribute
{
    public BindableAttribute()
    {
    }

    public BindableAttribute(string displayName)
    {
        DisplayName = displayName;
    }

    public string DisplayName { get; }
}
