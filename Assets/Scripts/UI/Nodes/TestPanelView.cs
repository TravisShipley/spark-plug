using System;
using UnityEngine;

public sealed class TestPanelView : DataProvider
{
    private TestPanelViewModel viewModel;

    public void Bind(TestPanelViewModel vm)
    {
        viewModel = vm;
        RebindChildren();
    }

    public override object GetBindingData() => viewModel;

    public override Type GetBindingDataType() => typeof(TestPanelViewModel);
}
