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
    [Tooltip("Ignore landings below this vertical impact speed, in metres per second.")]
    [Min(0f)] public float minimumLandingSpeed = 2.2f;
    [Tooltip("A landing at or above this speed uses the crash clip if assigned; otherwise it uses the landing clip.")]
    [Min(0f)] public float heavyLandingSpeed = 9f;

    [Header("Collision detection")]
    [Tooltip("Ignore gentle touches below this collision relative speed, in metres per second.")]
    [Min(0f)] public float minimumCollisionSpeed = 2f;
    [Tooltip("Collisions at or above this speed use the crash clip.")]
    [Min(0f)] public float crashCollisionSpeed = 7f;

    [Header("Playback")]
    [Range(0f, 1f)] public float landingVolume = 0.8f;
    [Range(0f, 1f)] public float bodyImpactVolume = 0.75f;
    [Range(0f, 1f)] public float crashVolume = 1f;
    [Tooltip("Minimum time between impact sounds, preventing repeated suspension bounces from spamming audio.")]
    [Min(0f)] public float impactCooldown = 0.3f;
    [Min(1f)] public float maxDistance = 70f;

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

        impactSource = GetComponent<AudioSource>();
        if (impactSource == null)
            impactSource = gameObject.AddComponent<AudioSource>();

        impactSource.playOnAwake = false;
        impactSource.loop = false;
        impactSource.spatialBlend = 1f;
        impactSource.rolloffMode = AudioRolloffMode.Logarithmic;
        impactSource.minDistance = 3f;
        impactSource.maxDistance = maxDistance;

        wasGrounded = AreWheelsGrounded();
    }

    private void FixedUpdate()
    {
        bool grounded = AreWheelsGrounded();

        if (!grounded)
        {
            // Keep the last airborne vertical speed so touchdown is measured before
            // the WheelColliders resolve the landing and reduce the Rigidbody velocity.
            lastAirborneVerticalSpeed = body.velocity.y;
        }
        else if (!wasGrounded)
        {
            float landingSpeed = Mathf.Abs(lastAirborneVerticalSpeed);
            if (landingSpeed >= minimumLandingSpeed)
            {
                AudioClip clip = landingSpeed >= heavyLandingSpeed && crashClip != null
                    ? crashClip
                    : landingAndBodyImpactClip;
                float volume = clip == crashClip && crashClip != null
                    ? crashVolume
                    : landingVolume;

                PlayImpact(clip, volume);
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
        if (collision == null || collision.contactCount == 0)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < minimumCollisionSpeed)
            return;

        bool isCrash = impactSpeed >= crashCollisionSpeed && crashClip != null;
        AudioClip clip = isCrash ? crashClip : landingAndBodyImpactClip;
        float volume = isCrash ? crashVolume : bodyImpactVolume;

        PlayImpact(clip, volume);
    }

    private void PlayImpact(AudioClip clip, float volume)
    {
        if (clip == null || impactSource == null || Time.time < nextImpactTime)
            return;

        impactSource.maxDistance = maxDistance;
        impactSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        nextImpactTime = Time.time + impactCooldown;
    }
}
