using UnityEngine;
using UnityEngine.EventSystems;

public sealed class LlamaSpawnerView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField]
    private LlamaView llamaPrefab;

    [SerializeField]
    private Transform sceneRoot;

    private Collider spawnerCollider;

    private void Awake()
    {
        if (llamaPrefab != null && sceneRoot != null && TryGetComponent(out spawnerCollider))
            return;

        Debug.LogError("LlamaSpawnerView: llamaPrefab, sceneRoot, and a Collider are required.", this);
        enabled = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left)
            return;

        var llama = Instantiate(llamaPrefab, sceneRoot, false);
        llama.transform.position = transform.position;
        llama.Launch(spawnerCollider);
        llama.Despawn();
    }
}
