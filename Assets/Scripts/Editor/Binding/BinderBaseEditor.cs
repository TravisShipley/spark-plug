using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BinderBase), true)]
public sealed class BinderBaseEditor : Editor
{
    private const string DataProviderFieldName = "dataProvider";
    private const string SelectedMemberNameFieldName = "selectedMemberName";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawScriptField();

        var dataProviderProperty = serializedObject.FindProperty(DataProviderFieldName);
        var selectedMemberNameProperty = serializedObject.FindProperty(SelectedMemberNameFieldName);

        EditorGUILayout.PropertyField(dataProviderProperty, new GUIContent("Data Provider"));
        DrawBindingMemberField(
            (BinderBase)target,
            dataProviderProperty,
            selectedMemberNameProperty
        );
        DrawRemainingProperties();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawBindingMemberField(
        BinderBase binder,
        SerializedProperty dataProviderProperty,
        SerializedProperty selectedMemberNameProperty
    )
    {
        var dataProviderComponent = dataProviderProperty.objectReferenceValue as DataProvider;
        if (dataProviderComponent == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a data provider that implements IBindingDataProvider.",
                MessageType.Info
            );
            DrawMemberPopup(selectedMemberNameProperty, new List<BindingMemberMetadata>(), null);
            DrawEditorWarning(binder);
            return;
        }

        if (dataProviderComponent is not IBindingDataProvider)
        {
            EditorGUILayout.HelpBox(
                $"{dataProviderComponent.GetType().Name} does not implement IBindingDataProvider.",
                MessageType.Error
            );
            DrawMemberPopup(selectedMemberNameProperty, new List<BindingMemberMetadata>(), null);
            DrawEditorWarning(binder);
            return;
        }

        var dataType = ResolveDataType(dataProviderComponent);
        if (dataType == null)
        {
            EditorGUILayout.HelpBox(
                "The data provider could not declare a binding data type. Implement IBindingDataTypeProvider for prefab authoring.",
                MessageType.Warning
            );
            DrawMemberPopup(selectedMemberNameProperty, new List<BindingMemberMetadata>(), null);
            DrawEditorWarning(binder);
            return;
        }

        EditorGUILayout.LabelField("Binding Data Type", dataType.Name);

        var compatibleMembers = GetCompatibleMembers(dataType, binder.BindingValueType);
        if (compatibleMembers.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No [Bindable] members on {dataType.Name} emit {binder.BindingValueType.Name}.",
                MessageType.Warning
            );
        }

        DrawMemberPopup(selectedMemberNameProperty, compatibleMembers, binder.BindingValueType);
        DrawEditorWarning(binder);
    }

    private void DrawMemberPopup(
        SerializedProperty selectedMemberNameProperty,
        List<BindingMemberMetadata> members,
        Type bindingValueType
    )
    {
        var optionLabels = new List<string> { "<None>" };
        var optionValues = new List<string> { string.Empty };
        var currentValue = selectedMemberNameProperty.stringValue;
        var hasCurrentValue = !string.IsNullOrWhiteSpace(currentValue);

        if (hasCurrentValue && !ContainsMember(members, currentValue))
        {
            optionLabels.Add($"Missing: {currentValue}");
            optionValues.Add(currentValue);

            if (bindingValueType != null)
            {
                EditorGUILayout.HelpBox(
                    $"'{currentValue}' is not a compatible {bindingValueType.Name} binding on the current data type.",
                    MessageType.Warning
                );
            }
        }

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            optionLabels.Add(
                member.DisplayName == member.MemberName
                    ? member.DisplayName
                    : $"{member.DisplayName} ({member.MemberName})"
            );
            optionValues.Add(member.MemberName);
        }

        var selectedIndex = Math.Max(0, optionValues.IndexOf(currentValue));
        var nextIndex = EditorGUILayout.Popup(
            "Bindable Member",
            selectedIndex,
            optionLabels.ToArray()
        );
        selectedMemberNameProperty.stringValue = optionValues[nextIndex];
    }

    private void DrawRemainingProperties()
    {
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            DataProviderFieldName,
            SelectedMemberNameFieldName
        );
    }

    private void DrawEditorWarning(BinderBase binder)
    {
        var warning = binder.GetEditorWarning();
        if (!string.IsNullOrWhiteSpace(warning))
            EditorGUILayout.HelpBox(warning, MessageType.Warning);
    }

    private void DrawScriptField()
    {
        using (new EditorGUI.DisabledScope(true))
        {
            var script = MonoScript.FromMonoBehaviour((MonoBehaviour)target);
            EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
        }
    }

    private static bool ContainsMember(List<BindingMemberMetadata> members, string memberName)
    {
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].MemberName == memberName)
                return true;
        }

        return false;
    }

    private static List<BindingMemberMetadata> GetCompatibleMembers(
        Type dataType,
        Type bindingValueType
    )
    {
        var result = new List<BindingMemberMetadata>();
        var members = BindingMetadataUtility.GetBindableMembers(dataType);
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member.ValueType == bindingValueType)
                result.Add(member);
        }

        result.Sort(
            (left, right) =>
                string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
        );
        return result;
    }

    private static Type ResolveDataType(DataProvider dataProviderComponent)
    {
        if (Application.isPlaying && dataProviderComponent is IBindingDataProvider runtimeProvider)
        {
            try
            {
                var runtimeData = runtimeProvider.GetBindingData();
                if (runtimeData != null)
                    return runtimeData.GetType();
            }
            catch (Exception) { }
        }

        if (dataProviderComponent is IBindingDataTypeProvider dataTypeProvider)
        {
            try
            {
                return dataTypeProvider.GetBindingDataType();
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
}
