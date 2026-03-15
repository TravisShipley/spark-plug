using System;
using UnityEngine;

namespace Ignition.Navigation
{
    public abstract class UiScreenView<TData> : UiScreenView
    {
        protected TData Data { get; private set; }

        public virtual void Bind(TData data)
        {
            this.Data = data;
            RebindChildren();
        }

        public override object GetBindingData() => this.Data;

        public override Type GetBindingDataType() => typeof(TData);

        public override void OnBeforeShow(object payload)
        {
            if (payload is not TData data)
            {
                Debug.LogError($"{GetType().Name}: Expected {typeof(TData).Name} payload.", this);
                return;
            }

            Bind(data);
        }
    }
}
