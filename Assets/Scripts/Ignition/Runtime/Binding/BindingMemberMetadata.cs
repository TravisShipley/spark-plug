using System;
using System.Reflection;

namespace Ignition.Binding
{
    public enum BindingMemberScope
    {
        Provider,
        Data,
    }

    public sealed class BindingMemberMetadata
    {
        public BindingMemberMetadata(
            BindingMemberScope scope,
            PropertyInfo property,
            string memberName,
            string serializedKey,
            string displayName,
            Type valueType,
            Type propertyType
        )
        {
            Scope = scope;
            Property = property;
            MemberName = memberName;
            SerializedKey = serializedKey;
            DisplayName = displayName;
            ValueType = valueType;
            PropertyType = propertyType;
        }

        public BindingMemberScope Scope { get; }
        public PropertyInfo Property { get; }
        public string MemberName { get; }
        public string SerializedKey { get; }
        public string DisplayName { get; }
        public Type ValueType { get; }
        public Type PropertyType { get; }
    }
}
