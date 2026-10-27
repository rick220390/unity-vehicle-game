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
    [Tooltip("A sustained high-RPM recording. This loops after the rising-RPM clip finishes.")]
    public AudioClip enginePeakRev;
    [Tooltip("Played once at each simulated gear change.")]
    public AudioClip gearChange;
    [Tooltip("Played once when the driver releases the accelerator after revving. Use a short high-to-low RPM recording.")]
    public AudioClip engineDeceleration;
    [Tooltip("Played once when the driver exits and shuts the vehicle down.")]
    public AudioClip engineShutdown;

    [Header("Rev sound")]
    [Tooltip("Enable if Engine Rev is a sustained loop. Disable if it is a short rev sound.")]
    public bool revSoundIsLoop = true;
    [Range(0f, 1f)] public float idleVolume = 1f;
    [Range(0f, 1f)] public float revVolume = 1f;
    [Tooltip("Volume for the falling-RPM recording played when the accelerator is released.")]
    [Range(0f, 1f)] public float decelerationVolume = 0.45f;
    [Tooltip("How much the idle sound is reduced while the deceleration clip plays.")]
    [Range(0f, 1f)] public float idleVolumeDuringDeceleration = 0.2f;
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
    [Tooltip("Minimum time between simulated gear changes. Increase this for slower, more noticeable shifts.")]
    [Min(0.25f)] public float gearShiftIntervalSeconds = 1.8f;
    [Min(0.1f)] public float rpmRiseResponse = 0.8f;
    [Min(0.1f)] public float rpmFallResponse = 0.55f;
    [Tooltip("Start time in seconds of the steady high-RPM tail in Engine Rev. At a standstill, the clip plays once from the beginning then loops only this tail. Adjust this to where the sound has reached maximum revs.")]
    [Min(0f)] public float peakLoopStartSeconds = 1.5f;
    [Tooltip("Below this speed, the gearbox will not shift and the high-RPM tail will loop while the accelerator is held.")]
    [Min(0f)] public float stationarySpeedKph = 2f;
    [Min(0f)] public float startDistance = 8f;
    [Min(1f)] public float maxDistance = 100f;

    private Vehicle vehicle;
    private AudioSource idleSource;
    private AudioSource revSource;
    private AudioSource oneShotSource;
    private AudioSource revPlaybackSource;
    private AudioSource peakRevSource;
    private AudioSource decelerationSource;
    private bool engineRunning;
    private bool loopSoundsStarted;
    private float startDelayRemaining;
    private bool throttleWasApplied;
    // Prevent the sustained peak loop from starting before the rising-rev clip has played.
    private bool revRampStarted;
    private float revRampElapsed;
    private float simulatedRpm;
    private float timeSinceLastGearShift = 100f;
    private float peakRevElapsed;
    private int simulatedGear = 1;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();

        idleSource = CreateSource("Engine Idle", engineIdle, true);
        // The rev source is controlled manually so short acceleration clips can
        // restart at simulated gear changes instead of looping like a stutter.
        revSource = CreateSource("Engine Rev", engineRev, false);
        oneShotSource = CreateSource("Engine One Shots", null, false);
        revPlaybackSource = CreateSource("Engine Rev Playback", engineRev, false);
        revPlaybackSource.loop = false;
        revPlaybackSource.volume = 1f;
        peakRevSource = CreateSource("Engine Peak Rev", enginePeakRev, true);
        peakRevSource.volume = 0f;
        decelerationSource = CreateSource("Engine Deceleration", engineDeceleration, false);
        decelerationSource.loop = false;
        decelerationSource.volume = decelerationVolume;
        decelerationSource.priority = 32;
        // Keep the falling-RPM clip clearer and less affected by distance attenuation.
        decelerationSource.spatialBlend = 0.15f;

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
        revPlaybackSource.Stop();
        peakRevSource.Stop();
        decelerationSource.Stop();
        simulatedRpm = 0f;
        simulatedGear = 1;
        timeSinceLastGearShift = 100f;
        peakRevElapsed = 0f;
        throttleWasApplied = false;
        revRampStarted = false;
        revRampElapsed = 0f;
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


    private void RestartRevClip(float startingPitch)
    {
        if (engineRev == null || revPlaybackSource == null)
            return;

        // Stop and replay from the beginning to create a clear gear-change drop.
        revPlaybackSource.Stop();
        revPlaybackSource.clip = engineRev;
        revPlaybackSource.loop = false;
        revPlaybackSource.time = 0f;
        // Keep the recorded acceleration sweep at a steady playback rate so its
        // own recorded low-to-high RPM rise is preserved.
        revPlaybackSource.pitch = 1f;
        revPlaybackSource.volume = revVolume;
        revPlaybackSource.Play();
        revRampElapsed = 0f;
        revRampStarted = true;
    }

    private void UpdateEngineSound(float dt)
    {
        float throttle = engineRunning && vehicle != null
            ? Mathf.Clamp01(Mathf.Abs(vehicle.Throttle))
            : 0f;

        // Detect the transition from accelerator pressed to released once,
        // before throttleWasApplied is updated later in this method.
        bool throttleReleased = engineRunning && throttleWasApplied && throttle <= 0.08f;
        if (throttleReleased && engineDeceleration != null)
        {
            decelerationSource.Stop();
            decelerationSource.clip = engineDeceleration;
            decelerationSource.loop = false;
            decelerationSource.Play();
        }

        // Reapplying throttle cuts the falling-RPM recording cleanly.
        if (throttle > 0.12f && decelerationSource.isPlaying)
            decelerationSource.Stop();

        float speedKph = vehicle != null ? vehicle.Speed * 3.6f : 0f;
        bool stationary = speedKph <= stationarySpeedKph;
        float speedFactor = vehicle != null && vehicle.maxSpeedKph > 0f
            ? Mathf.Clamp01(speedKph / vehicle.maxSpeedKph)
            : 0f;

        // Track elapsed time since the peak sound started. AudioSource.time is
        // unreliable for a looping clip because it wraps back to zero.
        timeSinceLastGearShift += dt;
        if (peakRevSource != null && peakRevSource.isPlaying && throttle > 0.08f)
            peakRevElapsed += dt;
        else if (peakRevSource == null || !peakRevSource.isPlaying)
            peakRevElapsed = 0f;

        // Approximate engine RPM: throttle raises RPM, while releasing the
        // accelerator lets RPM fall. A gear change drops RPM and restarts the
        // acceleration recording so it sounds like the next gear pulling.
        if (engineRunning && throttle > 0.08f)
        {
            simulatedRpm = Mathf.MoveTowards(
                simulatedRpm, 1f, rpmRiseResponse * throttle * dt);

            // Finish the rising-RPM recording, then hold peak revs briefly before
            // shifting. Clamp the wait so inspector values from older tuning do
            // not make the gearbox feel stuck in one gear.
            float peakHoldSeconds = Mathf.Clamp(gearShiftIntervalSeconds, 0.75f, 1.4f);
            if (simulateGearShifts && !revSoundIsLoop && !stationary &&
                simulatedRpm >= shiftRpm &&
                peakRevSource != null && peakRevSource.isPlaying &&
                peakRevElapsed >= peakHoldSeconds &&
                simulatedGear < Mathf.Max(2, simulatedGears))
            {
                simulatedGear++;
                simulatedRpm = rpmAfterShift;
                timeSinceLastGearShift = 0f;
                peakRevElapsed = 0f;

                if (engineRev != null && loopSoundsStarted)
                {
                    RestartRevClip(revPitchAtIdle);
                    peakRevSource.Stop();
                    if (gearChange != null)
                        oneShotSource.PlayOneShot(gearChange);
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

        // Stop rev playback when the driver releases the accelerator.
        if (!engineRunning || throttle <= 0.08f)
        {
            if (revPlaybackSource.isPlaying)
                revPlaybackSource.Stop();
            if (peakRevSource.isPlaying)
                peakRevSource.Stop();
            peakRevElapsed = 0f;
            revRampElapsed = 0f;
            revRampStarted = false;
        }
        else if (engineRev != null && revRampStarted && loopSoundsStarted)
        {
            // Use the recording's actual duration as the hand-off gate. This
            // prevents the peak clip from starting early just because Unity
            // briefly reports the AudioSource as not playing.
            revRampElapsed += dt;
            float rampDuration = engineRev.length;
            if (revRampElapsed >= rampDuration)
            {
                revRampStarted = false;
                if (revPlaybackSource.isPlaying)
                    revPlaybackSource.Stop();

                if (enginePeakRev != null && !peakRevSource.isPlaying)
                {
                    peakRevElapsed = 0f;
                    peakRevSource.Play();
                }
            }
        }

        float rpmBlend = Mathf.Clamp01(simulatedRpm);
        float revBlend = engineRunning
            ? Mathf.Clamp01(rpmBlend * 0.8f + speedFactor * 0.2f)
            : 0f;

        float targetIdleVolume = engineRunning && loopSoundsStarted && engineIdle != null
            ? idleVolume * (1f - revBlend * 0.45f)
            : 0f;

        // Let the deceleration recording stand out instead of being masked by idle audio.
        if (decelerationSource != null)
        {
            // The recorded deceleration transient is naturally prominent; keep
            // the inspector slider useful without letting it overpower the engine.
            decelerationSource.volume = decelerationVolume * 0.45f;
            if (decelerationSource.isPlaying)
                targetIdleVolume *= idleVolumeDuringDeceleration;
        }

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
        if (revPlaybackSource != null)
        {
            revPlaybackSource.volume = revVolume;
            // Engine Rev is a recorded low-to-high RPM sweep. Let the recording
            // provide its own rise instead of pitching it up a second time in code.
            // Only adjust pitch when it is idle, ready for the next pull.
            if (!revPlaybackSource.isPlaying)
            {
                revPlaybackSource.pitch = Mathf.MoveTowards(
                    revPlaybackSource.pitch, targetRevPitch, pitchResponse * dt);
            }
        }
        if (peakRevSource != null)
        {
            float peakTargetVolume = engineRunning && loopSoundsStarted && throttle > 0.08f && peakRevSource.isPlaying
                ? revVolume
                : 0f;
            peakRevSource.volume = Mathf.MoveTowards(peakRevSource.volume, peakTargetVolume, volumeResponse * dt);
            peakRevSource.pitch = Mathf.MoveTowards(peakRevSource.pitch, revPitchAtFullThrottle, pitchResponse * dt);
        }

        if (engineRunning && loopSoundsStarted && engineRev != null)
        {
            if (revSoundIsLoop && !simulateGearShifts)
            {
                if (!revSource.isPlaying)
                    revSource.Play();
            }
            else if (!revSoundIsLoop && simulateGearShifts)
            {
                // Start the rising-RPM clip on throttle press. It is not restarted
                // when it finishes; the peak-rev loop takes over. Gear shifts restart it.
                bool throttlePressed = throttle > 0.12f && !throttleWasApplied;
                // Every fresh throttle press must restart the acceleration sweep,
                // even if the car is already in a higher simulated gear. Otherwise
                // the rev/peak sources can both remain stopped until the car stops.
                if (throttlePressed)
                {
                    peakRevSource.Stop();
                    peakRevElapsed = 0f;
                    RestartRevClip(revPitchAtIdle);
                }

                throttleWasApplied = throttle > 0.12f;
            }
            else
            {
                // Legacy one-shot mode: one clip per throttle press.
                bool throttlePressed = throttle > 0.12f && !throttleWasApplied;
                if (throttlePressed)
                    RestartRevClip(Mathf.Lerp(revPitchAtIdle, revPitchAtFullThrottle, simulatedRpm));
                throttleWasApplied = throttle > 0.12f;
            }
        }
        else
        {
            throttleWasApplied = false;
        }
    }
}
