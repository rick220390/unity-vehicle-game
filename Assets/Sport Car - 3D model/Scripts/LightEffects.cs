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

    [Header("HDRP Emission Intensity")]
    [Tooltip("HDRP Lit materials named with 'leds-rear' use these emission intensities when the tail lights are on/off.")]
    [Min(0f)] public float rearLedOnIntensity = 60f;
    [Min(0f)] public float rearLedOffIntensity = 5f;

    [Tooltip("HDRP Lit materials named with 'white-lights' use these intensities when the headlights are on/off.")]
    [Min(0f)] public float frontLampOnIntensity = 200f;
    [Min(0f)] public float frontLampOffIntensity = 6f;

    [Tooltip("HDRP Lit materials named with 'brake-lights' use these intensities while braking/not braking.")]
    [Min(0f)] public float brakeLampOnIntensity = 588.1335f;
    [Min(0f)] public float brakeLampOffIntensity = 15f;

    [Header("Other Parked Vehicle Emission")]
    [Tooltip("Multiplier for emissive materials not matched by the named HDRP light-material rules, while the vehicle is unoccupied and exterior lights are off.")]
    [Range(0f, 1f)] public float unoccupiedEmissionMultiplier = 0.05f;

    private enum EmissionChannel
    {
        Generic,
        RearLeds,
        FrontLamps,
        BrakeLamps
    }

    private struct EmissionSnapshot
    {
        public Material material;
        public string colorProperty;
        public string intensityProperty;
        public Color originalColor;
        public float originalIntensity;
        public EmissionChannel channel;
    }

    private Vehicle vehicle;
    private LightingMode lightingMode = LightingMode.Off;
    private Light[] detectedBrakeLights;
    private Light[] detectedTailLights;
    private LightSnapshot[] headlightDefaults;
    private LightSnapshot[] mainBeamDefaults;
    private LightSnapshot[] brakeLightDefaults;
    private bool hasSeparateMainBeam;
    private bool brakeLightsActive;
    private float appliedEmissionMultiplier = -1f;

    private readonly List<Material> frontEmissionInstances = new List<Material>();
    private readonly List<Material> tailEmissionInstances = new List<Material>();
    private readonly List<Material> brakeEmissionInstances = new List<Material>();
    private readonly List<EmissionSnapshot> vehicleEmissionSnapshots = new List<EmissionSnapshot>();

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        if (vehicle == null)
            vehicle = GetComponentInParent<Vehicle>();
        if (vehicle == null)
            vehicle = GetComponentInChildren<Vehicle>(true);

        if (FL == null)
            FL = FindDescendant("FrontLights")?.gameObject;

        // Prefer the actual named BrakeLights group over any legacy serialized
        // BL reference, which may point at an older object from the original asset.
        GameObject namedBrakeGroup = FindDescendant("BrakeLights")?.gameObject;
        if (namedBrakeGroup != null)
            BL = namedBrakeGroup;
        else if (BL == null)
            BL = FindDescendant("BrakeLights")?.gameObject;

        if (TL == null)
            TL = FindDescendant("TailLights")?.gameObject;

        CacheHeadlights();
        CacheMainBeamLights();
        CacheTailLights();
        CacheBrakeLights();

        headlightDefaults = CaptureLights(headlightSpots);
        mainBeamDefaults = CaptureLights(mainBeamLights);
        brakeLightDefaults = CaptureLights(detectedBrakeLights);
        hasSeparateMainBeam = mainBeamLights != null && mainBeamLights.Length > 0;

        // Cache per-renderer material instances and their original emission so
        // parked cars can be dimmed without modifying shared project materials.
        CacheVehicleEmissionMaterials();

        // Keep lamp meshes active so their low, realistic off-state emission can
        // still be rendered. Control actual illumination with Light components
        // and the per-material HDRP emission intensities instead.
        if (FL != null && !FL.activeSelf) FL.SetActive(true);
        if (TL != null && !TL.activeSelf) TL.SetActive(true);
        if (BL != null && !BL.activeSelf) BL.SetActive(true);

        ApplyLightingMode();
        ApplyBrakeLights(false);
        UpdateUnoccupiedEmissionDimming();
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

        UpdateUnoccupiedEmissionDimming();
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

    private void CacheTailLights()
    {
        if (TL == null)
        {
            detectedTailLights = new Light[0];
            return;
        }

        // Tail-light spotlights are separate from the emissive tail-lamp meshes.
        // Keep the group and meshes active for the dim off-state glow, but control
        // every actual Light component here so it cannot remain lit when parked.
        detectedTailLights = TL.GetComponentsInChildren<Light>(true);
    }

    private void CacheBrakeLights()
    {
        var found = new List<Light>();

        // Every Light inside the dedicated BrakeLights group is a brake light.
        // Do not rely on the individual spotlight object's name: imported models
        // often give these children generic names such as "Spot Light".
        if (BL != null)
        {
            Light[] groupLights = BL.GetComponentsInChildren<Light>(true);
            foreach (Light candidate in groupLights)
            {
                if (candidate != null && !found.Contains(candidate))
                    found.Add(candidate);
            }
        }

        // Also support brake spotlights outside the group when their object name
        // explicitly identifies them. This helps older prefab layouts.
        Light[] allLights = GetComponentsInChildren<Light>(true);
        foreach (Light candidate in allLights)
        {
            if (candidate == null)
                continue;

            if (candidate.name.ToLowerInvariant().Contains("brakelight") &&
                !found.Contains(candidate))
                found.Add(candidate);
        }

        detectedBrakeLights = found.ToArray();
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

        // Keep lamp meshes active so the configured low off-state glow can show.
        // Actual light beams are controlled by the Light components below.
        ApplyHeadlightSnapshots(headlightDefaults, lightsOn,
            mainBeamOn && !hasSeparateMainBeam);
        ApplyHeadlightSnapshots(mainBeamDefaults, mainBeamOn, false);
        SetGroupLightsEnabled(detectedTailLights, lightsOn);
    }

    private static void SetGroupLightsEnabled(Light[] lights, bool enabled)
    {
        if (lights == null)
            return;

        foreach (Light light in lights)
        {
            if (light != null)
                light.enabled = enabled;
        }
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

        // Keep brake-lamp meshes active to show their configured low off-state
        // emission. The actual brake-light components turn on only while braking.
        if (BL != null && !BL.activeSelf)
            BL.SetActive(true);

        if (detectedBrakeLights != null)
        {
            foreach (Light light in detectedBrakeLights)
            {
                if (light == null)
                    continue;

                // Explicitly drive the light component to the requested state.
                // Don't preserve its prefab enabled flag: brake spots must be off
                // at rest and enabled whenever the player actually brakes.
                light.enabled = active;
            }
        }
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

    private void CacheVehicleEmissionMaterials()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            // Renderer.materials gives this renderer its own material instances,
            // avoiding changes to shared assets or other cars using the same model.
            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                // HDRP Lit may expose intensity separately from its emissive
                // colour, while other shaders bake intensity into the colour.
                string colorProperty = material.HasProperty("_EmissiveColor")
                    ? "_EmissiveColor"
                    : material.HasProperty("_EmissionColor")
                        ? "_EmissionColor"
                        : null;
                string intensityProperty = material.HasProperty("_EmissiveIntensity")
                    ? "_EmissiveIntensity"
                    : null;

                // The names such as "leds-rear" may be on the GameObject, not
                // on the Material asset itself. Match both the material and the
                // renderer's hierarchy so imported car meshes are detected too.
                string hierarchyNames = GetHierarchyNames(renderer.transform);
                EmissionChannel channel = GetEmissionChannel(material.name + " " + hierarchyNames);
                if (string.IsNullOrEmpty(colorProperty) && string.IsNullOrEmpty(intensityProperty))
                    continue;

                vehicleEmissionSnapshots.Add(new EmissionSnapshot
                {
                    material = material,
                    colorProperty = colorProperty,
                    intensityProperty = intensityProperty,
                    originalColor = string.IsNullOrEmpty(colorProperty)
                        ? Color.white
                        : material.GetColor(colorProperty),
                    originalIntensity = string.IsNullOrEmpty(intensityProperty)
                        ? 1f
                        : material.GetFloat(intensityProperty),
                    channel = channel
                });
            }
            renderer.sharedMaterials = materials;
        }
    }

    private void UpdateUnoccupiedEmissionDimming()
    {
        bool dimForParking = lightingMode == LightingMode.Off &&
                             (vehicle == null || !vehicle.HasDriver);
        float genericMultiplier = dimForParking
            ? Mathf.Clamp01(unoccupiedEmissionMultiplier)
            : 1f;

        bool exteriorLightsOn = lightingMode != LightingMode.Off;

        foreach (EmissionSnapshot snapshot in vehicleEmissionSnapshots)
        {
            if (snapshot.material == null)
                continue;

            switch (snapshot.channel)
            {
                case EmissionChannel.RearLeds:
                    ApplyConfiguredEmission(snapshot, exteriorLightsOn
                        ? rearLedOnIntensity : rearLedOffIntensity, rearLedOnIntensity);
                    break;

                case EmissionChannel.FrontLamps:
                    ApplyConfiguredEmission(snapshot, exteriorLightsOn
                        ? frontLampOnIntensity : frontLampOffIntensity, frontLampOnIntensity);
                    break;

                case EmissionChannel.BrakeLamps:
                    ApplyConfiguredEmission(snapshot, brakeLightsActive
                        ? brakeLampOnIntensity : brakeLampOffIntensity, brakeLampOnIntensity);
                    break;

                default:
                    // For generic materials prefer HDRP's separate intensity
                    // property where available, so colour and intensity are not
                    // both multiplied (which would dim the material twice).
                    if (!string.IsNullOrEmpty(snapshot.intensityProperty))
                    {
                        snapshot.material.SetFloat(snapshot.intensityProperty,
                            snapshot.originalIntensity * genericMultiplier);

                        if (!string.IsNullOrEmpty(snapshot.colorProperty))
                            snapshot.material.SetColor(snapshot.colorProperty, snapshot.originalColor);
                    }
                    else if (!string.IsNullOrEmpty(snapshot.colorProperty))
                    {
                        snapshot.material.SetColor(snapshot.colorProperty,
                            snapshot.originalColor * genericMultiplier);
                    }
                    break;
            }
        }

        appliedEmissionMultiplier = genericMultiplier;
    }

    private void ApplyConfiguredEmission(
        EmissionSnapshot snapshot,
        float targetIntensity,
        float referenceOnIntensity)
    {
        if (!string.IsNullOrEmpty(snapshot.intensityProperty))
        {
            // Important HDRP Lit behaviour: setting _EmissiveIntensity at runtime
            // alone does NOT change visible emission. HDRP serializes that field
            // in the Inspector but stores the effective emissive power in
            // _EmissiveColor. Rescale that colour from the material's original
            // Inspector intensity to the requested on/off intensity.
            float sourceIntensity = snapshot.originalIntensity > 0.0001f
                ? snapshot.originalIntensity
                : Mathf.Max(0.0001f, referenceOnIntensity);

            if (!string.IsNullOrEmpty(snapshot.colorProperty))
            {
                float colourMultiplier = targetIntensity / sourceIntensity;
                snapshot.material.SetColor(
                    snapshot.colorProperty,
                    snapshot.originalColor * colourMultiplier);
            }

            // Keep the serialized HDRP Inspector value in sync as well. The
            // _EmissiveColor change above is what drives the visible runtime result.
            snapshot.material.SetFloat(snapshot.intensityProperty, targetIntensity);
        }
        else if (!string.IsNullOrEmpty(snapshot.colorProperty))
        {
            // Some shaders store intensity solely in the HDR emissive colour.
            float denominator = Mathf.Max(0.0001f, referenceOnIntensity);
            float colourMultiplier = targetIntensity / denominator;
            snapshot.material.SetColor(snapshot.colorProperty,
                snapshot.originalColor * colourMultiplier);
        }
    }

    private static string GetHierarchyNames(Transform current)
    {
        var names = new System.Text.StringBuilder();
        while (current != null)
        {
            names.Append(current.name).Append(' ');
            current = current.parent;
        }

        return names.ToString();
    }

    private static EmissionChannel GetEmissionChannel(string names)
    {
        if (string.IsNullOrEmpty(names))
            return EmissionChannel.Generic;

        string normalized = names.ToLowerInvariant()
            .Replace("_", "-")
            .Replace(" ", "-");

        if (normalized.Contains("leds-rear") || normalized.Contains("led-rear"))
            return EmissionChannel.RearLeds;
        if (normalized.Contains("brake-lights") || normalized.Contains("brake-light"))
            return EmissionChannel.BrakeLamps;
        if (normalized.Contains("white-lights") || normalized.Contains("white-light"))
            return EmissionChannel.FrontLamps;

        return EmissionChannel.Generic;
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

        foreach (EmissionSnapshot snapshot in vehicleEmissionSnapshots)
        {
            if (snapshot.material != null)
                Destroy(snapshot.material);
        }
        vehicleEmissionSnapshots.Clear();
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
