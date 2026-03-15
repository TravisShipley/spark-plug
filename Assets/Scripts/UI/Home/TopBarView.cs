using System;
using Ignition.Binding;
using UnityEngine;

public sealed class TopBarView : DataProvider
{
    private TopBarViewModel data;

    public void Bind(TopBarViewModel vm)
    {
        data = vm;
        RebindChildren();
    }

    public override object GetBindingData() => data;

    public override Type GetBindingDataType() => typeof(TopBarViewModel);
}
