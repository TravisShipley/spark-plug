using System;
using System.Collections.Generic;
using System.Reflection;
using Ignition.Commands;
using UniRx;

namespace Ignition.Binding
{
    public static class BindingMetadataUtility
    {
        public const string DataScopePrefix = "Data.";
        private const BindingFlags BindablePropertyFlags =
            BindingFlags.Instance | BindingFlags.Public;

        public static IReadOnlyList<BindingMemberMetadata> GetBindableMembers(Type sourceType)
        {
            return GetBindableMembers(sourceType, null);
        }

        public static IReadOnlyList<BindingMemberMetadata> GetBindableMembers(
            Type providerType,
            Type dataType
        )
        {
            if (providerType == null && dataType == null)
                return Array.Empty<BindingMemberMetadata>();

            var members = new List<BindingMemberMetadata>();
            AddBindableMembers(providerType, BindingMemberScope.Provider, members);
            AddBindableMembers(dataType, BindingMemberScope.Data, members);
            return members;
        }

        public static bool TryGetBindableMember(
            Type sourceType,
            string memberName,
            out BindingMemberMetadata metadata
        )
        {
            return TryGetBindableMember(
                sourceType,
                BindingMemberScope.Provider,
                memberName,
                out metadata,
                out _
            );
        }

        public static bool TryGetBindableMember(
            Type sourceType,
            BindingMemberScope scope,
            string memberName,
            out BindingMemberMetadata metadata,
            out string validationMessage
        )
        {
            metadata = null;
            validationMessage = null;
            if (sourceType == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            var property = sourceType.GetProperty(memberName, BindablePropertyFlags);
            if (property == null)
            {
                validationMessage =
                    $"could not find public property '{memberName}' on '{sourceType.Name}'.";
                return false;
            }

            if (!IsBindable(property) && !IsBindableCommand(property))
            {
                validationMessage =
                    $"'{sourceType.Name}.{memberName}' is not marked with [Bindable] or [BindableCommand].";
                return false;
            }

            if (!TryCreateMetadata(property, scope, out metadata))
            {
                validationMessage = typeof(ICommand).IsAssignableFrom(property.PropertyType)
                    ? $"'{sourceType.Name}.{memberName}' must return {nameof(ICommand)}."
                    : $"'{sourceType.Name}.{memberName}' must return IReadOnlyReactiveProperty<T>, ReactiveProperty<T>, or IObservable<T>.";
                return false;
            }

            return true;
        }

        public static bool IsBindable(PropertyInfo property)
        {
            return property != null
                && Attribute.IsDefined(property, typeof(BindableAttribute), true);
        }

        public static bool IsBindableCommand(PropertyInfo property)
        {
            return property != null
                && Attribute.IsDefined(property, typeof(BindableCommandAttribute), true);
        }

        public static bool TryCreateMetadata(
            PropertyInfo property,
            out BindingMemberMetadata metadata
        )
        {
            return TryCreateMetadata(property, BindingMemberScope.Provider, out metadata);
        }

        public static bool TryCreateMetadata(
            PropertyInfo property,
            BindingMemberScope scope,
            out BindingMemberMetadata metadata
        )
        {
            metadata = null;
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                return false;

            var bindableAttribute = property.GetCustomAttribute<BindableAttribute>(true);
            var bindableCommandAttribute = property.GetCustomAttribute<BindableCommandAttribute>(
                true
            );
            if (bindableAttribute == null && bindableCommandAttribute == null)
                return false;

            Type valueType;
            if (typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                valueType = typeof(ICommand);
            }
            else if (!TryGetEmittedValueType(property.PropertyType, out valueType))
            {
                return false;
            }

            var displayName = ResolveDisplayName(property, bindableAttribute, bindableCommandAttribute);
            var memberName = property.Name;

            metadata = new BindingMemberMetadata(
                scope,
                property,
                memberName,
                CreateSerializedKey(scope, memberName),
                displayName,
                valueType,
                property.PropertyType
            );
            return true;
        }

        public static bool TryParseBindingKey(
            string bindingKey,
            out BindingMemberScope scope,
            out string memberName
        )
        {
            memberName = string.Empty;
            if (string.IsNullOrWhiteSpace(bindingKey))
            {
                scope = BindingMemberScope.Provider;
                return false;
            }

            var normalizedKey = bindingKey.Trim();
            if (
                normalizedKey.StartsWith(DataScopePrefix, StringComparison.Ordinal)
                && normalizedKey.Length > DataScopePrefix.Length
            )
            {
                scope = BindingMemberScope.Data;
                memberName = normalizedKey.Substring(DataScopePrefix.Length);
                return true;
            }

            if (normalizedKey.Contains(".", StringComparison.Ordinal))
            {
                scope = BindingMemberScope.Provider;
                return false;
            }

            scope = BindingMemberScope.Provider;
            memberName = normalizedKey;
            return true;
        }

        public static string CreateSerializedKey(BindingMemberScope scope, string memberName)
        {
            var normalizedMemberName = (memberName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalizedMemberName))
                return string.Empty;

            return scope == BindingMemberScope.Data
                ? $"{DataScopePrefix}{normalizedMemberName}"
                : normalizedMemberName;
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

        private static void AddBindableMembers(
            Type sourceType,
            BindingMemberScope scope,
            List<BindingMemberMetadata> members
        )
        {
            if (sourceType == null || members == null)
                return;

            var properties = sourceType.GetProperties(BindablePropertyFlags);
            for (var i = 0; i < properties.Length; i++)
            {
                if (TryCreateMetadata(properties[i], scope, out var metadata))
                    members.Add(metadata);
            }
        }

        private static string ResolveDisplayName(
            PropertyInfo property,
            BindableAttribute bindableAttribute,
            BindableCommandAttribute bindableCommandAttribute
        )
        {
            if (bindableAttribute != null && !string.IsNullOrWhiteSpace(bindableAttribute.DisplayName))
                return bindableAttribute.DisplayName.Trim();

            if (
                bindableCommandAttribute != null
                && !string.IsNullOrWhiteSpace(bindableCommandAttribute.DisplayName)
            )
            {
                return bindableCommandAttribute.DisplayName.Trim();
            }

            return property.Name;
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
