using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vehicle lighting controller for the SportCar model.
/// Press H while driving to cycle: lights off -> dipped headlights/tail lights
/// -> main beam + dipped headlights/tail lights -> all exterior lights off.
/// Brake lights are controlled independently by Vehicle.IsBraking.
/// </summary>
public class LightEffects : MonoBehaviour
{
    private enum LightingMode
    {
        Off = 0,
        DippedBeam = 1,
        MainBeam = 2
    }

    [Header("Controls")]
    public KeyCode lightToggleKey = KeyCode.H;

    [Header("Headlights")]
    [Tooltip("Dipped-beam spotlights. Automatically detected from FrontLights when left empty.")]
    public Light[] headlightSpots;

    [Tooltip("Optional separate main-beam lights. If empty, the dipped-beam spotlights are boosted for main beam.")]
    public Light[] mainBeamLights;

    [Min(1f)] public float mainBeamIntensityMultiplier = 2.2f;
    [Min(1f)] public float mainBeamRangeMultiplier = 1.6f;
    [Range(1f, 1.5f)] public float mainBeamSpotAngleMultiplier = 1.12f;

    [Header("Vehicle Light Groups")]
    [Tooltip("The existing front-light group. Auto-found by name when left empty.")]
    public GameObject FL;
    [Tooltip("The existing brake-light group. It is enabled only while braking.")]
    public GameObject BL;
    [Tooltip("The existing tail-light group.")]
    public GameObject TL;

    [Header("Optional Visual Emission Materials")]
    [Tooltip("Existing front lamp material. Its emission is toggled with the headlights.")]
    public Material FLM;
    [Tooltip("Existing tail lamp material. Its emission is toggled with the headlights.")]
    public Material TLM;
    [Tooltip("Optional brake-lamp materials to make emissive while braking.")]
    public Material[] brakeLightMaterials;

    // Preserve the old prefab fields so existing serialized references are not lost.
    [HideInInspector] public GameObject RL;
    [HideInInspector] public GameObject IL;
    [HideInInspector] public GameObject IR;
    [HideInInspector] public Material RLM;
    [HideInInspector] public Material ILM;
    [HideInInspector] public Material IRM;

    private struct LightSnapshot
    {
        public Light light;
        public bool wasEnabled;
        public float intensity;
        public float range;
        public float spotAngle;
    }

    private Vehicle vehicle;
    private LightingMode lightingMode = LightingMode.Off;
    private Light[] detectedBrakeLights;
    private LightSnapshot[] headlightDefaults;
    private LightSnapshot[] mainBeamDefaults;
    private LightSnapshot[] brakeLightDefaults;
    private bool hasSeparateMainBeam;
    private bool brakeLightsActive;

    private readonly List<Material> frontEmissionInstances = new List<Material>();
    private readonly List<Material> tailEmissionInstances = new List<Material>();
    private readonly List<Material> brakeEmissionInstances = new List<Material>();

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        if (vehicle == null)
            vehicle = GetComponentInParent<Vehicle>();
        if (vehicle == null)
            vehicle = GetComponentInChildren<Vehicle>(true);

        if (FL == null)
            FL = FindDescendant("FrontLights");
        if (BL == null)
            BL = FindDescendant("BrakeLights");
        if (TL == null)
            TL = FindDescendant("TailLights");

        CacheHeadlights();
        CacheMainBeamLights();
        CacheBrakeLights();

        headlightDefaults = CaptureLights(headlightSpots);
        mainBeamDefaults = CaptureLights(mainBeamLights);
        brakeLightDefaults = CaptureLights(detectedBrakeLights);
        hasSeparateMainBeam = mainBeamLights != null && mainBeamLights.Length > 0;

        CacheEmissionMaterials(FL, FLM, frontEmissionInstances, false);
        CacheEmissionMaterials(TL, TLM, tailEmissionInstances, false);
        CacheOptionalBrakeMaterials();

        // Start with exterior lamps off, but never rely on deactivating a shared
        // object elsewhere in the scene; all searches are limited to this vehicle.
        ApplyLightingMode();
        ApplyBrakeLights(false);
    }

    private void Update()
    {
        // With a Vehicle controller, only the driver of this vehicle can toggle
        // its lights. If this script is reused on a display car without Vehicle,
        // H remains available for simple preview/testing.
        if ((vehicle == null || vehicle.HasDriver) && Input.GetKeyDown(lightToggleKey))
        {
            lightingMode = (LightingMode)(((int)lightingMode + 1) % 3);
            ApplyLightingMode();
        }

        bool shouldBrake = vehicle != null && vehicle.HasDriver && vehicle.IsBraking;
        if (shouldBrake != brakeLightsActive)
            ApplyBrakeLights(shouldBrake);
    }

    private void CacheHeadlights()
    {
        if (headlightSpots != null && headlightSpots.Length > 0)
            return;

        Transform frontRoot = FL != null ? FL.transform : transform;
        Light[] candidates = frontRoot.GetComponentsInChildren<Light>(true);
        var found = new List<Light>();

        // Prefer explicitly named front spotlights, as used by the current car.
        foreach (Light candidate in candidates)
        {
            if (candidate == null || candidate.type != LightType.Spot)
                continue;

            string lowerName = candidate.name.ToLowerInvariant();
            if (lowerName.Contains("highbeam") ||
                lowerName.Contains("mainbeam") ||
                lowerName.Contains("brakelight") ||
                lowerName.Contains("reverse") ||
                lowerName.Contains("indicator"))
                continue;

            if (lowerName.Contains("spotlight"))
                found.Add(candidate);
        }

        // For future cars with differently named lights, use white spotlights
        // mounted towards the front of the vehicle.
        if (found.Count == 0)
        {
            Light[] allLights = GetComponentsInChildren<Light>(true);
            foreach (Light candidate in allLights)
            {
                if (candidate == null || candidate.type != LightType.Spot)
                    continue;

                string lowerName = candidate.name.ToLowerInvariant();
                if (lowerName.Contains("highbeam") ||
                    lowerName.Contains("mainbeam") ||
                    lowerName.Contains("brakelight") ||
                    lowerName.Contains("reverse") ||
                    lowerName.Contains("indicator"))
                    continue;

                Vector3 localPosition = transform.InverseTransformPoint(candidate.transform.position);
                bool looksWhite = candidate.color.r > candidate.color.g * 0.8f &&
                                  candidate.color.g > candidate.color.b * 0.8f;
                if (localPosition.z > 0.1f && looksWhite)
                    found.Add(candidate);
            }
        }

        headlightSpots = found.ToArray();
    }

    private void CacheMainBeamLights()
    {
        if (mainBeamLights != null && mainBeamLights.Length > 0)
            return;

        Light[] allLights = GetComponentsInChildren<Light>(true);
        var found = new List<Light>();

        foreach (Light candidate in allLights)
        {
            if (candidate == null || candidate.type != LightType.Spot)
                continue;

            string lowerName = candidate.name.ToLowerInvariant();
            if (!lowerName.Contains("highbeam") && !lowerName.Contains("mainbeam"))
                continue;

            if (ContainsLight(headlightSpots, candidate))
                continue;

            found.Add(candidate);
        }

        mainBeamLights = found.ToArray();
    }

    private void CacheBrakeLights()
    {
        if (BL != null)
        {
            Light[] candidates = BL.GetComponentsInChildren<Light>(true);
            var found = new List<Light>();

            foreach (Light candidate in candidates)
            {
                if (candidate != null &&
                    candidate.name.ToLowerInvariant().Contains("brakelight"))
                    found.Add(candidate);
            }

            detectedBrakeLights = found.ToArray();
        }
        else
        {
            Light[] candidates = GetComponentsInChildren<Light>(true);
            var found = new List<Light>();

            foreach (Light candidate in candidates)
            {
                if (candidate != null &&
                    candidate.name.ToLowerInvariant().Contains("brakelight"))
                    found.Add(candidate);
            }

            detectedBrakeLights = found.ToArray();
        }
    }

    private static bool ContainsLight(Light[] lights, Light target)
    {
        if (lights == null)
            return false;

        foreach (Light light in lights)
        {
            if (light == target)
                return true;
        }

        return false;
    }

    private static LightSnapshot[] CaptureLights(Light[] lights)
    {
        if (lights == null)
            return new LightSnapshot[0];

        var snapshots = new LightSnapshot[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null)
                continue;

            snapshots[i] = new LightSnapshot
            {
                light = light,
                wasEnabled = light.enabled,
                intensity = light.intensity,
                range = light.range,
                spotAngle = light.spotAngle
            };
        }

        return snapshots;
    }

    private void ApplyLightingMode()
    {
        bool lightsOn = lightingMode != LightingMode.Off;
        bool mainBeamOn = lightingMode == LightingMode.MainBeam;

        if (FL != null && FL.activeSelf != lightsOn)
            FL.SetActive(lightsOn);
        if (TL != null && TL.activeSelf != lightsOn)
            TL.SetActive(lightsOn);

        ApplyHeadlightSnapshots(headlightDefaults, lightsOn,
            mainBeamOn && !hasSeparateMainBeam);
        ApplyHeadlightSnapshots(mainBeamDefaults, mainBeamOn, false);

        SetEmission(frontEmissionInstances, lightsOn);
        SetEmission(tailEmissionInstances, lightsOn);
    }

    private void ApplyHeadlightSnapshots(
        LightSnapshot[] snapshots,
        bool lightsOn,
        bool boostForMainBeam)
    {
        if (snapshots == null)
            return;

        foreach (LightSnapshot snapshot in snapshots)
        {
            if (snapshot.light == null)
                continue;

            snapshot.light.enabled = lightsOn && snapshot.wasEnabled;
            snapshot.light.intensity = boostForMainBeam
                ? snapshot.intensity * Mathf.Max(1f, mainBeamIntensityMultiplier)
                : snapshot.intensity;
            snapshot.light.range = boostForMainBeam
                ? snapshot.range * Mathf.Max(1f, mainBeamRangeMultiplier)
                : snapshot.range;
            snapshot.light.spotAngle = boostForMainBeam
                ? Mathf.Min(179f, snapshot.spotAngle * mainBeamSpotAngleMultiplier)
                : snapshot.spotAngle;
        }
    }

    private void ApplyBrakeLights(bool active)
    {
        brakeLightsActive = active;

        if (BL != null && BL.activeSelf != active)
            BL.SetActive(active);

        if (detectedBrakeLights != null)
        {
            foreach (LightSnapshot snapshot in brakeLightDefaults)
            {
                if (snapshot.light == null)
                    continue;

                snapshot.light.enabled = active && snapshot.wasEnabled;
            }
        }

        SetEmission(brakeEmissionInstances, active);
    }

    private Transform FindDescendant(string targetName)
    {
        if (transform.name == targetName)
            return transform;

        foreach (Transform child in transform)
        {
            if (child.name == targetName)
                return child;

            Transform nested = FindDescendantIn(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static Transform FindDescendantIn(Transform parent, string targetName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == targetName)
                return child;

            Transform nested = FindDescendantIn(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private void CacheEmissionMaterials(
        GameObject group,
        Material source,
        List<Material> destination,
        bool includeAllEmissiveMaterials)
    {
        if (group == null)
            return;

        Renderer[] renderers = group.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                bool isSourceMaterial = source != null && material == source;
                bool isAutoEmissive = includeAllEmissiveMaterials &&
                                      material.HasProperty("_EmissionColor");

                if (!isSourceMaterial && !isAutoEmissive)
                    continue;

                Material instance = new Material(material);
                materials[i] = instance;
                destination.Add(instance);
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private void CacheOptionalBrakeMaterials()
    {
        if (BL != null)
            CacheEmissionMaterials(BL, null, brakeEmissionInstances, true);

        if (brakeLightMaterials == null || brakeLightMaterials.Length == 0 || BL == null)
            return;

        // Also support an explicitly assigned brake material if it isn't detected
        // automatically in the brake-light group.
        foreach (Renderer renderer in BL.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                bool isConfigured = false;

                foreach (Material configuredMaterial in brakeLightMaterials)
                {
                    if (configuredMaterial != null && material == configuredMaterial)
                    {
                        isConfigured = true;
                        break;
                    }
                }

                if (!isConfigured)
                    continue;

                Material instance = new Material(material);
                materials[i] = instance;
                brakeEmissionInstances.Add(instance);
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static void SetEmission(List<Material> materials, bool enabled)
    {
        foreach (Material material in materials)
        {
            if (material == null)
                continue;

            if (enabled)
                material.EnableKeyword("_EMISSION");
            else
                material.DisableKeyword("_EMISSION");
        }
    }

    private void OnDestroy()
    {
        DestroyMaterialInstances(frontEmissionInstances);
        DestroyMaterialInstances(tailEmissionInstances);
        DestroyMaterialInstances(brakeEmissionInstances);
    }

    private static void DestroyMaterialInstances(List<Material> materials)
    {
        foreach (Material material in materials)
        {
            if (material != null)
                Destroy(material);
        }

        materials.Clear();
    }
}
