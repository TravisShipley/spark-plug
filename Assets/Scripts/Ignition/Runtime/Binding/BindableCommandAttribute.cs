using System;

namespace Ignition.Binding
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class BindableCommandAttribute : Attribute
    {
        public BindableCommandAttribute() { }

        public BindableCommandAttribute(string displayName)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
