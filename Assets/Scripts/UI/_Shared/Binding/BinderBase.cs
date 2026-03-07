using System;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class BinderBase : MonoBehaviour, IBinder
{
    [FormerlySerializedAs("sourceProvider")]
    [SerializeField]
    private DataProvider dataProvider;

    [SerializeField]
    private string selectedMemberName;

    public IBindingDataProvider DataProvider => dataProvider;
    public string SelectedMemberName => selectedMemberName;
    public abstract Type BindingValueType { get; }

    public abstract void Rebind();

    public virtual string GetEditorWarning()
    {
        return null;
    }
}
