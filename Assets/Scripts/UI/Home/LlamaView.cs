using System.Collections;
using UnityEngine;

public sealed class LlamaView : MonoBehaviour
{
    [SerializeField]
    private float LlamaLifetimeSeconds = 1f;

    [SerializeField]
    private float DespawnDurationSeconds = 1.1f;

    private bool isDespawning;

    public void Despawn()
    {
        if (isDespawning)
            return;

        isDespawning = true;
        StartCoroutine(DespawnAfterDelay());
    }

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(LlamaLifetimeSeconds);

        var initialScale = transform.localScale;
        var elapsedSeconds = 0f;
        while (elapsedSeconds < DespawnDurationSeconds)
        {
            elapsedSeconds += Time.deltaTime;
            transform.localScale = Vector3.Lerp(
                initialScale,
                Vector3.zero,
                elapsedSeconds / DespawnDurationSeconds
            );
            yield return null;
        }

        transform.localScale = Vector3.zero;
        Destroy(gameObject);
    }
}
