using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.Device_Management;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Basis.Scripts.UI.UI_Panels
{
    public abstract class BasisUIBase : MonoBehaviour
    {
        public abstract void InitializeEvent();
        public abstract void DestroyEvent();
        public void CloseThisMenu()
        {
            BasisUIManagement.RemoveUI(this);
            DestroyEvent();

#if !UNITY_WEBGL || UNITY_EDITOR
            Addressables.ReleaseInstance(this.gameObject);
#endif
            Destroy(this.gameObject);
        }
        public static BasisUIBase OpenMenuNow(string resource)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            GameObject RAC = Instantiate(
                AddressableAssets.GetPrefab(resource),
                BasisDeviceManagement.Instance.transform,
                true);
#else
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op = Addressables.InstantiateAsync(resource, BasisDeviceManagement.Instance.transform, true);
            GameObject RAC = op.WaitForCompletion();
#endif
            BasisUIBase BasisUIBase = BasisHelpers.GetOrAddComponent<BasisUIBase>(RAC);
            BasisUIManagement.AddUI(BasisUIBase);
            BasisUIBase.InitializeEvent();
            return BasisUIBase;
        }
    }
}
