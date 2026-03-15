using System;
using System.Collections.Generic;
using System.Reflection;
using UniRx;

namespace Ignition.Binding
{
    public static class BindingMetadataUtility
    {
        private const BindingFlags BindablePropertyFlags =
            BindingFlags.Instance | BindingFlags.Public;

        public static IReadOnlyList<BindingMemberMetadata> GetBindableMembers(Type sourceType)
        {
            if (sourceType == null)
                return Array.Empty<BindingMemberMetadata>();

            var members = new List<BindingMemberMetadata>();
            var properties = sourceType.GetProperties(BindablePropertyFlags);
            for (var i = 0; i < properties.Length; i++)
            {
                if (TryCreateMetadata(properties[i], out var metadata))
                    members.Add(metadata);
            }

            return members;
        }

        public static bool TryGetBindableMember(
            Type sourceType,
            string memberName,
            out BindingMemberMetadata metadata
        )
        {
            metadata = null;
            if (sourceType == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            var property = sourceType.GetProperty(memberName, BindablePropertyFlags);
            return TryCreateMetadata(property, out metadata);
        }

        public static bool IsBindable(PropertyInfo property)
        {
            return property != null
                && Attribute.IsDefined(property, typeof(BindableAttribute), true);
        }

        public static bool TryCreateMetadata(
            PropertyInfo property,
            out BindingMemberMetadata metadata
        )
        {
            metadata = null;
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                return false;

            var bindableAttribute = property.GetCustomAttribute<BindableAttribute>(true);
            if (bindableAttribute == null)
                return false;

            if (!TryGetEmittedValueType(property.PropertyType, out var valueType))
                return false;

            var displayName = string.IsNullOrWhiteSpace(bindableAttribute.DisplayName)
                ? property.Name
                : bindableAttribute.DisplayName.Trim();

            metadata = new BindingMemberMetadata(
                property,
                property.Name,
                displayName,
                valueType,
                property.PropertyType
            );
            return true;
        }

        public static bool TryGetEmittedValueType(Type propertyType, out Type valueType)
        {
            if (
                TryGetGenericArgument(
                    propertyType,
                    typeof(IReadOnlyReactiveProperty<>),
                    out valueType
                )
            )
                return true;

            if (TryGetGenericArgument(propertyType, typeof(ReactiveProperty<>), out valueType))
                return true;

            if (TryGetGenericArgument(propertyType, typeof(IObservable<>), out valueType))
                return true;

            valueType = null;
            return false;
        }

        public static bool IsExactValueTypeMatch(
            BindingMemberMetadata metadata,
            Type expectedValueType
        )
        {
            return metadata != null && metadata.ValueType == expectedValueType;
        }

        private static bool TryGetGenericArgument(
            Type candidateType,
            Type genericTypeDefinition,
            out Type valueType
        )
        {
            if (candidateType == null)
            {
                valueType = null;
                return false;
            }

            if (
                candidateType.IsGenericType
                && candidateType.GetGenericTypeDefinition() == genericTypeDefinition
            )
            {
                valueType = candidateType.GetGenericArguments()[0];
                return true;
            }

            var interfaces = candidateType.GetInterfaces();
            for (var i = 0; i < interfaces.Length; i++)
            {
                var interfaceType = interfaces[i];
                if (
                    interfaceType.IsGenericType
                    && interfaceType.GetGenericTypeDefinition() == genericTypeDefinition
                )
                {
                    valueType = interfaceType.GetGenericArguments()[0];
                    return true;
                }
            }

            var baseType = candidateType.BaseType;
            if (baseType != null)
                return TryGetGenericArgument(baseType, genericTypeDefinition, out valueType);

            valueType = null;
            return false;
        }
    }
}
