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

        // Vehicle.Speed is metres per second; this adapter sends km/h to RES2.
        WriteMember("engineCurrentRPM", rpm);
        WriteMember("maxRPMLimit", redline);
        WriteMember("carCurrentSpeed", vehicle.Speed * 3.6f);
        WriteMember("carMaxSpeed", Mathf.Max(1f, vehicle.maxSpeedKph));

        bool shifting = vehicle.HasDriver && vehicle.IsShifting;
        WriteMember("isShifting", shifting);

        bool hasThrottleInput =
            Mathf.Abs(vehicle.Throttle) > Mathf.Max(0.001f, throttleDeadZone);
        bool gasPedalPressed =
            vehicle.HasDriver &&
            hasThrottleInput &&
            !shifting &&
            !vehicle.IsBraking;
        WriteMember("gasPedalPressing", gasPedalPressed);

        float speedKph = vehicle.Speed * 3.6f;
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

        WriteMember("isReversing",
            vehicle.HasDriver && (movingBackwards || reverseRequestedAtLowSpeed));
    }

    private void ResolveReferences()
    {
        if (vehicle == null)
        {
            vehicle = GetComponentInParent<global::Vehicle>();
            if (vehicle == null)
                vehicle = GetComponentInChildren<global::Vehicle>(true);
        }

        if (res2Component == null)
        {
            Component[] components = GetComponentsInChildren<Component>(true);
            foreach (Component candidate in components)
            {
                if (candidate != null &&
                    (candidate.GetType().Name == "RealisticEngineSound" ||
                     candidate.GetType().FullName == "SkrilStudio.RealisticEngineSound"))
                {
                    res2Component = candidate;
                    memberCache.Clear();
                    missingMembersLogged.Clear();
                    break;
                }
            }
        }

        if (vehicle != null)
            vehicleRigidbody = vehicle.GetComponent<Rigidbody>();
    }

    private void ApplyStaticVehicleValues()
    {
        WriteMember("maxRPMLimit",
            Mathf.Max(vehicle.idleRpm + 1f, vehicle.redlineRpm));
        WriteMember("carMaxSpeed", Mathf.Max(1f, vehicle.maxSpeedKph));
    }

    private void WriteMember(string memberName, object value)
    {
        if (res2Component == null)
            return;

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
            {
                field.SetValue(res2Component, value);
                return;
            }

            if (member is PropertyInfo property && property.CanWrite)
            {
                property.SetValue(res2Component, value);
                return;
            }
        }
        catch (Exception exception)
        {
            if (missingMembersLogged.Add(memberName))
                Debug.LogWarning(
                    name + ": Could not set RES2 member '" + memberName + "': " + exception.Message,
                    this);
            return;
        }

        if (missingMembersLogged.Add(memberName))
        {
            Debug.LogWarning(
                name + ": The assigned RES2 component does not expose a writable '" +
                memberName + "' field/property. Check that the correct RES2 component is assigned.",
                this);
        }
    }

    private void OnDisable()
    {
        WriteMember("gasPedalPressing", false);
        WriteMember("isShifting", false);
        WriteMember("isReversing", false);
        WriteMember("engineCurrentRPM", 0f);
    }
}
