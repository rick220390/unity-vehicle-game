using UnityEngine;

/// <summary>
/// Adds wheel-slip-driven tyre squeal and tyre smoke to a Vehicle.
/// Attach this to the same GameObject as Vehicle and assign a looping squeal clip
/// plus optional particle systems (one per wheel, matching Vehicle.wheelColliders).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Vehicle))]
public class VehicleTyreEffects : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("A looping tyre-squeal clip. A dedicated AudioSource is created automatically.")]
    public AudioClip tyreSquealClip;
    [Range(0f, 1f)] public float maximumSquealVolume = 1f;
    [Range(0.5f, 2f)] public float squealPitch = 1f;
    [Tooltip("Absolute wheel slip needed to trigger full-volume squeal. Lower this if skids are not triggering.")]
    [Min(0.001f)] public float squealStartSlip = 0.015f;
    [Tooltip("Keep this low for consistent volume from the camera and vehicle.")]
    [Range(0f, 1f)] public float squealSpatialBlend = 0.1f;
    [Tooltip("Distance at which the squeal is heard at full volume.")]
    [Min(0.1f)] public float squealMinDistance = 20f;
    [Tooltip("Distance at which the squeal becomes quieter.")]
    [Min(1f)] public float squealMaxDistance = 120f;

    [Header("Smoke")]
    [Tooltip("Optional particle systems, one per wheel, in the same order as Vehicle.wheelColliders.")]
    public ParticleSystem[] wheelSmoke;
    [Min(0.01f)] public float smokeStartSlip = 0.3f;
    [Min(0.02f)] public float smokeFullSlip = 1.0f;
    [Range(0f, 1f)] public float smokeEmissionMultiplier = 0.65f;
    [Min(0)] public int maximumParticlesPerSecondPerWheel = 45;
    [Tooltip("Only emit smoke when the wheel is grounded.")]
    public bool requireGroundContact = true;

    [Header("Debug")]
    public float maximumWheelSlip;
    public float currentSquealVolume;

    private Vehicle vehicle;
    private AudioSource squealSource;
    private float[] particleEmissionRemainders;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();

        // Use a dedicated source so tyre squeal cannot overwrite the engine audio source.
        squealSource = CreateSquealSource();

        int wheelCount = vehicle.wheelColliders != null ? vehicle.wheelColliders.Length : 0;
        particleEmissionRemainders = new float[wheelCount];

        if (wheelSmoke != null)
        {
            foreach (ParticleSystem smoke in wheelSmoke)
            {
                if (smoke == null)
                    continue;

                var emission = smoke.emission;
                emission.rateOverTime = 0f;
                if (!smoke.isPlaying)
                    smoke.Play();
            }
        }
    }

    private void Update()
    {
        if (vehicle == null)
            return;

        WheelCollider[] wheels = vehicle.wheelColliders;
        maximumWheelSlip = 0f;

        if (wheels == null || wheels.Length == 0)
        {
            FadeSqueal(0f);
            return;
        }

        if (particleEmissionRemainders == null || particleEmissionRemainders.Length != wheels.Length)
            particleEmissionRemainders = new float[wheels.Length];

        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wheel = wheels[i];
            if (wheel == null)
                continue;

            bool grounded = wheel.GetGroundHit(out WheelHit hit);
            float slip = 0f;

            if (grounded)
            {
                // Use the larger absolute slip value. This catches sideways drifts,
                // wheelspin and braking lock without making the trigger harder to reach.
                slip = Mathf.Max(Mathf.Abs(hit.sidewaysSlip), Mathf.Abs(hit.forwardSlip));
                maximumWheelSlip = Mathf.Max(maximumWheelSlip, slip);
            }

            UpdateWheelSmoke(i, wheel, grounded, grounded ? hit : default(WheelHit), slip);
        }

        FadeSqueal(maximumWheelSlip);
    }

    private void UpdateWheelSmoke(int index, WheelCollider wheel, bool grounded, WheelHit hit, float slip)
    {
        if (wheelSmoke == null || index >= wheelSmoke.Length || wheelSmoke[index] == null)
            return;

        ParticleSystem smoke = wheelSmoke[index];
        if ((requireGroundContact && !grounded) || slip < smokeStartSlip ||
            maximumParticlesPerSecondPerWheel <= 0)
            return;

        float intensity = Mathf.InverseLerp(smokeStartSlip, Mathf.Max(smokeStartSlip + 0.01f, smokeFullSlip), slip);
        float particlesPerSecond = maximumParticlesPerSecondPerWheel * intensity * smokeEmissionMultiplier;
        particleEmissionRemainders[index] += particlesPerSecond * Time.deltaTime;

        int count = Mathf.FloorToInt(particleEmissionRemainders[index]);
        if (count <= 0)
            return;

        particleEmissionRemainders[index] -= count;
        smoke.transform.position = hit.point;
        smoke.Emit(count);
    }

    private AudioSource CreateSquealSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true; // Native looping avoids overlapping copies and echo.
        source.spatialBlend = squealSpatialBlend;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = squealMinDistance;
        source.maxDistance = squealMaxDistance;
        source.volume = 0f;
        source.pitch = squealPitch;
        source.clip = tyreSquealClip;
        return source;
    }

    private void FadeSqueal(float slip)
    {
        if (squealSource == null)
            return;

        ConfigureSquealSource(squealSource);

        // Keep the squeal at the Inspector's selected volume for the whole skid.
        // Do not gradually ramp the sound up or leave it fading after grip returns.
        bool isSkidding = tyreSquealClip != null && slip >= squealStartSlip;
        currentSquealVolume = isSkidding ? maximumSquealVolume : 0f;
        squealSource.volume = currentSquealVolume;

        if (!isSkidding)
        {
            if (squealSource.isPlaying)
                squealSource.Stop();
            return;
        }

        if (!squealSource.isPlaying)
            squealSource.Play();
    }

    private void ConfigureSquealSource(AudioSource source)
    {
        source.spatialBlend = squealSpatialBlend;
        source.minDistance = squealMinDistance;
        source.maxDistance = Mathf.Max(squealMinDistance + 0.1f, squealMaxDistance);
        source.pitch = squealPitch;

        if (source.clip != tyreSquealClip)
            source.clip = tyreSquealClip;

        source.loop = true;
        source.dopplerLevel = 0f;
        source.playOnAwake = false;
    }

    private void OnDisable()
    {
        if (squealSource != null)
        {
            squealSource.Stop();
            squealSource.volume = 0f;
        }
    }
}
