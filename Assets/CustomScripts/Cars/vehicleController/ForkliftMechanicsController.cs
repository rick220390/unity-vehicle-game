using UnityEngine;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// Connects the WSM Heavy Machinery mechanical-part components to this project's
/// Vehicle driver ownership. It deliberately does not replace or modify Vehicle
/// driving physics or PlayerVehicleInteractor entry/exit handling.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Vehicle))]
public sealed class ForkliftMechanicsController : MonoBehaviour
{
    [Header("Vehicle integration")]
    [SerializeField] private Vehicle vehicle;
    [SerializeField] private bool requireEngineEnabled = true;
    [SerializeField] private bool enableKeyboardInput = true;

    [Header("Mechanical parts")]
    [SerializeField] private RotatingMechanicalPart mainMast;
    [SerializeField] private MovingMechanicalPart secondaryMast;
    [SerializeField] private MovingMechanicalPart loadBackrestCylinders;
    [SerializeField] private MovingMechanicalPart loadBackrest;
    [SerializeField] private MovingMechanicalPart forkLeft;
    [SerializeField] private MovingMechanicalPart forkRight;

    [Header("Movement speeds")]
    [Min(0f)] public float liftSpeed = 0.30f;
    [Min(0f)] public float sideShiftSpeed = 0.50f;
    [Min(0f)] public float forkWidthSpeed = 0.50f;
    [Min(0f)] public float mastTiltSpeed = 0.30f;

    [Header("Keyboard controls")]
    public KeyCode liftUpKey = KeyCode.Keypad4;
    public KeyCode liftDownKey = KeyCode.Keypad1;
    public KeyCode sideShiftRightKey = KeyCode.Keypad8;
    public KeyCode sideShiftLeftKey = KeyCode.Keypad7;
    public KeyCode forksOpenKey = KeyCode.Keypad9;
    public KeyCode forksCloseKey = KeyCode.Keypad6;
    public KeyCode mastTiltBackKey = KeyCode.Keypad2;
    public KeyCode mastTiltForwardKey = KeyCode.Keypad5;

    [Header("Control lever visuals")]
    [SerializeField] private Transform liftLever;
    [SerializeField] private Transform sideShiftLever;
    [SerializeField] private Transform forkWidthLever;
    [SerializeField] private Transform mastTiltLever;
    [Min(1f)] [SerializeField] private float leverResponse = 70f;
    [Min(0f)] [SerializeField] private float leverAngle = 10f;

    [Header("Hydraulic audio (optional)")]
    [SerializeField] private AudioSource movementLoop;
    [SerializeField] private AudioSource movementStart;
    [SerializeField] private AudioSource movementStop;

    private float liftInput;
    private float sideShiftInput;
    private float widthInput;
    private float tiltInput;

    private float liftValue;
    private float secondaryMastValue;
    private float sideShiftValue;
    private float widthValue;
    private float tiltValue;

    private float liftLeverAngle;
    private float sideShiftLeverAngle;
    private float widthLeverAngle;
    private float tiltLeverAngle;

    private void Awake()
    {
        if (vehicle == null)
            vehicle = GetComponent<Vehicle>();

        liftSpeed = Mathf.Abs(liftSpeed);
        sideShiftSpeed = Mathf.Abs(sideShiftSpeed);
        forkWidthSpeed = Mathf.Abs(forkWidthSpeed);
        mastTiltSpeed = Mathf.Abs(mastTiltSpeed);

        // Start from the positions serialized on the imported prefab.
        liftValue = loadBackrest != null ? loadBackrest.MovementInput : 0f;
        secondaryMastValue = secondaryMast != null ? secondaryMast.MovementInput : 0f;
        sideShiftValue = loadBackrestCylinders != null ? loadBackrestCylinders.MovementInput : 0f;
        widthValue = forkLeft != null ? forkLeft.MovementInput : 0f;
        tiltValue = mainMast != null ? mainMast.MovementInput : 0f;
    }

    private void Update()
    {
        if (!CanOperate())
        {
            // Clear each part's moving flag without changing its current position.
            if (loadBackrest != null) loadBackrest.MovementInput = liftValue;
            if (secondaryMast != null) secondaryMast.MovementInput = secondaryMastValue;
            if (loadBackrestCylinders != null) loadBackrestCylinders.MovementInput = sideShiftValue;
            if (forkLeft != null) forkLeft.MovementInput = widthValue;
            if (forkRight != null) forkRight.MovementInput = widthValue;
            if (mainMast != null) mainMast.MovementInput = tiltValue;
            SetHydraulicAudio(false);
            UpdateControlLeversToNeutral();
            return;
        }

        if (enableKeyboardInput)
        {
            liftInput = ReadAxis(liftUpKey, liftDownKey);
            sideShiftInput = ReadAxis(sideShiftRightKey, sideShiftLeftKey);
            widthInput = ReadAxis(forksOpenKey, forksCloseKey);
            tiltInput = ReadAxis(mastTiltForwardKey, mastTiltBackKey);
        }

        UpdateLift(liftInput);
        UpdateSideShift(sideShiftInput);
        UpdateForkWidth(widthInput);
        UpdateMastTilt(tiltInput);
        UpdateControlLevers();
        SetHydraulicAudio(IsAnyPartMoving());
    }

    /// <summary>
    /// Can also be called by a future gamepad/UI input component.
    /// Values are clamped to -1..1 and are read while the player owns this vehicle.
    /// </summary>
    public void SetMechanismInput(float lift, float sideShift, float width, float tilt)
    {
        liftInput = Mathf.Clamp(lift, -1f, 1f);
        sideShiftInput = Mathf.Clamp(sideShift, -1f, 1f);
        widthInput = Mathf.Clamp(width, -1f, 1f);
        tiltInput = Mathf.Clamp(tilt, -1f, 1f);
    }

    private bool CanOperate()
    {
        if (vehicle == null || !vehicle.HasDriver)
            return false;

        return !requireEngineEnabled || vehicle.ExternalEngineEnabled;
    }

    private static float ReadAxis(KeyCode positive, KeyCode negative)
    {
        bool positiveHeld = Input.GetKey(positive);
        bool negativeHeld = Input.GetKey(negative);
        if (positiveHeld == negativeHeld)
            return 0f;
        return positiveHeld ? 1f : -1f;
    }

    private void UpdateLift(float input)
    {
        if (loadBackrest == null || secondaryMast == null)
            return;

        float step = input * Time.deltaTime * liftSpeed;

        // Two-stage lift: extend the primary carriage first, then telescope the
        // secondary mast. When lowering, retract the secondary mast first.
        if (input > 0f)
        {
            if (liftValue < 0.999f)
                liftValue = Mathf.Clamp01(liftValue + step);
            else
                secondaryMastValue = Mathf.Clamp01(secondaryMastValue + step);
        }
        else if (input < 0f)
        {
            if (secondaryMastValue > 0.001f)
                secondaryMastValue = Mathf.Clamp01(secondaryMastValue + step);
            else
                liftValue = Mathf.Clamp01(liftValue + step);
        }

        loadBackrest.MovementInput = liftValue;
        secondaryMast.MovementInput = secondaryMastValue;
    }

    private void UpdateSideShift(float input)
    {
        if (loadBackrestCylinders == null)
            return;

        sideShiftValue = Mathf.Clamp01(sideShiftValue + input * Time.deltaTime * sideShiftSpeed);
        loadBackrestCylinders.MovementInput = sideShiftValue;
    }

    private void UpdateForkWidth(float input)
    {
        if (forkLeft == null || forkRight == null)
            return;

        widthValue = Mathf.Clamp01(widthValue + input * Time.deltaTime * forkWidthSpeed);
        forkLeft.MovementInput = widthValue;
        forkRight.MovementInput = widthValue;
    }

    private void UpdateMastTilt(float input)
    {
        if (mainMast == null)
            return;

        tiltValue = Mathf.Clamp01(tiltValue + input * Time.deltaTime * mastTiltSpeed);
        mainMast.MovementInput = tiltValue;
    }

    private void UpdateControlLeversToNeutral()
    {
        float response = Mathf.Max(1f, leverResponse) * Time.deltaTime;
        liftLeverAngle = Mathf.MoveTowards(liftLeverAngle, 0f, response);
        sideShiftLeverAngle = Mathf.MoveTowards(sideShiftLeverAngle, 0f, response);
        widthLeverAngle = Mathf.MoveTowards(widthLeverAngle, 0f, response);
        tiltLeverAngle = Mathf.MoveTowards(tiltLeverAngle, 0f, response);
        SetLeverRotation(liftLever, liftLeverAngle);
        SetLeverRotation(sideShiftLever, sideShiftLeverAngle);
        SetLeverRotation(forkWidthLever, widthLeverAngle);
        SetLeverRotation(mastTiltLever, tiltLeverAngle);
    }

    private void UpdateControlLevers()
    {
        float response = Mathf.Max(1f, leverResponse) * Time.deltaTime;
        liftLeverAngle = Mathf.MoveTowards(liftLeverAngle, -liftInput * leverAngle, response);
        sideShiftLeverAngle = Mathf.MoveTowards(sideShiftLeverAngle, sideShiftInput * leverAngle, response);
        widthLeverAngle = Mathf.MoveTowards(widthLeverAngle, widthInput * leverAngle, response);
        tiltLeverAngle = Mathf.MoveTowards(tiltLeverAngle, tiltInput * leverAngle, response);

        SetLeverRotation(liftLever, liftLeverAngle);
        SetLeverRotation(sideShiftLever, sideShiftLeverAngle);
        SetLeverRotation(forkWidthLever, widthLeverAngle);
        SetLeverRotation(mastTiltLever, tiltLeverAngle);
    }

    private static void SetLeverRotation(Transform lever, float angle)
    {
        if (lever != null)
            lever.localEulerAngles = new Vector3(angle, 0f, 0f);
    }

    private bool IsAnyPartMoving()
    {
        return (mainMast != null && mainMast.IsMoving) ||
               (secondaryMast != null && secondaryMast.IsMoving) ||
               (loadBackrestCylinders != null && loadBackrestCylinders.IsMoving) ||
               (loadBackrest != null && loadBackrest.IsMoving) ||
               (forkLeft != null && forkLeft.IsMoving) ||
               (forkRight != null && forkRight.IsMoving);
    }

    private void SetHydraulicAudio(bool moving)
    {
        if (movementLoop == null)
            return;

        if (moving && !movementLoop.isPlaying)
        {
            movementLoop.Play();
            if (movementStart != null && !movementStart.isPlaying)
                movementStart.Play();
        }
        else if (!moving && movementLoop.isPlaying)
        {
            movementLoop.Stop();
            if (movementStop != null && !movementStop.isPlaying)
                movementStop.Play();
        }
    }

    private void OnDisable()
    {
        if (movementLoop != null && movementLoop.isPlaying)
            movementLoop.Stop();
    }
}