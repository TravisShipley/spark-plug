using UnityEngine;
using UnityEngine.EventSystems;

public sealed class LlamaSpawnerView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField]
    private LlamaView llamaPrefab;

    [SerializeField]
    private Transform sceneRoot;

    [SerializeField]
    private Vector3 spawnLocalPosition = new Vector3(0f, 1f, 0f);

    private void Awake()
    {
        if (llamaPrefab != null && sceneRoot != null)
            return;

        Debug.LogError("LlamaSpawnerView: llamaPrefab and sceneRoot must be assigned.", this);
        enabled = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left)
            return;

        var llama = Instantiate(llamaPrefab, sceneRoot, false);
        llama.transform.localPosition = spawnLocalPosition;
        llama.Despawn();
    }
}
