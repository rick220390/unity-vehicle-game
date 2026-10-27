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
    [Tooltip("A looping tyre-squeal clip. The AudioSource is created automatically.")]
    public AudioClip tyreSquealClip;
    [Range(0f, 1f)] public float maximumSquealVolume = 0.8f;
    [Range(0.5f, 2f)] public float squealPitch = 1f;
    [Min(0.01f)] public float squealFadeSpeed = 5f;
    [Tooltip("Combined slip needed before tyre squeal begins.")]
    [Min(0.01f)] public float squealStartSlip = 0.22f;
    [Tooltip("Slip amount at which the squeal reaches full volume.")]
    [Min(0.02f)] public float squealFullSlip = 0.8f;

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

        squealSource = GetComponent<AudioSource>();
        if (squealSource == null)
            squealSource = gameObject.AddComponent<AudioSource>();

        squealSource.playOnAwake = false;
        squealSource.loop = true;
        squealSource.spatialBlend = 1f;
        squealSource.volume = 0f;
        squealSource.pitch = squealPitch;
        squealSource.clip = tyreSquealClip;

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

        float totalSlip = 0f;
        int slippingWheelCount = 0;

        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wheel = wheels[i];
            if (wheel == null)
                continue;

            bool grounded = wheel.GetGroundHit(out WheelHit hit);
            float slip = 0f;

            if (grounded)
            {
                // Sideways slip captures cornering/drifting; forward slip captures
                // wheelspin and braking lock. Combine both for a single effect signal.
                slip = Mathf.Sqrt(hit.sidewaysSlip * hit.sidewaysSlip +
                                  hit.forwardSlip * hit.forwardSlip);

                maximumWheelSlip = Mathf.Max(maximumWheelSlip, slip);
                totalSlip += slip;
                slippingWheelCount++;
            }

            UpdateWheelSmoke(i, wheel, grounded, grounded ? hit : default(WheelHit), slip);
        }

        float averageSlip = slippingWheelCount > 0 ? totalSlip / slippingWheelCount : 0f;
        FadeSqueal(averageSlip);
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

    private void FadeSqueal(float slip)
    {
        float targetVolume = 0f;
        if (tyreSquealClip != null && slip >= squealStartSlip)
        {
            targetVolume = Mathf.InverseLerp(
                squealStartSlip,
                Mathf.Max(squealStartSlip + 0.01f, squealFullSlip),
                slip) * maximumSquealVolume;
        }

        currentSquealVolume = Mathf.MoveTowards(
            currentSquealVolume,
            targetVolume,
            squealFadeSpeed * Time.deltaTime);

        if (squealSource == null)
            return;

        if (squealSource.clip != tyreSquealClip)
            squealSource.clip = tyreSquealClip;

        squealSource.pitch = squealPitch;
        squealSource.volume = currentSquealVolume;

        if (tyreSquealClip != null && currentSquealVolume > 0.01f)
        {
            if (!squealSource.isPlaying)
                squealSource.Play();
        }
        else if (squealSource.isPlaying && currentSquealVolume <= 0.01f)
        {
            squealSource.Stop();
        }
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
