using System;
using UnityEngine;

namespace Ignition.Binding
{
    public abstract class DataProvider
        : MonoBehaviour,
            IBindingDataProvider,
            IBindingDataTypeProvider
    {
        public abstract object GetBindingData();
        public abstract Type GetBindingDataType();

        protected void RebindChildren()
        {
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IBinder binder)
                    binder.Rebind();
            }
        }
    }
}
