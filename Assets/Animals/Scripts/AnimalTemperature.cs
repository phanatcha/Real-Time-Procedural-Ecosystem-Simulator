using UnityEngine;

[DisallowMultipleComponent]
public class AnimalTemperature : MonoBehaviour
{
    [Header("Heritable Thermal Traits (Celsius)")]
    public float preferredTemperature = 20f;
    [Min(0f)] public float coldTolerance = 10f;
    [Min(0f)] public float heatTolerance = 10f;

    [Header("Response Tuning")]
    public bool enableTemperatureEffects = true;
    [Tooltip("Degrees beyond the comfort range that produce one unit of stress.")]
    [Min(0.1f)] public float degreesPerStressUnit = 20f;
    [Min(0f)] public float extraEnergyDrainPerStressUnit = 0.5f;
    [Tooltip("Stress where movement or recovery penalties begin. Damage begins at stress 1.")]
    [Range(0f, 0.99f)] public float severeStressThreshold = 0.5f;
    [Range(0.05f, 1f)] public float minimumColdMovementMultiplier = 0.5f;
    [Range(0f, 1f)] public float minimumHeatStaminaRecoveryMultiplier = 0.5f;
    [Min(0f)] public float extremeDamagePerStressUnit = 5f;
    [Tooltip("Maximum stress used for penalties, limiting extreme input temperatures.")]
    [Min(1f)] public float maximumStress = 3f;
    [Tooltip("Simulated seconds between position samples; at most one query per rendered frame.")]
    [Min(0.05f)] public float temperatureSampleInterval = 0.5f;

    [Header("Mutation Bounds")]
    public float minimumPreferredTemperature = -40f;
    public float maximumPreferredTemperature = 60f;
    [Min(0f)] public float maximumTolerance = 40f;
    [Tooltip("Preference mutation changes by up to this many degrees times the animal's mutation magnitude.")]
    [Min(0f)] public float preferenceMutationScale = 10f;

    [Header("Runtime Temperature")]
    [SerializeField] private bool hasTemperatureSample;
    [SerializeField] private float ambientTemperature = 20f;
    [SerializeField] private float coldStress;
    [SerializeField] private float heatStress;
    [SerializeField] private float energyDrainMultiplier = 1f;
    [SerializeField] private float movementMultiplier = 1f;
    [SerializeField] private float staminaRecoveryMultiplier = 1f;
    [SerializeField] private float thermalDamagePerSecond;
    private float sampleTimer;

    public bool HasTemperatureSample => hasTemperatureSample;
    public float AmbientTemperature => ambientTemperature;
    public float ColdStress => coldStress;
    public float HeatStress => heatStress;
    public float EnergyDrainMultiplier => energyDrainMultiplier;
    public float MovementMultiplier => movementMultiplier;
    public float StaminaRecoveryMultiplier => staminaRecoveryMultiplier;
    public float ThermalDamagePerSecond => thermalDamagePerSecond;
    public float MinimumComfortTemperature => preferredTemperature - Mathf.Max(0f, coldTolerance);
    public float MaximumComfortTemperature => preferredTemperature + Mathf.Max(0f, heatTolerance);

    public struct Response
    {
        public float coldStress, heatStress;
        public float energyMultiplier, movementMultiplier, recoveryMultiplier, damagePerSecond;
    }

    public Response EvaluateAtTemperature(float celsius)
    {
        Response result = new Response { energyMultiplier = 1f, movementMultiplier = 1f,
                                         recoveryMultiplier = 1f };
        if (!enableTemperatureEffects || float.IsNaN(celsius) || float.IsInfinity(celsius))
            return result;

        float span = Mathf.Max(0.1f, degreesPerStressUnit);
        float cap = Mathf.Max(1f, maximumStress);
        result.coldStress = Mathf.Clamp((MinimumComfortTemperature - celsius) / span, 0f, cap);
        result.heatStress = Mathf.Clamp((celsius - MaximumComfortTemperature) / span, 0f, cap);
        float stress = Mathf.Max(result.coldStress, result.heatStress);
        result.energyMultiplier = 1f + stress * Mathf.Max(0f, extraEnergyDrainPerStressUnit);
        float severeStart = Mathf.Clamp(severeStressThreshold, 0f, 0.99f);
        result.movementMultiplier = Mathf.Lerp(1f, Mathf.Clamp(minimumColdMovementMultiplier, 0.05f, 1f),
            Mathf.InverseLerp(severeStart, 1f, result.coldStress));
        result.recoveryMultiplier = Mathf.Lerp(1f, Mathf.Clamp01(minimumHeatStaminaRecoveryMultiplier),
            Mathf.InverseLerp(severeStart, 1f, result.heatStress));
        result.damagePerSecond = Mathf.Max(0f, stress - 1f) * Mathf.Max(0f, extremeDamagePerStressUnit);
        return result;
    }
    public void ResetRuntimeState()
    {
        sampleTimer = Mathf.Max(0.05f, temperatureSampleInterval);
        ApplyNeutralResponse();
        SampleTemperature();
    }

    public void Tick(float simulatedDeltaTime)
    {
        if (simulatedDeltaTime <= 0f) return;
        if (!isActiveAndEnabled || !enableTemperatureEffects)
        {
            ApplyNeutralResponse();
            sampleTimer = 0f;
            return;
        }

        sampleTimer -= simulatedDeltaTime;
        if (sampleTimer <= 0f)
        {
            SampleTemperature();
            float interval = Mathf.Max(0.05f, temperatureSampleInterval);
            sampleTimer = interval - Mathf.Repeat(-sampleTimer, interval);
        }
    }

    private void SampleTemperature()
    {
        if (!isActiveAndEnabled || !enableTemperatureEffects ||
            !TemperatureSystem.TryGetTemperatureAt(transform.position, out float celsius))
        {
            ApplyNeutralResponse();
            return;
        }

        hasTemperatureSample = true;
        ambientTemperature = celsius;
        Response response = EvaluateAtTemperature(celsius);
        coldStress = response.coldStress;
        heatStress = response.heatStress;
        energyDrainMultiplier = response.energyMultiplier;
        movementMultiplier = response.movementMultiplier;
        staminaRecoveryMultiplier = response.recoveryMultiplier;
        thermalDamagePerSecond = response.damagePerSecond;
    }

    private void ApplyNeutralResponse()
    {
        hasTemperatureSample = false;
        ambientTemperature = preferredTemperature;
        coldStress = heatStress = thermalDamagePerSecond = 0f;
        energyDrainMultiplier = movementMultiplier = staminaRecoveryMultiplier = 1f;
    }

    public bool InheritAndMutate(AnimalTemperature parent, float mutationChance, float magnitude)
    {
        float chance = Mathf.Clamp(mutationChance, 0f, 100f);
        float shift = Mathf.Max(0f, magnitude);
        preferredTemperature = parent.preferredTemperature;
        coldTolerance = parent.coldTolerance;
        heatTolerance = parent.heatTolerance;
        if (Random.Range(0f, 100f) < chance)
            preferredTemperature += Random.Range(-shift, shift) * Mathf.Max(0f, preferenceMutationScale);
        if (Random.Range(0f, 100f) < chance)
            coldTolerance += Random.Range(-shift, shift) * Mathf.Max(1f, parent.coldTolerance);
        if (Random.Range(0f, 100f) < chance)
            heatTolerance += Random.Range(-shift, shift) * Mathf.Max(1f, parent.heatTolerance);
        preferredTemperature = Mathf.Clamp(preferredTemperature, minimumPreferredTemperature,
            Mathf.Max(minimumPreferredTemperature, maximumPreferredTemperature));
        coldTolerance = Mathf.Clamp(coldTolerance, 0f, Mathf.Max(0f, maximumTolerance));
        heatTolerance = Mathf.Clamp(heatTolerance, 0f, Mathf.Max(0f, maximumTolerance));
        return preferredTemperature != parent.preferredTemperature ||
               coldTolerance != parent.coldTolerance || heatTolerance != parent.heatTolerance;
    }
}
