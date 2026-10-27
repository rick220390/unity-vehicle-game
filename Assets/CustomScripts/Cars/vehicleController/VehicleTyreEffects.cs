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
    [Min(0.01f)] public float squealFadeSpeed = 8f;
    [Tooltip("Combined slip needed before tyre squeal begins.")]
    [Min(0.01f)] public float squealStartSlip = 0.16f;
    [Tooltip("Slip amount at which the squeal reaches full volume.")]
    [Min(0.02f)] public float squealFullSlip = 0.55f;
    [Tooltip("How 3D the sound is. Lower values make squeal easier to hear at the camera.")]
    [Range(0f, 1f)] public float squealSpatialBlend = 0.25f;
    [Tooltip("Distance at which the squeal is heard at full volume.")]
    [Min(0.1f)] public float squealMinDistance = 12f;
    [Tooltip("Distance at which the squeal becomes inaudible.")]
    [Min(1f)] public float squealMaxDistance = 100f;
    [Tooltip("Seconds of overlap between loop repetitions to hide the loop point.")]
    [Range(0.05f, 1f)] public float loopCrossfadeDuration = 0.35f;

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
    private AudioSource squealSourceB;
    private AudioSource activeLoopSource;
    private AudioSource nextLoopSource;
    private bool isCrossfading;
    private float crossfadeTimer;
    private float[] particleEmissionRemainders;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();

        // Use a dedicated source so tyre squeal cannot overwrite the engine audio source.
        squealSource = CreateSquealSource();
        squealSourceB = CreateSquealSource();
        activeLoopSource = squealSource;
        nextLoopSource = squealSourceB;

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
                // Sideways slip captures cornering/drifting; forward slip captures
                // wheelspin and braking lock. Use the strongest wheel's slip for audio
                // so non-slipping wheels do not dilute the sound of a skid.
                slip = Mathf.Sqrt(hit.sidewaysSlip * hit.sidewaysSlip +
                                  hit.forwardSlip * hit.forwardSlip);
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
        source.loop = false; // We manually crossfade between two overlapping plays.
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

        if (squealSource == null || squealSourceB == null)
            return;

        ConfigureSquealSource(squealSource);
        ConfigureSquealSource(squealSourceB);

        if (tyreSquealClip == null || currentSquealVolume <= 0.01f)
        {
            squealSource.Stop();
            squealSourceB.Stop();
            squealSource.volume = 0f;
            squealSourceB.volume = 0f;
            isCrossfading = false;
            return;
        }

        if (!activeLoopSource.isPlaying)
        {
            activeLoopSource.clip = tyreSquealClip;
            activeLoopSource.time = 0f;
            activeLoopSource.volume = currentSquealVolume;
            activeLoopSource.Play();
            nextLoopSource.Stop();
            nextLoopSource.volume = 0f;
            isCrossfading = false;
        }

        float clipLength = tyreSquealClip.length;
        float overlap = Mathf.Clamp(loopCrossfadeDuration, 0.05f, Mathf.Max(0.05f, clipLength * 0.45f));

        if (!isCrossfading && clipLength > 0.1f &&
            activeLoopSource.time >= clipLength - overlap)
        {
            nextLoopSource.Stop();
            nextLoopSource.clip = tyreSquealClip;
            nextLoopSource.time = 0f;
            nextLoopSource.volume = 0f;
            nextLoopSource.Play();
            isCrossfading = true;
            crossfadeTimer = 0f;
        }

        if (isCrossfading)
        {
            crossfadeTimer += Time.deltaTime;
            float blend = Mathf.Clamp01(crossfadeTimer / overlap);
            activeLoopSource.volume = currentSquealVolume * (1f - blend);
            nextLoopSource.volume = currentSquealVolume * blend;

            if (blend >= 1f)
            {
                activeLoopSource.Stop();
                AudioSource oldSource = activeLoopSource;
                activeLoopSource = nextLoopSource;
                nextLoopSource = oldSource;
                activeLoopSource.volume = currentSquealVolume;
                nextLoopSource.volume = 0f;
                isCrossfading = false;
            }
        }
        else
        {
            activeLoopSource.volume = currentSquealVolume;
            nextLoopSource.volume = 0f;
        }
    }

    private void ConfigureSquealSource(AudioSource source)
    {
        source.spatialBlend = squealSpatialBlend;
        source.minDistance = squealMinDistance;
        source.maxDistance = Mathf.Max(squealMinDistance + 0.1f, squealMaxDistance);
        source.pitch = squealPitch;
        source.clip = tyreSquealClip;
    }

    private void OnDisable()
    {
        if (squealSource != null)
        {
            squealSource.Stop();
            squealSource.volume = 0f;
        }

        if (squealSourceB != null)
        {
            squealSourceB.Stop();
            squealSourceB.volume = 0f;
        }
    }
}
