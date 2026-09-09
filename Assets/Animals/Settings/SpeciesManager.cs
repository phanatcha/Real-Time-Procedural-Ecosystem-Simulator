using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum AgentDeathCause
{
    Starvation,
    Predation,
    OldAge,
    Other,
    ColdExposure,
    HeatExposure
}

[System.Serializable]
public sealed class SpeciesTelemetryRecord
{
    public string speciesName;
    public float dietAffinity;
    public string dietClassification;
    public float strength;
    public float bodyBulk;
    public float bodyHeight;
    public float bodyMassFactor;
    public float feedingReach;
    public float maxStamina;
    public float maturityTime;
    public float maxLifespan;
    public float preferredTemperature;
    public float coldTolerance;
    public float heatTolerance;
    public int births;
    public int deaths;
    public int starvationDeaths;
    public int predationDeaths;
    public int oldAgeDeaths;
    public int coldExposureDeaths;
    public int heatExposureDeaths;
    public int fightResponses;
    public int fleeResponses;
    public int reproductionEvents;
    public int mutatedOffspring;
    public int plantMeals;
    public int meatMeals;
    public float rawPlantEnergyConsumed;
    public float rawMeatEnergyConsumed;
    public float digestiblePlantEnergyGained;
    public float digestibleMeatEnergyGained;
    public int successfulKills;
}

public class SpeciesManager : MonoBehaviour
{
    public static SpeciesManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [Header("Simulation Controls")]
    [Tooltip("Requested simulation speed. 1 = normal speed, 50 = 50x fast-forward, and 0 pauses the simulation.")]
    [InspectorName("Requested Simulation Speed")]
    [Range(0f, 50f)]
    public float simulationSpeed = 1f;

    [Header("Fast-Forward Performance")]
    [Tooltip("Scales the physics timestep up to the protected-mode threshold. Speeds above the threshold are always CPU-budgeted.")]
    public bool optimizePhysicsForFastForward = true;
    [Tooltip("Largest allowed physics step below protected fast-forward. 0.2 keeps 20x mode at about 100 physics steps per real second.")]
    [Min(0.02f)] public float maximumSimulationFixedStep = 0.2f;
    [Tooltip("Speeds above this value use the protected high-speed physics budget.")]
    [Range(1f, 49f)] public float protectedFastForwardThreshold = 20f;
    [Tooltip("Target upper limit for physics updates per real second in protected fast-forward.")]
    [Min(10f)] public float protectedPhysicsStepsPerRealSecond = 100f;
    [Tooltip("Maximum simulated time Unity may process in one rendered frame during protected fast-forward. If the computer falls behind, achieved speed drops instead of creating an unlimited catch-up workload.")]
    [Min(0.02f)] public float protectedMaximumDeltaTime = 1f;

    [Header("Fast-Forward Safety")]
    [Tooltip("Pauses protected fast-forward after sustained severe frame-rate loss or when the emergency population ceiling is reached.")]
    public bool enableSafetyPause = true;
    [Tooltip("Protected fast-forward pauses if measured frame rate remains below this value for the grace period.")]
    [Min(1f)] public float minimumSafeFrameRate = 10f;
    [Tooltip("How long severe frame-rate loss must persist before the safety pause activates.")]
    [Min(0.5f)] public float lowFrameRateGracePeriod = 3f;
    [Tooltip("Emergency population ceiling for protected fast-forward. Set to 0 to disable. Births are never silently blocked.")]
    [Min(0)] public int emergencyPopulationLimit = 500;

    [Header("Runtime Speed Diagnostics")]
    [SerializeField, Tooltip("Speed currently applied after safety handling.")]
    private float appliedSimulationSpeed = 1f;
    [SerializeField, Tooltip("Measured simulated seconds advanced per real second.")]
    private float achievedSimulationSpeed = 1f;
    [SerializeField] private float measuredFrameRate;
    [SerializeField] private bool protectedFastForwardActive;
    [SerializeField] private bool safetyPaused;
    [SerializeField, TextArea] private string lastSafetyPauseReason;

    [Header("Telemetry")]
    [Tooltip("Enables individual birth, death, speciation, and extinction messages. Leave off for large or fast simulations.")]
    public bool logLifecycleEvents;
    public bool logTelemetryToConsole;
    [Min(1f)] public float telemetryLogInterval = 30f;

    private int currentSpeciesIndex;
    private float telemetryTimer;
    private float baseFixedDeltaTime;
    private float baseMaximumDeltaTime;
    private float performanceMeasurementRealTime;
    private float performanceMeasurementSimulatedTime;
    private float lastPerformanceSampleDuration;
    private float lowFrameRateDuration;
    private int performanceMeasurementFrames;
    
    private readonly Dictionary<string, int> speciesPopulation = new Dictionary<string, int>();
    private readonly Dictionary<string, Color> speciesColors = new Dictionary<string, Color>();
    private readonly HashSet<string> assignedSpeciesNames = new HashSet<string>();
    private readonly HashSet<SeekFood> activeAgents = new HashSet<SeekFood>();
    private readonly Dictionary<string, SpeciesTelemetryRecord> telemetry = new Dictionary<string, SpeciesTelemetryRecord>();

    public IReadOnlyDictionary<string, int> SpeciesPopulation => speciesPopulation;
    public IReadOnlyDictionary<string, SpeciesTelemetryRecord> Telemetry => telemetry;
    public IEnumerable<SeekFood> ActiveAgents => activeAgents;
    public int TotalPopulation { get; private set; }
    public bool ShouldLogLifecycleEvents => logLifecycleEvents;
    public float AppliedSimulationSpeed => appliedSimulationSpeed;
    public float AchievedSimulationSpeed => achievedSimulationSpeed;
    public float MeasuredFrameRate => measuredFrameRate;
    public bool IsProtectedFastForwardActive => protectedFastForwardActive;
    public bool IsSafetyPaused => safetyPaused;
    public string LastSafetyPauseReason => lastSafetyPauseReason;

    void Update()
    {
        float requestedSpeed = Mathf.Clamp(simulationSpeed, 0f, 50f);
        float protectedThreshold = Mathf.Clamp(protectedFastForwardThreshold, 1f, 49f);

        if (safetyPaused && (!enableSafetyPause || requestedSpeed <= protectedThreshold))
        {
            ResumeAfterSafetyPause();
        }

        ApplyTimeSettings(requestedSpeed, protectedThreshold);
        bool completedPerformanceSample = UpdatePerformanceDiagnostics();
        EvaluateFastForwardSafety(requestedSpeed, protectedThreshold,
                                  completedPerformanceSample);

        if (logTelemetryToConsole)
        {
            telemetryTimer += Time.deltaTime;
            if (telemetryTimer >= telemetryLogInterval)
            {
                telemetryTimer -= telemetryLogInterval;
                LogTelemetrySnapshot();
            }
        }
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            baseFixedDeltaTime = Time.fixedDeltaTime;
            baseMaximumDeltaTime = Time.maximumDeltaTime;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDeltaTime;
            Time.maximumDeltaTime = baseMaximumDeltaTime;
        }
    }

    void ApplyTimeSettings(float requestedSpeed, float protectedThreshold)
    {
        appliedSimulationSpeed = safetyPaused ? 0f : requestedSpeed;
        protectedFastForwardActive = appliedSimulationSpeed > protectedThreshold;
        Time.timeScale = appliedSimulationSpeed;

        if (appliedSimulationSpeed <= 0f)
        {
            Time.fixedDeltaTime = baseFixedDeltaTime;
            Time.maximumDeltaTime = baseMaximumDeltaTime;
            return;
        }

        if (protectedFastForwardActive)
        {
            float physicsBudget = Mathf.Max(10f, protectedPhysicsStepsPerRealSecond);
            Time.fixedDeltaTime = Mathf.Max(baseFixedDeltaTime,
                                            appliedSimulationSpeed / physicsBudget);
            Time.maximumDeltaTime = Mathf.Max(Time.fixedDeltaTime,
                                              protectedMaximumDeltaTime);
            return;
        }

        Time.fixedDeltaTime = optimizePhysicsForFastForward
            ? Mathf.Min(baseFixedDeltaTime * appliedSimulationSpeed,
                        Mathf.Max(baseFixedDeltaTime, maximumSimulationFixedStep))
            : baseFixedDeltaTime;
        Time.maximumDeltaTime = baseMaximumDeltaTime;
    }

    bool UpdatePerformanceDiagnostics()
    {
        float realDeltaTime = Time.unscaledDeltaTime;
        performanceMeasurementRealTime += realDeltaTime;
        performanceMeasurementSimulatedTime += Time.deltaTime;
        performanceMeasurementFrames++;

        if (performanceMeasurementRealTime < 1f)
        {
            return false;
        }

        achievedSimulationSpeed = performanceMeasurementSimulatedTime /
                                  Mathf.Max(0.001f, performanceMeasurementRealTime);
        measuredFrameRate = performanceMeasurementFrames /
                            Mathf.Max(0.001f, performanceMeasurementRealTime);
        lastPerformanceSampleDuration = performanceMeasurementRealTime;
        performanceMeasurementRealTime = 0f;
        performanceMeasurementSimulatedTime = 0f;
        performanceMeasurementFrames = 0;
        return true;
    }

    void EvaluateFastForwardSafety(float requestedSpeed, float protectedThreshold,
                                   bool completedPerformanceSample)
    {
        if (!enableSafetyPause || safetyPaused || requestedSpeed <= protectedThreshold)
        {
            lowFrameRateDuration = 0f;
            return;
        }

        if (emergencyPopulationLimit > 0 && TotalPopulation >= emergencyPopulationLimit)
        {
            TriggerSafetyPause($"Population reached the emergency ceiling of " +
                               $"{emergencyPopulationLimit}. Lower the requested speed or " +
                               "raise the ceiling before resuming.");
            return;
        }

        if (!completedPerformanceSample)
        {
            return;
        }

        if (measuredFrameRate < Mathf.Max(1f, minimumSafeFrameRate))
        {
            lowFrameRateDuration += lastPerformanceSampleDuration;
            if (lowFrameRateDuration >= Mathf.Max(0.5f, lowFrameRateGracePeriod))
            {
                TriggerSafetyPause($"Frame rate remained below {minimumSafeFrameRate:F0} FPS " +
                                   $"for {lowFrameRateDuration:F0} seconds.");
            }
        }
        else
        {
            lowFrameRateDuration = 0f;
        }
    }

    void TriggerSafetyPause(string reason)
    {
        safetyPaused = true;
        lastSafetyPauseReason = reason;
        appliedSimulationSpeed = 0f;
        protectedFastForwardActive = false;
        Time.timeScale = 0f;
        Time.fixedDeltaTime = baseFixedDeltaTime;
        Time.maximumDeltaTime = baseMaximumDeltaTime;
        Debug.LogWarning($"Fast-forward safety pause: {reason}");
    }

    [ContextMenu("Resume After Safety Pause")]
    public void ResumeAfterSafetyPause()
    {
        safetyPaused = false;
        lastSafetyPauseReason = "";
        lowFrameRateDuration = 0f;
    }

    public string GetNextSpeciesName()
    {
        string name;
        do
        {
            name = SpeciesNameFromIndex(currentSpeciesIndex++);
        }
        while (assignedSpeciesNames.Contains(name));

        assignedSpeciesNames.Add(name);
        return name;
    }

    public static string SpeciesNameFromIndex(int index)
    {
        int number = Mathf.Max(0, index);
        string name = "";

        while (number >= 0)
        {
            name = (char)('A' + (number % 26)) + name;
            number = (number / 26) - 1;
        }

        return name;
    }

    public Color GenerateUniqueColor()
    {
        Color newColor;
        bool isUnique;
        int safetyCounter = 0;

        do
        {
            newColor = new Color(Random.value, Random.value, Random.value);
            isUnique = true;

            foreach (var existingColor in speciesColors.Values)
            {
                float diff = Mathf.Abs(existingColor.r - newColor.r) + 
                             Mathf.Abs(existingColor.g - newColor.g) + 
                             Mathf.Abs(existingColor.b - newColor.b);
                
                if (diff < 0.4f) 
                {
                    isUnique = false;
                    break;
                }
            }
            safetyCounter++;
        } while (!isUnique && safetyCounter < 100); 

        return newColor;
    }

    public void RegisterAgent(SeekFood agent)
    {
        if (agent == null || !activeAgents.Add(agent))
        {
            return;
        }

        string speciesName = agent.speciesName;
        Color color = agent.speciesColor;
        assignedSpeciesNames.Add(speciesName);

        if (!speciesPopulation.ContainsKey(speciesName))
        {
            speciesPopulation[speciesName] = 0;
            speciesColors[speciesName] = color;
        }
        speciesPopulation[speciesName]++;
        TotalPopulation++;
        SpeciesTelemetryRecord telemetryRecord = GetOrCreateTelemetry(speciesName);
        if (telemetryRecord.births == 0)
        {
            telemetryRecord.dietAffinity = agent.dietAffinity;
            telemetryRecord.dietClassification = agent.DietClassification;
            telemetryRecord.strength = agent.strength;
            telemetryRecord.bodyBulk = agent.bodyBulk;
            telemetryRecord.bodyHeight = agent.bodyHeight;
            telemetryRecord.bodyMassFactor = agent.BodyMassFactor;
            telemetryRecord.feedingReach = agent.FeedingReach;
            telemetryRecord.maxStamina = agent.maxStamina;
            telemetryRecord.maturityTime = agent.maturityTime;
            telemetryRecord.maxLifespan = agent.maxLifespan;
            if (agent.ThermalResponse != null)
            {
                telemetryRecord.preferredTemperature = agent.ThermalResponse.preferredTemperature;
                telemetryRecord.coldTolerance = agent.ThermalResponse.coldTolerance;
                telemetryRecord.heatTolerance = agent.ThermalResponse.heatTolerance;
            }
        }
        telemetryRecord.births++;
    }

    public void DeregisterAgent(SeekFood agent, AgentDeathCause deathCause)
    {
        if (agent == null || !activeAgents.Remove(agent))
        {
            return;
        }

        string speciesName = agent.speciesName;
        if (speciesPopulation.ContainsKey(speciesName))
        {
            speciesPopulation[speciesName]--;
            TotalPopulation = Mathf.Max(0, TotalPopulation - 1);
            SpeciesTelemetryRecord record = GetOrCreateTelemetry(speciesName);
            record.deaths++;
            if (deathCause == AgentDeathCause.Starvation) record.starvationDeaths++;
            if (deathCause == AgentDeathCause.Predation) record.predationDeaths++;
            if (deathCause == AgentDeathCause.OldAge) record.oldAgeDeaths++;
            if (deathCause == AgentDeathCause.ColdExposure) record.coldExposureDeaths++;
            if (deathCause == AgentDeathCause.HeatExposure) record.heatExposureDeaths++;
            
            if (speciesPopulation[speciesName] <= 0)
            {
                speciesPopulation.Remove(speciesName);
                speciesColors.Remove(speciesName);
                if (logLifecycleEvents)
                {
                    Debug.Log($"Species {speciesName} has gone extinct! Color recycled.");
                }
            }
        }
    }

    public void RecordConsumption(SeekFood agent, FoodType foodType, float rawEnergy, float digestibleEnergy)
    {
        if (agent == null)
        {
            return;
        }

        SpeciesTelemetryRecord record = GetOrCreateTelemetry(agent.speciesName);
        if (foodType == FoodType.Plant)
        {
            record.plantMeals++;
            record.rawPlantEnergyConsumed += rawEnergy;
            record.digestiblePlantEnergyGained += digestibleEnergy;
        }
        else
        {
            record.meatMeals++;
            record.rawMeatEnergyConsumed += rawEnergy;
            record.digestibleMeatEnergyGained += digestibleEnergy;
        }
    }

    public void RecordReproduction(SeekFood parent, string childSpeciesName)
    {
        if (parent == null)
        {
            return;
        }

        SpeciesTelemetryRecord record = GetOrCreateTelemetry(parent.speciesName);
        record.reproductionEvents++;
        if (childSpeciesName != parent.speciesName)
        {
            record.mutatedOffspring++;
        }
    }

    public void RecordKill(SeekFood predator)
    {
        if (predator != null)
        {
            GetOrCreateTelemetry(predator.speciesName).successfulKills++;
        }
    }

    public void RecordThreatResponse(SeekFood agent, bool foughtBack)
    {
        if (agent == null)
        {
            return;
        }

        SpeciesTelemetryRecord record = GetOrCreateTelemetry(agent.speciesName);
        if (foughtBack)
        {
            record.fightResponses++;
        }
        else
        {
            record.fleeResponses++;
        }
    }

    SpeciesTelemetryRecord GetOrCreateTelemetry(string speciesName)
    {
        if (!telemetry.TryGetValue(speciesName, out SpeciesTelemetryRecord record))
        {
            record = new SpeciesTelemetryRecord { speciesName = speciesName };
            telemetry.Add(speciesName, record);
        }

        return record;
    }

    [ContextMenu("Log Telemetry Snapshot")]
    public void LogTelemetrySnapshot()
    {
        StringBuilder builder = new StringBuilder("Ecosystem telemetry");
        foreach (SpeciesTelemetryRecord record in telemetry.Values)
        {
            speciesPopulation.TryGetValue(record.speciesName, out int living);
            builder.Append($"\n{record.speciesName} ({record.dietClassification}, {record.dietAffinity:F2}, ");
            builder.Append($"strength={record.strength:F1}, bulk={record.bodyBulk:F2}, ");
            builder.Append($"height={record.bodyHeight:F2}, mass={record.bodyMassFactor:F2}, ");
            builder.Append($"reach={record.feedingReach:F1}, stamina={record.maxStamina:F1}, ");
            builder.Append($"maturity={record.maturityTime:F1}s, lifespan={record.maxLifespan:F1}s): ");
            builder.Append($"preferredC={record.preferredTemperature:F1}, coldTolerance={record.coldTolerance:F1}, ");
            builder.Append($"heatTolerance={record.heatTolerance:F1}, ");
            builder.Append($"living={living}, births={record.births}, deaths={record.deaths}, ");
            builder.Append($"oldAge={record.oldAgeDeaths}, ");
            builder.Append($"coldExposure={record.coldExposureDeaths}, heatExposure={record.heatExposureDeaths}, ");
            builder.Append($"fight={record.fightResponses}, flee={record.fleeResponses}, ");
            builder.Append($"offspring={record.reproductionEvents}, plants={record.plantMeals} ");
            builder.Append($"({record.digestiblePlantEnergyGained:F1} energy), meat={record.meatMeals} ");
            builder.Append($"({record.digestibleMeatEnergyGained:F1} energy), kills={record.successfulKills}");
        }

        Debug.Log(builder.ToString());
    }
}
