using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class LlamaView : MonoBehaviour
{
    private const float MinLaunchHeight = 0.5f;
    private const float MaxLaunchHeight = 1f;
    private const float MinLaunchElevationDegrees = 60f;
    private const float MaxLaunchElevationDegrees = 85f;

    [SerializeField]
    private float LlamaLifetimeSeconds = 1f;

    [SerializeField]
    private float DespawnDurationSeconds = 1.1f;

    private Rigidbody body;
    private bool isDespawning;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public void Launch()
    {
        var launchHeight = Random.Range(MinLaunchHeight, MaxLaunchHeight);
        var elevation = Random.Range(MinLaunchElevationDegrees, MaxLaunchElevationDegrees)
            * Mathf.Deg2Rad;
        var azimuth = Random.Range(0f, 2f * Mathf.PI);

        // Keep the vertical speed tied to the target rise regardless of launch angle.
        var verticalSpeed = Mathf.Sqrt(2f * Mathf.Max(0f, -Physics.gravity.y) * launchHeight);
        var horizontalSpeed = verticalSpeed / Mathf.Tan(elevation);
        body.linearVelocity = new Vector3(
            Mathf.Cos(azimuth) * horizontalSpeed,
            verticalSpeed,
            Mathf.Sin(azimuth) * horizontalSpeed
        );
    }

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
