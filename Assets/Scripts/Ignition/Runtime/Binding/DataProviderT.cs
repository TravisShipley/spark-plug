using System;

namespace Ignition.Binding
{
    public abstract class DataProvider<TData> : DataProvider
    {
        protected TData Data { get; private set; }

        public virtual void Bind(TData data)
        {
            Data = data;
            RebindChildren();
        }

        public override object GetBindingData() => Data;

        public override Type GetBindingDataType() => typeof(TData);
    }
}
