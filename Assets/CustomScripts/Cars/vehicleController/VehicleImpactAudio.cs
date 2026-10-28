using UnityEngine;

/// <summary>
/// Adds landing and collision impact audio to a vehicle without changing its driving physics.
/// Add this component to the same GameObject as Vehicle and Rigidbody, then assign the clips.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Vehicle))]
[RequireComponent(typeof(Rigidbody))]
public class VehicleImpactAudio : MonoBehaviour
{
    [Header("Impact clips")]
    [Tooltip("Use for a landing, light body impact, or low-speed scrape.")]
    public AudioClip landingAndBodyImpactClip;
    [Tooltip("Use for a harder collision with a wall, obstacle, or another vehicle.")]
    public AudioClip crashClip;

    [Header("Landing detection")]
    [Min(0f)] public float minimumLandingSpeed = 1.2f;
    [Tooltip("A landing at or above this speed uses the crash clip if assigned; otherwise it uses the landing clip.")]
    [Min(0f)] public float heavyLandingSpeed = 9f;

    [Header("Collision detection")]
    [Min(0f)] public float minimumCollisionSpeed = 1.2f;
    [Min(0f)] public float crashCollisionSpeed = 7f;

    [Header("Playback")]
    [Range(0f, 1f)] public float landingVolume = 1f;
    [Range(0f, 1f)] public float bodyImpactVolume = 1f;
    [Range(0f, 1f)] public float crashVolume = 1f;
    [Min(0f)] public float impactCooldown = 0.3f;
    [Min(1f)] public float maxDistance = 120f;
    [Min(0f)] public float minDistance = 8f;
    [Tooltip("Enable to print which events trigger and warn if a required sound clip is missing.")]
    public bool debugLogging = true;

    private Vehicle vehicle;
    private Rigidbody body;
    private AudioSource impactSource;
    private bool wasGrounded;
    private float lastAirborneVerticalSpeed;
    private float nextImpactTime;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        body = GetComponent<Rigidbody>();

        // Always create a dedicated source. Reusing GetComponent<AudioSource>() can
        // hijack the engine source and change its loop/spatial/audio settings.
        impactSource = gameObject.AddComponent<AudioSource>();
        impactSource.playOnAwake = false;
        impactSource.loop = false;
        impactSource.spatialBlend = 0.65f;
        impactSource.rolloffMode = AudioRolloffMode.Logarithmic;
        impactSource.minDistance = minDistance;
        impactSource.maxDistance = maxDistance;
        impactSource.dopplerLevel = 0f;

        wasGrounded = AreWheelsGrounded();

        if (debugLogging)
        {
            if (landingAndBodyImpactClip == null)
                Debug.LogWarning($"[{nameof(VehicleImpactAudio)}] Landing/body impact clip is not assigned on '{name}'.", this);
            if (crashClip == null)
                Debug.LogWarning($"[{nameof(VehicleImpactAudio)}] Crash clip is not assigned on '{name}'.", this);
        }
    }

    private void FixedUpdate()
    {
        bool grounded = AreWheelsGrounded();

        if (!grounded)
        {
            lastAirborneVerticalSpeed = body.velocity.y;
        }
        else if (!wasGrounded)
        {
            float landingSpeed = Mathf.Abs(lastAirborneVerticalSpeed);
            if (debugLogging)
                Debug.Log($"[{nameof(VehicleImpactAudio)}] Landing detected on '{name}', vertical speed {landingSpeed:F1} m/s.", this);

            if (landingSpeed >= minimumLandingSpeed)
            {
                bool heavyLanding = landingSpeed >= heavyLandingSpeed && crashClip != null;
                PlayImpact(
                    heavyLanding ? crashClip : landingAndBodyImpactClip,
                    heavyLanding ? crashVolume : landingVolume,
                    heavyLanding ? "heavy landing" : "landing");
            }
        }

        wasGrounded = grounded;
    }

    private bool AreWheelsGrounded()
    {
        if (vehicle == null || vehicle.wheelColliders == null || vehicle.wheelColliders.Length == 0)
            return false;

        for (int i = 0; i < vehicle.wheelColliders.Length; i++)
        {
            WheelCollider wheel = vehicle.wheelColliders[i];
            if (wheel != null && wheel.enabled && wheel.GetGroundHit(out _))
                return true;
        }

        return false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (debugLogging)
            Debug.Log($"[{nameof(VehicleImpactAudio)}] Collision with '{collision.gameObject.name}' on '{name}', relative speed {impactSpeed:F1} m/s.", this);

        if (impactSpeed < minimumCollisionSpeed)
            return;

        bool isCrash = impactSpeed >= crashCollisionSpeed && crashClip != null;
        AudioClip clip = isCrash ? crashClip : landingAndBodyImpactClip;
        float volume = isCrash ? crashVolume : bodyImpactVolume;

        PlayImpact(clip, volume, isCrash ? "crash" : "body impact");
    }

    private void PlayImpact(AudioClip clip, float volume, string reason)
    {
        if (Time.time < nextImpactTime)
            return;

        if (clip == null)
        {
            if (debugLogging)
                Debug.LogWarning($"[{nameof(VehicleImpactAudio)}] {reason} triggered on '{name}', but its AudioClip is not assigned.", this);
            return;
        }

        if (impactSource == null)
        {
            Debug.LogWarning($"[{nameof(VehicleImpactAudio)}] No impact AudioSource exists on '{name}'.", this);
            return;
        }

        impactSource.minDistance = minDistance;
        impactSource.maxDistance = maxDistance;
        impactSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        nextImpactTime = Time.time + impactCooldown;

        if (debugLogging)
            Debug.Log($"[{nameof(VehicleImpactAudio)}] Playing '{clip.name}' for {reason} on '{name}' (volume {volume:F2}).", this);
    }
}
