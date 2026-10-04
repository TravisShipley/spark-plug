using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class LlamaView : MonoBehaviour
{
    private const float LaunchClearance = 0.02f;

    [SerializeField]
    private float MinLaunchHeight = 0.1f;

    [SerializeField]
    private float MaxLaunchHeight = 0.250f;

    [SerializeField]
    private float MinLaunchElevationDegrees = 60f;

    [SerializeField]
    private float MaxLaunchElevationDegrees = 85f;

    [SerializeField]
    [Tooltip("Minimum and maximum launch spin speed in degrees per second.")]
    private Vector2 launchSpinDegreesPerSecond = new Vector2(45f, 90f);

    [SerializeField]
    private float LlamaLifetimeSeconds = 1f;

    [SerializeField]
    private float DespawnDurationSeconds = 1.1f;

    private Rigidbody body;
    private BoxCollider bodyCollider;
    private bool isDespawning;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<BoxCollider>();
    }

    public void Launch(Collider spawnerCollider = null)
    {
        var launchHeight = Random.Range(MinLaunchHeight, MaxLaunchHeight);
        if (spawnerCollider != null)
        {
            PlaceAboveSpawner(spawnerCollider);
            Physics.IgnoreCollision(bodyCollider, spawnerCollider, true);
            StartCoroutine(RestoreSpawnerCollisionWhenClear(spawnerCollider));
        }

        var elevation =
            Random.Range(MinLaunchElevationDegrees, MaxLaunchElevationDegrees) * Mathf.Deg2Rad;
        // Azimuth is the horizontal direction around the vertical Y axis, measured here in radians.
        var azimuth = Random.Range(0f, 2f * Mathf.PI);

        // Keep the vertical speed tied to the target rise regardless of launch angle.
        var verticalSpeed = Mathf.Sqrt(2f * Mathf.Max(0f, -Physics.gravity.y) * launchHeight);
        var horizontalSpeed = verticalSpeed / Mathf.Tan(elevation);
        body.linearVelocity = new Vector3(
            Mathf.Cos(azimuth) * horizontalSpeed,
            verticalSpeed,
            Mathf.Sin(azimuth) * horizontalSpeed
        );

        var spinSpeed = Random.Range(launchSpinDegreesPerSecond.x, launchSpinDegreesPerSecond.y);
        body.angularVelocity = Random.onUnitSphere * (spinSpeed * Mathf.Deg2Rad);
    }

    private void PlaceAboveSpawner(Collider spawnerCollider)
    {
        // Use the current transform so positioning does not depend on a physics sync.
        var halfSize = bodyCollider.size * 0.5f;
        var halfHeight =
            Mathf.Abs(transform.TransformVector(Vector3.right * halfSize.x).y)
            + Mathf.Abs(transform.TransformVector(Vector3.up * halfSize.y).y)
            + Mathf.Abs(transform.TransformVector(Vector3.forward * halfSize.z).y);
        var bottom = transform.TransformPoint(bodyCollider.center).y - halfHeight;
        var position = transform.position;
        position.y += spawnerCollider.bounds.max.y + LaunchClearance - bottom;
        body.position = position;
    }

    private IEnumerator RestoreSpawnerCollisionWhenClear(Collider spawnerCollider)
    {
        var waitForPhysics = new WaitForFixedUpdate();
        // Wait for the first physics step so bounds reflect the spawn position.
        do
        {
            yield return waitForPhysics;
        } while (spawnerCollider != null && bodyCollider.bounds.Intersects(spawnerCollider.bounds));

        if (spawnerCollider != null)
            Physics.IgnoreCollision(bodyCollider, spawnerCollider, false);
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
