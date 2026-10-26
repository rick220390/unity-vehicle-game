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
    [Range(0f, 1f)] public float idleVolume = 0.7f;
    [Range(0f, 1f)] public float revVolume = 0.8f;
    [Min(0.1f)] public float idlePitch = 0.95f;
    [Min(0.1f)] public float revPitchAtIdle = 0.9f;
    [Min(0.1f)] public float revPitchAtFullThrottle = 1.45f;
    [Min(0.1f)] public float pitchResponse = 4f;
    [Min(0.1f)] public float volumeResponse = 4f;
    [Min(0f)] public float startDistance = 3f;
    [Min(1f)] public float maxDistance = 35f;

    private Vehicle vehicle;
    private AudioSource idleSource;
    private AudioSource revSource;
    private AudioSource oneShotSource;
    private bool engineRunning;
    private bool loopSoundsStarted;
    private float startDelayRemaining;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();

        idleSource = CreateSource("Engine Idle", engineIdle, true);
        revSource = CreateSource("Engine Rev", engineRev, revSoundIsLoop);
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
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = startDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
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
                if (revSoundIsLoop)
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
            if (revSoundIsLoop)
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
            ? Mathf.Abs(vehicle.Throttle)
            : 0f;

        // Use throttle for engine load and a little road-speed influence so
        // the sound responds even when the car is moving with light throttle.
        float speedFactor = vehicle != null
            ? Mathf.Clamp01(vehicle.Speed / 35f)
            : 0f;
        float revBlend = engineRunning ? Mathf.Clamp01(throttle * 0.8f + speedFactor * 0.2f) : 0f;

        float targetIdleVolume = engineRunning && loopSoundsStarted && engineIdle != null
            ? idleVolume * (1f - revBlend * 0.75f)
            : 0f;
        float targetRevVolume = engineRunning && loopSoundsStarted && engineRev != null
            ? revVolume * revBlend
            : 0f;

        idleSource.volume = Mathf.MoveTowards(idleSource.volume, targetIdleVolume, volumeResponse * dt);
        revSource.volume = Mathf.MoveTowards(revSource.volume, targetRevVolume, volumeResponse * dt);

        float targetRevPitch = Mathf.Lerp(revPitchAtIdle, revPitchAtFullThrottle, revBlend);
        revSource.pitch = Mathf.MoveTowards(revSource.pitch, targetRevPitch, pitchResponse * dt);
        idleSource.pitch = Mathf.Lerp(0.95f, 1.08f, revBlend * 0.35f);

        // A short rev clip is retriggered when the player applies throttle,
        // rather than being forced into an audible, unnatural loop.
        if (engineRunning && loopSoundsStarted && engineRev != null && !revSoundIsLoop)
        {
            if (throttle > 0.12f && !revSource.isPlaying)
                revSource.PlayOneShot(engineRev, revVolume);
        }
        else if (revSoundIsLoop && engineRunning && loopSoundsStarted &&
                 engineRev != null && !revSource.isPlaying)
        {
            revSource.Play();
        }
    }
}
