using System;
using Ignition.Binding;
using UnityEngine;

public sealed class TestPanelView : DataProvider
{
    private TestPanelViewModel data;

    public void Bind(TestPanelViewModel vm)
    {
        this.data = vm;
        RebindChildren();
    }

    public override object GetBindingData() => this.data;

    public override Type GetBindingDataType() => typeof(TestPanelViewModel);
}
