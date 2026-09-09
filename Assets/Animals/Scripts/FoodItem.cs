using System.Collections.Generic;
using UnityEngine;

public enum FoodType
{
    Plant,
    Meat
}

public class FoodItem : MonoBehaviour
{
    private static readonly HashSet<FoodItem> activeItems = new HashSet<FoodItem>();
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Food Properties")]
    public FoodType foodType = FoodType.Plant;
    public float nutritionValue = 50f;
    public string sourceSpeciesName;

    [Header("Accessibility")]
    [Tooltip("Optional minimum feeding reach. Vertical height is checked automatically; use this for branches or other access difficulty.")]
    [Min(0f)] public float requiredFeedingReach;

    [Header("Plant Growth Temperature")]
    public bool temperatureAffectsGrowth = true;
    [Tooltip("Test tuning in Celsius, not a biological profile. Growth rises from zero to the optimal range and falls to zero above it.")]
    public float minimumGrowthTemperature = 0f;
    public float optimalGrowthTemperatureMin = 15f;
    public float optimalGrowthTemperatureMax = 25f;
    public float maximumGrowthTemperature = 45f;

    [Header("Spoilage")]
    [Tooltip("Lifetime in simulated seconds at the reference temperature. Zero means this food does not spoil.")]
    [Min(0f)] public float referenceLifetime;
    public bool temperatureAffectsSpoilage = true;
    public float spoilageReferenceTemperature = 20f;
    [Tooltip("Rate increase per 10 Celsius. The final rate is bounded by the minimum and maximum multipliers.")]
    [Min(1f)] public float spoilageRatePerTenDegrees = 2f;
    [Min(0.01f)] public float minimumSpoilageMultiplier = 0.25f;
    [Min(0.01f)] public float maximumSpoilageMultiplier = 4f;
    [Min(0.05f)] public float temperatureSampleInterval = 0.5f;

    [Header("Spoilage Runtime")]
    [SerializeField, Range(0f, 1f)] private float freshness = 1f;
    [SerializeField] private float remainingReferenceLifetime;
    [SerializeField] private bool hasLocalTemperature;
    [SerializeField] private float localTemperatureCelsius = 20f;
    [SerializeField] private float currentSpoilageMultiplier = 1f;

    private bool consumed;
    private float temperatureSampleTimer;

    public static IEnumerable<FoodItem> ActiveItems => activeItems;
    public static int ActiveCount => activeItems.Count;
    public bool IsAvailable => !consumed && isActiveAndEnabled;
    public bool IsPerishable => referenceLifetime > 0f && IsFinite(referenceLifetime);
    public float Freshness => freshness;
    public float RemainingReferenceLifetime => remainingReferenceLifetime;
    public float CurrentSpoilageMultiplier => currentSpoilageMultiplier;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        activeItems.Clear();
    }

    void OnEnable()
    {
        consumed = false;
        ResetFreshness();
        activeItems.Add(this);
    }

    void OnDisable()
    {
        activeItems.Remove(this);
    }

    public void Configure(FoodType type, float rawNutrition, string sourceSpecies = "", float lifetime = 0f,
                          float requiredReach = 0f)
    {
        foodType = type;
        nutritionValue = Mathf.Max(0f, rawNutrition);
        sourceSpeciesName = sourceSpecies;
        requiredFeedingReach = Mathf.Max(0f, requiredReach);
        referenceLifetime = IsFinite(lifetime) ? Mathf.Max(0f, lifetime) : 0f;
        ResetFreshness();
    }

    void Update()
    {
        AdvanceSpoilage(Time.deltaTime);
    }

    public void AdvanceSpoilage(float elapsed)
    {
        if (!IsAvailable || !IsPerishable || !IsFinite(elapsed) || elapsed <= 0f)
        {
            return;
        }

        temperatureSampleTimer -= elapsed;
        if (temperatureSampleTimer <= 0f)
        {
            SampleTemperature();
        }

        freshness = Mathf.Max(0f, freshness - elapsed * currentSpoilageMultiplier / referenceLifetime);
        remainingReferenceLifetime = freshness * referenceLifetime;
        if (freshness <= 0f)
        {
            RemoveFromSimulation();
        }
    }

    void ResetFreshness()
    {
        freshness = 1f;
        remainingReferenceLifetime = IsPerishable ? referenceLifetime : 0f;
        SampleTemperature();
    }

    void SampleTemperature()
    {
        temperatureSampleTimer = Mathf.Max(0.05f, FiniteOr(temperatureSampleInterval, 0.5f));
        hasLocalTemperature = temperatureAffectsSpoilage && IsPerishable &&
            TemperatureSystem.TryGetTemperatureAt(transform.position, out localTemperatureCelsius);
        currentSpoilageMultiplier = hasLocalTemperature ? EvaluateSpoilageMultiplier(localTemperatureCelsius) : 1f;
    }

    public float EvaluateGrowthMultiplier(float temperatureCelsius)
    {
        if (!temperatureAffectsGrowth || foodType != FoodType.Plant || !IsFinite(temperatureCelsius))
        {
            return 1f;
        }

        float minimum = FiniteOr(minimumGrowthTemperature, 0f);
        float maximum = Mathf.Max(minimum + 0.01f, FiniteOr(maximumGrowthTemperature, 45f));
        float optimalMin = Mathf.Clamp(FiniteOr(optimalGrowthTemperatureMin, 15f), minimum, maximum);
        float optimalMax = Mathf.Clamp(FiniteOr(optimalGrowthTemperatureMax, 25f), optimalMin, maximum);
        if (temperatureCelsius <= minimum || temperatureCelsius >= maximum)
        {
            return 0f;
        }

        if (temperatureCelsius < optimalMin)
        {
            return Mathf.InverseLerp(minimum, optimalMin, temperatureCelsius);
        }

        return temperatureCelsius <= optimalMax ? 1f :
            1f - Mathf.InverseLerp(optimalMax, maximum, temperatureCelsius);
    }

    public float EvaluateSpoilageMultiplier(float temperatureCelsius)
    {
        if (!temperatureAffectsSpoilage || !IsFinite(temperatureCelsius))
        {
            return 1f;
        }

        float minimum = Mathf.Clamp(FiniteOr(minimumSpoilageMultiplier, 0.25f), 0.01f, 1f);
        float maximum = Mathf.Max(1f, FiniteOr(maximumSpoilageMultiplier, 4f));
        float factor = Mathf.Max(1f, FiniteOr(spoilageRatePerTenDegrees, 2f));
        float exponent = (temperatureCelsius - FiniteOr(spoilageReferenceTemperature, 20f)) / 10f;
        return Mathf.Clamp(Mathf.Pow(factor, exponent), minimum, maximum);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static float FiniteOr(float value, float fallback)
    {
        return IsFinite(value) ? value : fallback;
    }

    public bool TryConsume(out float nutrition, out FoodType consumedType)
    {
        nutrition = 0f;
        consumedType = foodType;
        if (!IsAvailable)
        {
            return false;
        }

        nutrition = nutritionValue;
        RemoveFromSimulation();
        return true;
    }

    void RemoveFromSimulation()
    {
        consumed = true;
        activeItems.Remove(this);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    public bool TryConsume(out float nutrition)
    {
        return TryConsume(out nutrition, out _);
    }

    public static FoodItem CreateCarcass(Vector3 position, float rawNutrition, string sourceSpecies,
                                         float lifetime, float scale)
    {
        if (rawNutrition <= 0f)
        {
            return null;
        }

        GameObject carcass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        carcass.name = $"Carcass_{sourceSpecies}";
        carcass.tag = "Food";
        carcass.transform.position = position;
        carcass.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

        Collider collider = carcass.GetComponent<Collider>();
        collider.isTrigger = true;

        Rigidbody rigidbody = carcass.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        FoodItem foodItem = carcass.AddComponent<FoodItem>();
        foodItem.Configure(FoodType.Meat, rawNutrition, sourceSpecies, lifetime);

        MeshRenderer renderer = carcass.GetComponent<MeshRenderer>();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        Color carcassColor = new Color(0.45f, 0.08f, 0.05f, 1f);
        propertyBlock.SetColor(BaseColorId, carcassColor);
        propertyBlock.SetColor(ColorId, carcassColor);
        renderer.SetPropertyBlock(propertyBlock);

        return foodItem;
    }
}
