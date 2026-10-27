using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Bridges the project's custom Vehicle controller to Realistic Engine Sounds 2.
/// Uses reflection for the RES2 component so this script can compile even when the
/// imported asset uses a separate assembly or namespace.
/// Add it to the car root or the RES2 prefab under the car.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class VehicleAudioRES2 : MonoBehaviour
{
    [Header("Optional references")]
    [Tooltip("Leave empty to find the Vehicle controller in this object's parents/children.")]
    public global::Vehicle vehicle;

    [Tooltip("Optional: drag the RES2 component here. If empty, it is found by type name in this object and its children.")]
    public Component res2Component;

    [Header("Input tuning")]
    [Tooltip("Throttle amount required to tell RES2 that the gas pedal is pressed.")]
    [Range(0.001f, 0.5f)]
    public float throttleDeadZone = 0.05f;

    [Tooltip("Speed in km/h below which negative throttle selects reverse sound before the car starts moving backwards.")]
    [Min(0f)]
    public float reverseEngageSpeedKph = 3f;

    private static readonly BindingFlags MemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Dictionary<string, MemberInfo> memberCache = new Dictionary<string, MemberInfo>();
    private readonly HashSet<string> missingMembersLogged = new HashSet<string>();

    [Header("Runtime Debug (Play Mode)")]
    public bool debugVehicleHasDriver;
    public float debugVehicleRPM;
    public float debugSentRPM;
    public float debugThrottle;
    public float debugSpeedKph;
    public bool debugLastRes2WriteSucceeded;
    public bool debugRpmWriteSucceeded;
    public bool debugThrottleWriteSucceeded;
    public float debugRes2ReadbackRPM;
    public string debugRes2Type = "Not found";
    public string debugRes2ObjectPath = "Not found";
    [TextArea(2, 5)] public string debugWriteDetails = "No writes attempted yet.";

    private Rigidbody vehicleRigidbody;
    private bool warnedMissingVehicle;
    private bool warnedMissingRes2;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();

        if (vehicle == null && !warnedMissingVehicle)
        {
            Debug.LogWarning(
                name + ": VehicleAudioRES2 could not find a Vehicle controller. Add this to the car hierarchy or assign the Vehicle field.",
                this);
            warnedMissingVehicle = true;
        }

        if (res2Component == null && !warnedMissingRes2)
        {
            Debug.LogWarning(
                name + ": VehicleAudioRES2 could not find a component whose type is RealisticEngineSound. Assign the RES2 component field manually.",
                this);
            warnedMissingRes2 = true;
        }

        if (vehicle != null && res2Component != null)
            ApplyStaticVehicleValues();
    }

    private void Update()
    {
        PushRuntimeValues();
    }

    // RES2 runs in LateUpdate. Reapply after its own update pass as a diagnostic
    // safeguard: if it resets engineCurrentRPM internally, readback will expose it.
    private void LateUpdate()
    {
        PushRuntimeValues();
        debugRes2ReadbackRPM = ReadFloatMember("engineCurrentRPM");
    }

    private void PushRuntimeValues()
    {
        if (vehicle == null || res2Component == null)
        {
            ResolveReferences();
            if (vehicle == null || res2Component == null)
                return;
        }

        float redline = Mathf.Max(vehicle.idleRpm + 1f, vehicle.redlineRpm);
        float rpm = vehicle.HasDriver
            ? Mathf.Clamp(vehicle.EngineRpm, 0f, redline)
            : 0f;
        float speedKph = vehicle.Speed * 3.6f;

        // Inspector-visible telemetry makes it clear whether input values are
        // reaching this adapter before we investigate RES2's own playback settings.
        debugVehicleHasDriver = vehicle.HasDriver;
        debugVehicleRPM = vehicle.EngineRpm;
        debugSentRPM = rpm;
        debugThrottle = vehicle.Throttle;
        debugSpeedKph = speedKph;
        debugRes2Type = res2Component != null
            ? res2Component.GetType().FullName
            : "Not found";
        debugRes2ObjectPath = res2Component != null
            ? GetHierarchyPath(res2Component.transform)
            : "Not found";

        // Vehicle.Speed is metres per second; this adapter sends km/h to RES2.
        var writeDetails = new List<string>();
        bool writesSucceeded = true;

        debugRpmWriteSucceeded = WriteMember("engineCurrentRPM", rpm, writeDetails);
        writesSucceeded &= debugRpmWriteSucceeded;
        writesSucceeded &= WriteMember("maxRPMLimit", redline, writeDetails);
        writesSucceeded &= WriteMember("carCurrentSpeed", speedKph, writeDetails);
        writesSucceeded &= WriteMember("carMaxSpeed", Mathf.Max(1f, vehicle.maxSpeedKph), writeDetails);

        bool shifting = vehicle.HasDriver && vehicle.IsShifting;
        writesSucceeded &= WriteMember("isShifting", shifting, writeDetails);

        bool hasThrottleInput =
            Mathf.Abs(vehicle.Throttle) > Mathf.Max(0.001f, throttleDeadZone);
        bool gasPedalPressed =
            vehicle.HasDriver &&
            hasThrottleInput &&
            !shifting &&
            !vehicle.IsBraking;
        debugThrottleWriteSucceeded = WriteMember("gasPedalPressing", gasPedalPressed, writeDetails);
        writesSucceeded &= debugThrottleWriteSucceeded;

        bool movingBackwards = false;

        if (vehicleRigidbody != null)
        {
#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = vehicleRigidbody.linearVelocity;
#else
            Vector3 velocity = vehicleRigidbody.velocity;
#endif
            movingBackwards = Vector3.Dot(velocity, vehicle.transform.forward) < -0.5f;
        }

        bool reverseRequestedAtLowSpeed =
            vehicle.Throttle < -Mathf.Max(0.001f, throttleDeadZone) &&
            speedKph <= Mathf.Max(0f, reverseEngageSpeedKph);

        writesSucceeded &= WriteMember("isReversing",
            vehicle.HasDriver && (movingBackwards || reverseRequestedAtLowSpeed), writeDetails);
        debugLastRes2WriteSucceeded = writesSucceeded;
        debugWriteDetails = string.Join("\n", writeDetails);
    }

    private static string GetHierarchyPath(Transform current)
    {
        if (current == null)
            return "Not found";

        string result = current.name;
        while (current.parent != null)
        {
            current = current.parent;
            result = current.name + "/" + result;
        }

        return result;
    }

    private float ReadFloatMember(string memberName)
    {
        if (res2Component == null)
            return 0f;

        Type targetType = res2Component.GetType();
        if (!memberCache.TryGetValue(memberName, out MemberInfo member))
        {
            member = (MemberInfo)targetType.GetField(memberName, MemberFlags);
            if (member == null)
                member = targetType.GetProperty(memberName, MemberFlags);

            if (member != null)
                memberCache[memberName] = member;
        }

        try
        {
            if (member is FieldInfo field)
                return Convert.ToSingle(field.GetValue(res2Component));
            if (member is PropertyInfo property && property.CanRead)
                return Convert.ToSingle(property.GetValue(res2Component));
        }
        catch (Exception exception)
        {
            if (missingMembersLogged.Add("read-" + memberName))
                Debug.LogWarning(
                    name + ": Could not read RES2 member '" + memberName + "': " + exception.Message,
                    this);
        }

        return 0f;
    }

    private void ResolveReferences()
    {
        if (vehicle == null)
        {
            vehicle = GetComponentInParent<global::Vehicle>();
            if (vehicle == null)
                vehicle = GetComponentInChildren<global::Vehicle>(true);
        }

        // The Inspector may accept a generic Component reference such as Transform.
        // Never attempt to write RES2 engine fields to that accidentally assigned object.
        if (res2Component != null && !IsRes2Component(res2Component))
        {
            Debug.LogWarning(
                name + ": Res2 Component was assigned " + res2Component.GetType().FullName +
                ", not RealisticEngineSound. Searching the vehicle hierarchy for the real RES2 component.",
                this);
            res2Component = null;
            memberCache.Clear();
            missingMembersLogged.Clear();
        }

        if (res2Component == null)
        {
            // Search this hierarchy first (normal setup: adapter on car root and RES2
            // prefab parented beneath it), then the discovered Vehicle hierarchy.
            Component found = FindRes2ComponentIn(transform);
            if (found == null && vehicle != null)
                found = FindRes2ComponentIn(vehicle.transform);

            if (found != null)
            {
                res2Component = found;
                memberCache.Clear();
                missingMembersLogged.Clear();
            }
        }

        if (vehicle != null)
            vehicleRigidbody = vehicle.GetComponent<Rigidbody>();
    }

    private static Component FindRes2ComponentIn(Transform root)
    {
        if (root == null)
            return null;

        Component[] components = root.GetComponentsInChildren<Component>(true);
        foreach (Component candidate in components)
        {
            if (candidate != null && IsRes2Component(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsRes2Component(Component candidate)
    {
        if (candidate == null)
            return false;

        Type type = candidate.GetType();
        return type.Name == "RealisticEngineSound" ||
               type.FullName == "SkrilStudio.RealisticEngineSound";
    }

    private void ApplyStaticVehicleValues()
    {
        WriteMember("maxRPMLimit",
            Mathf.Max(vehicle.idleRpm + 1f, vehicle.redlineRpm));
        WriteMember("carMaxSpeed", Mathf.Max(1f, vehicle.maxSpeedKph));
    }

    // Convenience overload for lifecycle/static writes that don't need inspector detail rows.
    private bool WriteMember(string memberName, object value)
    {
        return WriteMember(memberName, value, new List<string>());
    }

    private bool WriteMember(string memberName, object value, List<string> writeDetails)
    {
        if (res2Component == null)
        {
            writeDetails.Add(memberName + ": FAIL - RES2 component reference is null");
            return false;
        }

        Type targetType = res2Component.GetType();
        if (!memberCache.TryGetValue(memberName, out MemberInfo member))
        {
            member = (MemberInfo)targetType.GetField(memberName, MemberFlags);
            if (member == null)
                member = targetType.GetProperty(memberName, MemberFlags);

            if (member != null)
                memberCache[memberName] = member;
        }

        if (member == null)
        {
            string discovered = string.Join(", ", Array.ConvertAll(
                targetType.GetMembers(MemberFlags),
                found => found.Name));
            writeDetails.Add(memberName + ": NOT FOUND on " + targetType.FullName);
            if (missingMembersLogged.Add(memberName))
            {
                Debug.LogWarning(
                    name + ": RES2 member '" + memberName + "' was not found on " +
                    targetType.FullName + ". Actual public/non-public members: " + discovered,
                    this);
            }
            return false;
        }

        try
        {
            if (member is FieldInfo field)
            {
                object convertedValue = ConvertForMember(value, field.FieldType);
                field.SetValue(res2Component, convertedValue);
                writeDetails.Add(memberName + ": OK (" + field.FieldType.Name + ")");
                return true;
            }

            if (member is PropertyInfo property && property.CanWrite)
            {
                object convertedValue = ConvertForMember(value, property.PropertyType);
                property.SetValue(res2Component, convertedValue);
                writeDetails.Add(memberName + ": OK (" + property.PropertyType.Name + ")");
                return true;
            }

            writeDetails.Add(memberName + ": FAIL - member is not writable");
        }
        catch (Exception exception)
        {
            string message = memberName + ": FAIL - " + exception.GetType().Name + ": " + exception.Message;
            writeDetails.Add(message);
            if (missingMembersLogged.Add(memberName))
                Debug.LogWarning(name + ": " + message, this);
            return false;
        }

        return false;
    }

    private static object ConvertForMember(object value, Type targetType)
    {
        if (value == null)
            return null;

        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(value))
            return value;

        if (effectiveType.IsEnum)
            return Enum.ToObject(effectiveType, value);

        return Convert.ChangeType(value, effectiveType);
    }

    private void OnDisable()
    {
        WriteMember("gasPedalPressing", false);
        WriteMember("isShifting", false);
        WriteMember("isReversing", false);
        WriteMember("engineCurrentRPM", 0f);
    }
}
