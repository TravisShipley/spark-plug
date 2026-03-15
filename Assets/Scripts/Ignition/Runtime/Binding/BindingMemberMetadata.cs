using System;
using System.Reflection;

namespace Ignition.Binding
{
    public sealed class BindingMemberMetadata
    {
        public BindingMemberMetadata(
            PropertyInfo property,
            string memberName,
            string displayName,
            Type valueType,
            Type propertyType
        )
        {
            Property = property;
            MemberName = memberName;
            DisplayName = displayName;
            ValueType = valueType;
            PropertyType = propertyType;
        }

        public PropertyInfo Property { get; }
        public string MemberName { get; }
        public string DisplayName { get; }
        public Type ValueType { get; }
        public Type PropertyType { get; }
    }
}
