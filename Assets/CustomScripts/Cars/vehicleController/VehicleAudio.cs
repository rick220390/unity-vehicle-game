using UnityEngine;

/// <summary>
/// Audio for a single vehicle. Add this alongside Vehicle and assign the clips
/// in the Inspector. The Vehicle controller itself does not need to be changed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Vehicle))]
public class VehicleAudio : MonoBehaviour
{
    [Header("Audio clips")]
    [Tooltip("A seamless loop of the engine idling.")]
    public AudioClip engineIdle;
    [Tooltip("A sustained engine rev/running loop. Disable Rev Sound Is Loop if this is a short one-shot recording.")]
    public AudioClip engineRev;
    [Tooltip("Played once when a driver enters and starts the vehicle.")]
    public AudioClip engineStart;
    [Tooltip("Played once when the driver exits and shuts the vehicle down.")]
    public AudioClip engineShutdown;

    [Header("Rev sound")]
    [Tooltip("Enable if Engine Rev is a sustained loop. Disable if it is a short rev sound.")]
    public bool revSoundIsLoop = true;
    [Range(0f, 1f)] public float idleVolume = 1f;
    [Range(0f, 1f)] public float revVolume = 1f;
    [Min(0.1f)] public float idlePitch = 0.95f;
    [Min(0.1f)] public float revPitchAtIdle = 0.9f;
    [Min(0.1f)] public float revPitchAtFullThrottle = 1.45f;
    [Min(0.1f)] public float pitchResponse = 4f;
    [Min(0.1f)] public float volumeResponse = 4f;
    [Header("Simulated RPM and gears")]
    [Tooltip("For a short accelerating/rev recording, enable this to ramp the sound up and restart it at simulated gear changes.")]
    public bool simulateGearShifts = true;
    [Range(2, 8)] public int simulatedGears = 5;
    [Range(0.5f, 0.99f)] public float shiftRpm = 0.88f;
    [Range(0.1f, 0.8f)] public float rpmAfterShift = 0.48f;
    [Min(0.1f)] public float rpmRiseResponse = 0.8f;
    [Min(0.1f)] public float rpmFallResponse = 0.55f;
    [Min(0f)] public float startDistance = 8f;
    [Min(1f)] public float maxDistance = 100f;

    private Vehicle vehicle;
    private AudioSource idleSource;
    private AudioSource revSource;
    private AudioSource oneShotSource;
    private bool engineRunning;
    private bool loopSoundsStarted;
    private float startDelayRemaining;
    private bool throttleWasApplied;
    private float simulatedRpm;
    private int simulatedGear = 1;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();

        idleSource = CreateSource("Engine Idle", engineIdle, true);
        // The rev source is controlled manually so short acceleration clips can
        // restart at simulated gear changes instead of looping like a stutter.
        revSource = CreateSource("Engine Rev", engineRev, false);
        oneShotSource = CreateSource("Engine One Shots", null, false);

        idleSource.volume = 0f;
        revSource.volume = 0f;
    }

    private AudioSource CreateSource(string sourceName, AudioClip clip, bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = clip;
        source.loop = loop;
        source.spatialBlend = 0.35f; // Keep some 3D positioning without making the engine too quiet from the chase camera.
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = startDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.priority = 64;
        return source;
    }

    private void Update()
    {
        bool shouldRun = vehicle != null && vehicle.HasDriver;

        if (shouldRun && !engineRunning)
            StartEngine();
        else if (!shouldRun && engineRunning)
            StopEngine();

        float dt = Time.deltaTime;

        if (engineRunning && !loopSoundsStarted)
        {
            startDelayRemaining -= dt;
            if (startDelayRemaining <= 0f)
            {
                loopSoundsStarted = true;
                StartLoop(idleSource);
                if (revSoundIsLoop && !simulateGearShifts)
                    StartLoop(revSource);
            }
        }

        UpdateEngineSound(dt);
    }

    private void StartEngine()
    {
        engineRunning = true;
        loopSoundsStarted = false;
        startDelayRemaining = engineStart != null ? engineStart.length : 0f;

        if (engineStart != null)
            oneShotSource.PlayOneShot(engineStart);

        if (startDelayRemaining <= 0f)
        {
            loopSoundsStarted = true;
            StartLoop(idleSource);
            if (revSoundIsLoop && !simulateGearShifts)
                StartLoop(revSource);
        }
    }

    private void StopEngine()
    {
        engineRunning = false;
        loopSoundsStarted = false;
        startDelayRemaining = 0f;

        idleSource.Stop();
        revSource.Stop();
        simulatedRpm = 0f;
        simulatedGear = 1;
        throttleWasApplied = false;
        idleSource.volume = 0f;
        revSource.volume = 0f;

        if (engineShutdown != null)
            oneShotSource.PlayOneShot(engineShutdown);
    }

    private void StartLoop(AudioSource source)
    {
        if (source == null || source.clip == null || source.isPlaying)
            return;

        source.loop = source == revSource ? revSoundIsLoop : true;
        source.Play();
    }

    private void UpdateEngineSound(float dt)
    {
        float throttle = engineRunning && vehicle != null
            ? Mathf.Clamp01(Mathf.Abs(vehicle.Throttle))
            : 0f;

        float speedFactor = vehicle != null && vehicle.maxSpeedKph > 0f
            ? Mathf.Clamp01((vehicle.Speed * 3.6f) / vehicle.maxSpeedKph)
            : 0f;

        // Approximate engine RPM: throttle raises RPM, while releasing the
        // accelerator lets RPM fall. A gear change drops RPM and restarts the
        // acceleration recording so it sounds like the next gear pulling.
        if (engineRunning && throttle > 0.08f)
        {
            simulatedRpm = Mathf.MoveTowards(
                simulatedRpm, 1f, rpmRiseResponse * throttle * dt);

            if (simulateGearShifts && !revSoundIsLoop &&
                simulatedRpm >= shiftRpm && simulatedGear < Mathf.Max(2, simulatedGears))
            {
                simulatedGear++;
                simulatedRpm = rpmAfterShift;

                if (engineRev != null && loopSoundsStarted)
                {
                    revSource.Stop();
                    revSource.pitch = revPitchAtIdle;
                    revSource.PlayOneShot(engineRev, revVolume);
                }
            }
        }
        else
        {
            simulatedRpm = Mathf.MoveTowards(
                simulatedRpm, 0f, rpmFallResponse * dt);

            // When slowing right down, reset the simulated gearbox for the next pull-away.
            if (speedFactor < 0.08f && simulatedRpm < 0.12f)
                simulatedGear = 1;
        }

        float rpmBlend = Mathf.Clamp01(simulatedRpm);
        float revBlend = engineRunning
            ? Mathf.Clamp01(rpmBlend * 0.8f + speedFactor * 0.2f)
            : 0f;

        float targetIdleVolume = engineRunning && loopSoundsStarted && engineIdle != null
            ? idleVolume * (1f - revBlend * 0.45f)
            : 0f;

        // In short-clip/gear-shift mode, PlayOneShot handles each pull and shift.
        // Keep the regular rev source silent in that mode to avoid doubling audio.
        float targetRevVolume = engineRunning && loopSoundsStarted &&
                                engineRev != null && revSoundIsLoop && !simulateGearShifts
            ? revVolume * revBlend
            : 0f;

        idleSource.volume = Mathf.MoveTowards(
            idleSource.volume, targetIdleVolume, volumeResponse * dt);
        revSource.volume = Mathf.MoveTowards(
            revSource.volume, targetRevVolume, volumeResponse * dt);

        float idleTargetPitch = Mathf.Lerp(idlePitch, revPitchAtFullThrottle, revBlend);
        idleSource.pitch = Mathf.MoveTowards(
            idleSource.pitch, idleTargetPitch, pitchResponse * dt);

        float targetRevPitch = Mathf.Lerp(revPitchAtIdle, revPitchAtFullThrottle, rpmBlend);
        revSource.pitch = Mathf.MoveTowards(
            revSource.pitch, targetRevPitch, pitchResponse * dt);

        if (engineRunning && loopSoundsStarted && engineRev != null)
        {
            if (revSoundIsLoop && !simulateGearShifts)
            {
                if (!revSource.isPlaying)
                    revSource.Play();
            }
            else if (!revSoundIsLoop && simulateGearShifts)
            {
                // Start the acceleration clip on throttle press. Further restarts
                // happen only at simulated shifts, not continuously while held.
                bool throttlePressed = throttle > 0.12f && !throttleWasApplied;
                if (throttlePressed && simulatedGear == 1)
                {
                    revSource.Stop();
                    revSource.PlayOneShot(engineRev, revVolume);
                }

                throttleWasApplied = throttle > 0.12f;
            }
            else
            {
                // Legacy one-shot mode: one clip per throttle press.
                bool throttlePressed = throttle > 0.12f && !throttleWasApplied;
                if (throttlePressed)
                    revSource.PlayOneShot(engineRev, revVolume);
                throttleWasApplied = throttle > 0.12f;
            }
        }
        else
        {
            throttleWasApplied = false;
        }
    }
}
