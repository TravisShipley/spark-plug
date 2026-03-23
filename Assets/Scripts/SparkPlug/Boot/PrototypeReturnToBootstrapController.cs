using UnityEngine;

public sealed class PrototypeReturnToBootstrapController : MonoBehaviour
{
    [SerializeField]
    private string bootstrapSceneName = "Bootstrap";

    [SerializeField]
    private KeyCode exitKey = KeyCode.Escape;

    [SerializeField]
    private UiScreenManager uiScreenManager;

    private void LateUpdate()
    {
        if (!Input.GetKeyDown(exitKey))
            return;

        if (uiScreenManager != null)
        {
            if (uiScreenManager.HasOpenScreens || uiScreenManager.ConsumedEscapeThisFrame)
                return;
        }

        PrototypeLaunchService.ReturnToBootstrap(bootstrapSceneName);
    }

    public void ReturnToBootstrap()
    {
        PrototypeLaunchService.ReturnToBootstrap(bootstrapSceneName);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (uiScreenManager == null)
            uiScreenManager = FindFirstObjectByType<UiScreenManager>();
    }
#endif
}
