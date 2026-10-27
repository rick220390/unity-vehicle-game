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
    [Tooltip("Volume for the engine startup sound when the driver enters the vehicle.")]
    [Range(0f, 1f)] public float engineStartVolume = 0.35f;
    [Tooltip("Volume for the engine shutdown sound when the driver exits the vehicle.")]
    [Range(0f, 1f)] public float engineShutdownVolume = 0.35f;
    [Tooltip("Volume for the sustained maximum-RPM engine recording. This is independent of the normal rev volume.")]
    [Range(0f, 1f)] public float peakRevVolume = 0.85f;
    [Tooltip("Volume for the sound played when the simulated gearbox changes gear.")]
    [Range(0f, 1f)] public float gearChangeVolume = 0.5f;
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
    [Tooltip("Legacy fallback only. With a Vehicle component, actual drivetrain shifts control the audio.")]
    [Min(0.25f)] public float gearShiftIntervalSeconds = 1.8f;
    [Tooltip("Playback speed for the gear-change sound. Lower values make the shift sound longer and less rushed.")]
    [Range(0.4f, 1.5f)] public float gearChangePitch = 0.75f;
    [Tooltip("RPM fraction at which the recorded rev sound transitions into its sustained tail.")]
    [Range(0.5f, 0.99f)] public float sustainedRevStartRpm = 0.72f;
    [Min(0.1f)] public float rpmRiseResponse = 0.8f;
    [Min(0.1f)] public float rpmFallResponse = 0.55f;
    [Tooltip("Start time in seconds of the steady high-RPM tail in Engine Rev. At a standstill, the clip plays once from the beginning then loops only this tail. Adjust this to where the sound has reached maximum revs.")]
    [Min(0f)] public float peakLoopStartSeconds = 1.5f;
    [Tooltip("Below this speed, the gearbox will not shift and the high-RPM tail will loop while the accelerator is held.")]
    [Min(0f)] public float stationarySpeedKph = 2f;
    [Min(0f)] public float startDistance = 8f;
    [Min(1f)] public float maxDistance = 100f;

    [Header("Granular Engine Synthesis (Experimental)")]
    [Tooltip("Enable the optional granular engine model. When disabled, the existing engine audio behaves exactly as before.")]
    public bool useGranularEngineSynthesis = false;
    [Tooltip("Use a clean, continuous recording that rises from engine idle to high RPM. Prefer several seconds without gear shifts, tyres, wind, or background noise.")]
    public AudioClip granularEngineRecording;
    [Tooltip("Start time in the source recording. Use the beginning of the engine idle.")]
    [Min(0f)] public float granularRecordingStartSeconds = 0f;
    [Tooltip("End time in the source recording. Stop before the throttle is released or the engine RPM falls. Set 0 to use the end of the clip.")]
    [Min(0f)] public float granularRecordingEndSeconds = 9.5f;
    [Tooltip("Length of each audio grain. Start around 120 ms and tune by listening.")]
    [Range(60f, 240f)] public float granularGrainLengthMs = 120f;
    [Tooltip("Variation in source position around the section selected for current RPM. 0.02 means about 2% of the recording length either side.")]
    [Range(0f, 0.08f)] public float granularSourcePositionJitter = 0.02f;
    [Tooltip("Idle volume for the granular engine.")]
    [Range(0f, 1f)] public float granularIdleVolume = 0.25f;
    [Tooltip("Loaded/high-RPM volume for the granular engine.")]
    [Range(0f, 1f)] public float granularLoadVolume = 0.9f;
    [Tooltip("How quickly the granular playback volume follows engine load.")]
    [Min(0.1f)] public float granularVolumeResponse = 5f;

    private Vehicle vehicle;
    private AudioSource idleSource;
    private AudioSource revSource;
    private AudioSource oneShotSource;
    private AudioSource revPlaybackSource;
    private AudioSource peakRevSource;
    private AudioSource decelerationSource;
    private AudioSource granularSource;

    private AudioClip granularStreamClip;
    private float[] granularRecordingSamples;
    private float[] granularWindow;
    private int granularRecordingChannels;
    private int granularRecordingFrames;
    private int granularSourceStartFrame;
    private int granularSourceEndFrame;
    private int granularSourceFrameCount;
    private int granularGrainFrames;
    private int granularHopFrames;
    private volatile float granularRpmFraction;
    private bool granularCallbackInitialized;
    private int granularFramesUntilSpawn;
    private int granularNextSlot;
    private uint granularRandomState = 0xA341316Cu;
    private GranularGrain granularGrainA;
    private GranularGrain granularGrainB;

    private struct GranularGrain
    {
        public int startFrame;
        public int phase;
        public bool active;
    }

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
    private bool previousVehicleShifting;
    private bool revTailLooping;

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

        TryInitializeGranularEngine();
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
                if (!GranularModeAvailable)
                {
                    StartLoop(idleSource);
                    if (revSoundIsLoop && !simulateGearShifts)
                        StartLoop(revSource);
                }
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
            oneShotSource.PlayOneShot(engineStart, engineStartVolume);

        if (startDelayRemaining <= 0f)
        {
            loopSoundsStarted = true;
            if (!GranularModeAvailable)
            {
                StartLoop(idleSource);
                if (revSoundIsLoop && !simulateGearShifts)
                    StartLoop(revSource);
            }
        }
    }

    private void StopEngine()
    {
        engineRunning = false;
        loopSoundsStarted = false;
        startDelayRemaining = 0f;

        idleSource.Stop();
        revSource.Stop();
        if (granularSource != null)
        {
            granularSource.Stop();
            granularSource.volume = 0f;
        }
        revPlaybackSource.Stop();
        revPlaybackSource.loop = false;
        revTailLooping = false;
        peakRevSource.Stop();
        decelerationSource.Stop();
        simulatedRpm = 0f;
        simulatedGear = 1;
        timeSinceLastGearShift = 100f;
        peakRevElapsed = 0f;
        throttleWasApplied = false;
        revRampStarted = false;
        revRampElapsed = 0f;
        revTailLooping = false;
        previousVehicleShifting = false;
        idleSource.volume = 0f;
        revSource.volume = 0f;

        if (engineShutdown != null)
            oneShotSource.PlayOneShot(engineShutdown, engineShutdownVolume);
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
        revTailLooping = false;
        revPlaybackSource.time = 0f;
        // Keep the recorded acceleration sweep at a steady playback rate so its
        // own recorded low-to-high RPM rise is preserved.
        revPlaybackSource.pitch = 1f;
        revPlaybackSource.volume = revVolume;
        revPlaybackSource.Play();
        revRampElapsed = 0f;
        revRampStarted = true;
    }

    private bool GranularModeAvailable
    {
        get
        {
            return useGranularEngineSynthesis &&
                   granularStreamClip != null &&
                   granularSource != null;
        }
    }

    private void UpdateGranularEngineSound(float dt)
    {
        float throttle = engineRunning && vehicle != null
            ? Mathf.Clamp01(Mathf.Abs(vehicle.Throttle))
            : 0f;

        float rpmFraction = vehicle != null
            ? Mathf.InverseLerp(vehicle.idleRpm, vehicle.redlineRpm, vehicle.EngineRpm)
            : 0f;
        granularRpmFraction = Mathf.Clamp01(rpmFraction);

        if (vehicle != null)
        {
            bool actualShiftStarted = vehicle.IsShifting && !previousVehicleShifting;
            if (actualShiftStarted && gearChange != null && loopSoundsStarted)
            {
                oneShotSource.pitch = gearChangePitch;
                oneShotSource.PlayOneShot(gearChange, gearChangeVolume);
                oneShotSource.pitch = 1f;
            }

            previousVehicleShifting = vehicle.IsShifting;
            simulatedGear = vehicle.CurrentGear;
        }

        // The granular recording follows live engine RPM, so legacy loop, rev,
        // peak and throttle-release clips must not play on top of it.
        idleSource.Stop();
        revSource.Stop();
        revPlaybackSource.Stop();
        peakRevSource.Stop();
        decelerationSource.Stop();
        idleSource.volume = 0f;
        revSource.volume = 0f;

        bool shouldPlay = engineRunning && loopSoundsStarted;
        float load = Mathf.Clamp01(throttle * 0.7f + granularRpmFraction * 0.3f);
        float targetVolume = shouldPlay
            ? Mathf.Lerp(granularIdleVolume, granularLoadVolume, load)
            : 0f;

        granularSource.volume = Mathf.MoveTowards(
            granularSource.volume, targetVolume, granularVolumeResponse * dt);

        if (shouldPlay)
        {
            if (!granularSource.isPlaying)
                granularSource.Play();
        }
        else if (granularSource.isPlaying)
        {
            granularSource.Stop();
        }

        throttleWasApplied = engineRunning && throttle > 0.12f;
    }

    private void UpdateEngineSound(float dt)
    {
        if (GranularModeAvailable)
        {
            UpdateGranularEngineSound(dt);
            return;
        }

        float throttle = engineRunning && vehicle != null
            ? Mathf.Clamp01(Mathf.Abs(vehicle.Throttle))
            : 0f;

        // Detect the accelerator edge independently of the rev/gear audio mode.
        // Capture RPM BEFORE the engine model starts falling, so the deceleration
        // recording can begin at the matching point in its high-to-low sweep.
        bool throttleReleased = engineRunning && throttleWasApplied && throttle <= 0.08f;
        bool throttlePressed = engineRunning && throttle > 0.12f && !throttleWasApplied;
        float rpmAtRelease = Mathf.Clamp01(simulatedRpm);

        if (throttleReleased)
        {
            // The ramp and peak sounds must not mask or race the release sound.
            if (revPlaybackSource != null)
                revPlaybackSource.Stop();
            if (peakRevSource != null)
                peakRevSource.Stop();
            if (revPlaybackSource != null)
            {
                revPlaybackSource.loop = false;
                revTailLooping = false;
            }
            revRampStarted = false;
            revRampElapsed = 0f;
            peakRevElapsed = 0f;

            if (engineDeceleration != null && decelerationSource != null)
            {
                decelerationSource.Stop();
                decelerationSource.clip = engineDeceleration;
                decelerationSource.loop = false;
                // Always start at the beginning of the falling-RPM recording.
                // The simulated RPM is only an approximation and can lag behind
                // the recorded rev audio; seeking from it can jump almost to the
                // end of the clip and make a valid throttle-release sound seem silent.
                // First make the clip explicit, reset playback, then start it.
                decelerationSource.clip = engineDeceleration;
                decelerationSource.time = 0f;
                decelerationSource.volume = decelerationVolume;
                decelerationSource.Play();
            }
        }

        // Reapplying throttle cuts the falling-RPM recording cleanly.
        if (throttlePressed && decelerationSource != null && decelerationSource.isPlaying)
            decelerationSource.Stop();

        float speedKph = vehicle != null ? vehicle.Speed * 3.6f : 0f;
        bool stationary = speedKph <= stationarySpeedKph;
        // A handbrake burnout is an engine-load event, not a road-speed gear
        // event. Keep the simulated gearbox in first while the handbrake is
        // held at low speed, even with full throttle and maximum simulated RPM.
        bool handbrakeBurnout = stationary && vehicle != null &&
                                vehicle.Handbrake && throttle >= 0.8f;
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

        // Use the vehicle drivetrain's real RPM and gear whenever available.
        // This prevents the audio from reaching its peak merely because the
        // accelerator has been held for a fixed amount of time.
        if (engineRunning && throttle > 0.08f)
        {
            if (vehicle != null)
            {
                simulatedRpm = Mathf.InverseLerp(
                    vehicle.idleRpm, vehicle.redlineRpm, vehicle.EngineRpm);

                // Follow real gearbox changes rather than running an independent
                // audio-only gearbox. This keeps the rev ramp and peak loop aligned
                // with the speed/gear configured on the Vehicle component.
                bool actualGearChanged = vehicle.CurrentGear != simulatedGear;
                bool actualShiftStarted = vehicle.IsShifting && !previousVehicleShifting;
                if (actualGearChanged)
                {
                    simulatedGear = vehicle.CurrentGear;
                    timeSinceLastGearShift = 0f;
                    peakRevElapsed = 0f;

                    if (engineRev != null && loopSoundsStarted && simulateGearShifts && !revSoundIsLoop)
                    {
                        peakRevSource.Stop();
                        RestartRevClip(revPitchAtIdle);
                    }
                }

                // Play the shift transient only when the real drivetrain enters its
                // shift window, not from an independent audio-only gear timer.
                if (actualShiftStarted && gearChange != null && loopSoundsStarted)
                {
                    oneShotSource.pitch = gearChangePitch;
                    oneShotSource.PlayOneShot(gearChange, gearChangeVolume);
                    oneShotSource.pitch = 1f;
                }
                previousVehicleShifting = vehicle.IsShifting;
            }
            else
            {
                // Fallback for safety if this component is reused without a
                // functioning Vehicle reference.
                simulatedRpm = Mathf.MoveTowards(
                    simulatedRpm, 1f, rpmRiseResponse * throttle * dt);
            }
        }
        else
        {
            // Keep the release sound's fall-off behaviour, then let the next
            // throttle pull use the live drivetrain RPM again.
            simulatedRpm = Mathf.MoveTowards(
                simulatedRpm, 0f, rpmFallResponse * dt);

            if (vehicle != null)
            {
                simulatedGear = vehicle.CurrentGear;
                previousVehicleShifting = vehicle.IsShifting;
            }
            else if (timeSinceLastGearShift >= Mathf.Max(0.25f, gearShiftIntervalSeconds) &&
                     simulatedGear > 1 &&
                     (speedFactor < 0.35f || (throttle <= 0.08f && speedFactor < 0.55f)))
            {
                simulatedGear--;
                timeSinceLastGearShift = 0f;
                if (gearChange != null && loopSoundsStarted)
                    oneShotSource.PlayOneShot(gearChange, gearChangeVolume);
            }

            if (speedFactor < 0.08f && simulatedRpm < 0.12f)
                simulatedGear = 1;
        }

        // Stop rev playback when the driver releases the accelerator.
        if (!engineRunning || throttle <= 0.08f)
        {
            if (revPlaybackSource.isPlaying)
                revPlaybackSource.Stop();
            revPlaybackSource.loop = false;
            revTailLooping = false;
            if (peakRevSource.isPlaying)
                peakRevSource.Stop();
            peakRevElapsed = 0f;
            revRampElapsed = 0f;
            revRampStarted = false;
        }
        else if (engineRev != null && revTailLooping && revPlaybackSource != null &&
                 revPlaybackSource.isPlaying && loopSoundsStarted)
        {
            // Loop only the steady high-RPM tail, not the entire recorded sweep.
            if (revPlaybackSource.time >= engineRev.length - 0.03f)
                revPlaybackSource.time = Mathf.Clamp(peakLoopStartSeconds, 0f, Mathf.Max(0f, engineRev.length - 0.05f));
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

                // Keep the engine audible after the rising sweep finishes. At
                // high RPM, the peak recording fills the gap until the next
                // simulated shift, including wheelspin where road speed is low.
                // In top gear it remains the sound at maximum road speed.
                bool highRpmLimiter = simulatedRpm >= 0.96f;
                bool stationaryBurnout = stationary && vehicle != null &&
                                         vehicle.Handbrake && throttle >= 0.9f;

                // A short rev sweep must never leave the engine silent. If a
                // dedicated peak clip is supplied, use it at the limiter; otherwise
                // loop the steady tail of the acceleration recording.
                if (enginePeakRev != null && !peakRevSource.isPlaying &&
                    (highRpmLimiter || stationaryBurnout))
                {
                    peakRevElapsed = 0f;
                    peakRevSource.Play();
                }
                else if (engineRev != null)
                {
                    // Keep a fallback engine sound running even if a peak clip is
                    // assigned but the current RPM has not reached the limiter.
                    revPlaybackSource.Stop();
                    revPlaybackSource.loop = false;
                    revPlaybackSource.time = Mathf.Clamp(peakLoopStartSeconds, 0f, Mathf.Max(0f, engineRev.length - 0.05f));
                    revTailLooping = true;
                    revRampStarted = false;
                    revPlaybackSource.Play();
                }
            }
        }

        // If the rev sweep finished before the car reached top speed, start
        // the peak loop as soon as the correct condition becomes true.
        bool highRpmLimiterActive = !revSoundIsLoop && simulateGearShifts &&
                                    throttle >= 0.8f && simulatedRpm >= 0.96f;
        bool allowPeakRev = highRpmLimiterActive ||
                            (stationary && vehicle != null && vehicle.Handbrake && throttle >= 0.9f);
        if (engineRunning && loopSoundsStarted && enginePeakRev != null &&
            throttle > 0.08f && allowPeakRev && !revRampStarted &&
            (revPlaybackSource == null || !revPlaybackSource.isPlaying) &&
            !peakRevSource.isPlaying)
        {
            peakRevElapsed = 0f;
            peakRevSource.Play();
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
            // Apply the Inspector value directly; don't silently scale it down.
            decelerationSource.volume = decelerationVolume;
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
                ? peakRevVolume
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
                // Every fresh throttle press must restart the acceleration sweep,
                // even if the car is already in a higher simulated gear. Otherwise
                // the rev/peak sources can both remain stopped until the car stops.
                if (throttlePressed)
                {
                    peakRevSource.Stop();
                    peakRevElapsed = 0f;
                    RestartRevClip(revPitchAtIdle);
                }

            }
            else
            {
                // Legacy one-shot mode: one clip per throttle press.
                if (throttlePressed)
                    RestartRevClip(Mathf.Lerp(revPitchAtIdle, revPitchAtFullThrottle, simulatedRpm));
            }
        }

        // Always update the edge detector, regardless of which clips are assigned
        // or which audio mode is selected. This keeps throttle release reliable.
        throttleWasApplied = engineRunning && throttle > 0.12f;
    }
    private void TryInitializeGranularEngine()
    {
        if (!useGranularEngineSynthesis)
            return;

        if (granularEngineRecording == null)
        {
            Debug.LogWarning(
                $"{name}: Granular Engine Synthesis is enabled, but no Granular Engine Recording is assigned. Using the existing VehicleAudio system.",
                this);
            return;
        }

        // AudioClip.GetData needs a readable clip. Set Import Settings > Load Type
        // to Decompress On Load for the cleanest, most reliable setup.
        granularRecordingChannels = Mathf.Max(1, granularEngineRecording.channels);
        granularRecordingFrames = granularEngineRecording.samples;
        if (granularRecordingFrames < 128)
        {
            Debug.LogWarning($"{name}: The granular engine recording is too short. Using the existing VehicleAudio system.", this);
            return;
        }

        granularRecordingSamples = new float[granularRecordingFrames * granularRecordingChannels];
        if (!granularEngineRecording.GetData(granularRecordingSamples, 0))
        {
            granularRecordingSamples = null;
            Debug.LogWarning(
                $"{name}: Could not read the Granular Engine Recording. Set its Import Settings > Load Type to Decompress On Load, then reimport it. Using the existing VehicleAudio system.",
                this);
            return;
        }

        granularSourceStartFrame = Mathf.Clamp(
            Mathf.RoundToInt(granularRecordingStartSeconds * granularEngineRecording.frequency),
            0,
            granularRecordingFrames - 1);

        float requestedEndSeconds = granularRecordingEndSeconds <= 0f
            ? granularEngineRecording.length
            : Mathf.Min(granularRecordingEndSeconds, granularEngineRecording.length);

        granularSourceEndFrame = Mathf.Clamp(
            Mathf.RoundToInt(requestedEndSeconds * granularEngineRecording.frequency),
            granularSourceStartFrame + 1,
            granularRecordingFrames);

        granularSourceFrameCount = granularSourceEndFrame - granularSourceStartFrame;
        if (granularSourceFrameCount < 128)
        {
            Debug.LogWarning($"{name}: The selected granular recording region is too short. Increase Granular Recording End Seconds. Using the existing VehicleAudio system.", this);
            granularRecordingSamples = null;
            return;
        }

        granularGrainFrames = Mathf.Clamp(
            Mathf.RoundToInt(granularEngineRecording.frequency * granularGrainLengthMs / 1000f),
            64,
            Mathf.Max(64, granularSourceFrameCount / 2));
        granularGrainFrames = Mathf.Min(granularGrainFrames, granularSourceFrameCount - 1);
        granularHopFrames = Mathf.Max(1, granularGrainFrames / 2);

        granularWindow = new float[granularGrainFrames];
        for (int i = 0; i < granularWindow.Length; i++)
        {
            // Hann envelope: two grains offset by half a grain crossfade smoothly.
            granularWindow[i] = 0.5f - 0.5f * Mathf.Cos(
                2f * Mathf.PI * i / Mathf.Max(1, granularWindow.Length - 1));
        }

        int outputLengthFrames = granularEngineRecording.frequency * 8;
        granularStreamClip = AudioClip.Create(
            "Granular Engine Stream",
            outputLengthFrames,
            granularRecordingChannels,
            granularEngineRecording.frequency,
            true,
            OnGranularAudioRead,
            OnGranularAudioSetPosition);

        granularSource = CreateSource("Granular Engine", granularStreamClip, true);
        granularSource.loop = true;
        granularSource.volume = 0f;
        granularCallbackInitialized = false;
        granularRpmFraction = 0f;
    }

    private void OnGranularAudioRead(float[] data)
    {
        if (granularRecordingSamples == null ||
            granularWindow == null ||
            granularRecordingFrames <= 0 ||
            granularRecordingChannels <= 0)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        int channels = granularRecordingChannels;
        int outputFrames = data.Length / channels;

        for (int frame = 0; frame < outputFrames; frame++)
        {
            if (!granularCallbackInitialized)
            {
                granularGrainA = CreateGranularGrain(0);
                granularGrainB = CreateGranularGrain(granularHopFrames);
                granularNextSlot = 1;
                granularFramesUntilSpawn = granularHopFrames;
                granularCallbackInitialized = true;
            }
            else if (granularFramesUntilSpawn <= 0)
            {
                GranularGrain next = CreateGranularGrain(0);
                if (granularNextSlot == 0)
                    granularGrainA = next;
                else
                    granularGrainB = next;

                granularNextSlot = 1 - granularNextSlot;
                granularFramesUntilSpawn = granularHopFrames;
            }

            float weightA = 0f;
            float weightB = 0f;
            if (granularGrainA.active && granularGrainA.phase >= 0 &&
                granularGrainA.phase < granularGrainFrames)
                weightA = granularWindow[granularGrainA.phase];
            if (granularGrainB.active && granularGrainB.phase >= 0 &&
                granularGrainB.phase < granularGrainFrames)
                weightB = granularWindow[granularGrainB.phase];

            for (int channel = 0; channel < channels; channel++)
            {
                float sample = 0f;
                if (weightA > 0f)
                {
                    int indexA = (granularGrainA.startFrame + granularGrainA.phase) * channels + channel;
                    sample += granularRecordingSamples[indexA] * weightA;
                }
                if (weightB > 0f)
                {
                    int indexB = (granularGrainB.startFrame + granularGrainB.phase) * channels + channel;
                    sample += granularRecordingSamples[indexB] * weightB;
                }

                data[frame * channels + channel] = Mathf.Clamp(sample, -1f, 1f);
            }

            if (granularGrainA.active)
            {
                granularGrainA.phase++;
                if (granularGrainA.phase >= granularGrainFrames)
                    granularGrainA.active = false;
            }
            if (granularGrainB.active)
            {
                granularGrainB.phase++;
                if (granularGrainB.phase >= granularGrainFrames)
                    granularGrainB.active = false;
            }

            granularFramesUntilSpawn--;
        }
    }

    private GranularGrain CreateGranularGrain(int startingPhase)
    {
        float rpm = granularRpmFraction;
        int maxStartOffset = Mathf.Max(0, granularSourceFrameCount - granularGrainFrames);
        float centreOffset = rpm * maxStartOffset;
        int jitterRange = Mathf.RoundToInt(
            granularSourcePositionJitter * granularSourceFrameCount);
        int jitter = jitterRange > 0
            ? Mathf.RoundToInt((NextGranularRandom() * 2f - 1f) * jitterRange)
            : 0;
        int start = granularSourceStartFrame + Mathf.Clamp(
            Mathf.RoundToInt(centreOffset) + jitter,
            0,
            maxStartOffset);

        return new GranularGrain
        {
            startFrame = start,
            phase = startingPhase,
            active = startingPhase < granularGrainFrames
        };
    }

    private float NextGranularRandom()
    {
        // Small deterministic PRNG; avoids allocations or UnityEngine.Random on
        // the audio thread.
        granularRandomState = 1664525u * granularRandomState + 1013904223u;
        return (granularRandomState & 0x00FFFFFFu) / 16777215f;
    }

    private void OnGranularAudioSetPosition(int newPosition)
    {
        // Keep grain phases alive across the generated stream's loop point.
    }

    private void OnDestroy()
    {
        if (granularStreamClip != null)
            Destroy(granularStreamClip);
    }

}
