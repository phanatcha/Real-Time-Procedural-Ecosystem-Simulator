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

// Animals kept as numbers away from the camera (see OffscreenPopulationBridge). They keep their species
// alive, so a species with no animals on screen is not extinct.
public interface IOffscreenPopulation
{
    int TotalOffscreen { get; }
    int CountOffscreen(string speciesName);
    IEnumerable<string> OffscreenSpecies { get; }
}

// One species' history. Trait values are the living members' averages, refreshed at every species
// census; after extinction they keep the last averages.
[System.Serializable]
public sealed class SpeciesTelemetryRecord
{
    public string speciesName;
    public string parentSpeciesName;
    public Color color;
    [Tooltip("Simulated seconds since the simulation started.")]
    public float originTime;
    [Tooltip("Simulated seconds since the simulation started, or -1 while the species survives.")]
    public float extinctionTime = -1f;
    [Tooltip("Lowest (x) and highest (y) values among the living members at the last census.")]
    public Vector2 dietRange;
    public Vector2 bodyBulkRange;
    public Vector2 bodyHeightRange;
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
    public float sexualDrive;
    [Tooltip("Average number of harmful mutations carried, each adding to energy use.")]
    public float harmfulMutations;
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
    [Tooltip("Births with two parents. The rest are clones.")]
    public int sexualOffspring;
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
    [Tooltip("Enables individual birth and death messages. Leave off for large or fast simulations. " +
             "Speciation and extinction are always logged because they are rare.")]
    public bool logLifecycleEvents;
    public bool logTelemetryToConsole;
    [Min(1f)] public float telemetryLogInterval = 30f;

    [Header("Speciation")]
    [Tooltip("Genetic distance at which animals count as separate species: 0 means identical and 1 means " +
             "opposite ends of every gene's range. Lower values produce more species.")]
    [Range(0.005f, 0.5f)] public float speciationThreshold = 0.03f;
    [Tooltip("Simulated seconds between checks for species that have drifted into separate groups.")]
    [Min(1f)] public float speciesCensusInterval = 10f;
    [Tooltip("A separated group becomes its own species once it has at least this many members.")]
    [Min(1)] public int minimumNewSpeciesSize = 3;

    [Header("Population Ceiling")]
    [Tooltip("Animals stop giving birth while the population is this large, so an open world stays " +
             "affordable to simulate. Set to 0 to disable. With a sea ceiling it counts animals on land only.")]
    [Min(0)] public int populationCeiling = 400;
    [Tooltip("A separate ceiling for animals in the water, so a crowded sea cannot stop births on land, or the " +
             "other way round. Animals at sea are never kept as off-screen numbers, so without this they could " +
             "fill the whole ceiling. 0 counts animals in water toward the ceiling above, as before.")]
    [Min(0)] public int seaPopulationCeiling = 200;

    private int currentSpeciesIndex;
    private float telemetryTimer;
    private float censusTimer;
    private bool isShuttingDown;
    // Newborns register on their first frame, so births are counted here until then. Otherwise every
    // parent in a busy frame sees room under the ceiling and the population overshoots it.
    private int pendingBirths;
    private int seaPopulation;
    private int seaPopulationFrame = -1;
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
    // Animals on screen. Off-screen animals are in OffscreenPopulation.
    public int TotalPopulation { get; private set; }
    public IOffscreenPopulation OffscreenPopulation { get; set; }
    public int OffscreenTotal => OffscreenPopulation == null ? 0 : OffscreenPopulation.TotalOffscreen;

    // Species with animals on screen or off it.
    public int SpeciesAliveCount
    {
        get
        {
            if (OffscreenPopulation == null) return speciesPopulation.Count;

            int count = speciesPopulation.Count;
            foreach (string speciesName in OffscreenPopulation.OffscreenSpecies)
            {
                if (!speciesPopulation.ContainsKey(speciesName)) count++;
            }

            return count;
        }
    }
    // Animals in the water right now, counted at most once a frame.
    public int SeaPopulation
    {
        get
        {
            if (seaPopulationFrame != Time.frameCount)
            {
                seaPopulationFrame = Time.frameCount;
                seaPopulation = 0;
                foreach (SeekFood agent in activeAgents)
                {
                    if (agent != null && agent.IsInWater) seaPopulation++;
                }
            }

            return seaPopulation;
        }
    }
    // The animals populationCeiling counts: those on land while the sea has its own ceiling, otherwise all.
    public int LandPopulation => seaPopulationCeiling > 0 ? TotalPopulation - SeaPopulation : TotalPopulation;
    public bool IsAtPopulationCeiling =>
        populationCeiling > 0 && LandPopulation + pendingBirths >= populationCeiling;
    public bool IsAtSeaPopulationCeiling => seaPopulationCeiling > 0
        ? SeaPopulation + pendingBirths >= seaPopulationCeiling
        : IsAtPopulationCeiling;

    // Whether a parent here must wait to give birth: one in the water answers to the sea's ceiling, others to the
    // land's.
    public bool IsAtPopulationCeilingFor(bool inWater) => inWater ? IsAtSeaPopulationCeiling : IsAtPopulationCeiling;
    // Simulated time (Time.time) when the current world's history began.
    public float WorldStartTime { get; private set; }
    // Refreshed at every species census. Founders are generation 0.
    public float AverageGeneration { get; private set; }
    public int HighestGeneration { get; private set; }
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

        censusTimer += Time.deltaTime;
        if (censusTimer >= Mathf.Max(1f, speciesCensusInterval))
        {
            censusTimer = 0f;
            RunSpeciesCensus();
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

    // Leaving Play mode destroys every animal; those are not deaths or extinctions.
    void OnApplicationQuit()
    {
        isShuttingDown = true;
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

    // Forgets every animal and species so a newly generated world starts a fresh history. Animals
    // destroyed afterwards are no longer registered, so they are not recorded as deaths.
    public void ResetSimulation()
    {
        activeAgents.Clear();
        speciesPopulation.Clear();
        speciesColors.Clear();
        assignedSpeciesNames.Clear();
        telemetry.Clear();
        currentSpeciesIndex = 0;
        TotalPopulation = 0;
        pendingBirths = 0;
        telemetryTimer = 0f;
        censusTimer = 0f;
        WorldStartTime = Time.time;
        AverageGeneration = 0f;
        HighestGeneration = 0;
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

    // Daughter species get a colour close to their parent's, so related species look alike while
    // sister species stay distinguishable.
    public static Color GenerateDaughterColor(Color parentColor)
    {
        Color.RGBToHSV(parentColor, out float hue, out float saturation, out float brightness);
        float hueShift = Random.Range(0.06f, 0.14f) * (Random.value < 0.5f ? -1f : 1f);
        return Color.HSVToRGB(
            Mathf.Repeat(hue + hueShift, 1f),
            Mathf.Clamp(saturation + Random.Range(-0.15f, 0.15f), 0.35f, 1f),
            Mathf.Clamp(brightness + Random.Range(-0.15f, 0.15f), 0.45f, 1f));
    }

    // countAsBirth is false for animals arriving from an off-screen population, which were counted already.
    public void RegisterAgent(SeekFood agent, bool countAsBirth = true)
    {
        if (agent == null || !activeAgents.Add(agent))
        {
            return;
        }

        if (pendingBirths > 0) pendingBirths--;
        string speciesName = agent.speciesName;
        Color color = agent.speciesColor;
        assignedSpeciesNames.Add(speciesName);

        if (!speciesPopulation.ContainsKey(speciesName))
        {
            speciesPopulation[speciesName] = 0;
            speciesColors[speciesName] = color;
        }
        bool isFirstLivingMember = speciesPopulation[speciesName] == 0;
        speciesPopulation[speciesName]++;
        TotalPopulation++;
        SpeciesTelemetryRecord telemetryRecord = GetOrCreateTelemetry(speciesName);
        telemetryRecord.extinctionTime = -1f;

        // Until the next census, a brand-new species' traits are its first member's.
        if (isFirstLivingMember)
        {
            BlendTraits(telemetryRecord, agent, 1f);
            WidenTraitRanges(telemetryRecord, agent, true);
        }
        if (countAsBirth) telemetryRecord.births++;
    }

    public void DeregisterAgent(SeekFood agent, AgentDeathCause deathCause)
    {
        if (agent == null || !activeAgents.Remove(agent) || isShuttingDown)
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
                bool livesOffscreen = OffscreenPopulation != null && OffscreenPopulation.CountOffscreen(speciesName) > 0;
                if (!livesOffscreen)
                {
                    speciesColors.Remove(speciesName);
                    MarkExtinct(record);
                }
            }
        }
    }

    // Called when a species' last off-screen animals die out while none of it is on screen.
    public void RecordOffscreenExtinction(string speciesName)
    {
        if (speciesPopulation.ContainsKey(speciesName) || !telemetry.TryGetValue(speciesName, out SpeciesTelemetryRecord record))
        {
            return;
        }

        speciesColors.Remove(speciesName);
        MarkExtinct(record);
    }

    void MarkExtinct(SpeciesTelemetryRecord record)
    {
        if (record.extinctionTime >= 0f) return;

        record.extinctionTime = Time.time;
        Debug.Log($"Extinction: species {record.speciesName} died out at {Time.time:F0} simulated seconds.");
    }

    public void UnregisterAgentWithoutDeath(SeekFood agent)
    {
        if (agent == null || !activeAgents.Remove(agent)) return;

        string speciesName = agent.speciesName;
        if (speciesPopulation.TryGetValue(speciesName, out int population))
        {
            population--;
            TotalPopulation = Mathf.Max(0, TotalPopulation - 1);
            if (population > 0)
            {
                speciesPopulation[speciesName] = population;
            }
            else
            {
                speciesPopulation.Remove(speciesName);
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

    // sexual is true for a child of two parents, counted once, for the parent it was born beside.
    public void RecordReproduction(SeekFood parent, bool offspringMutated, bool sexual = false)
    {
        if (parent == null)
        {
            return;
        }

        pendingBirths++;
        SpeciesTelemetryRecord record = GetOrCreateTelemetry(parent.speciesName);
        record.reproductionEvents++;
        if (offspringMutated)
        {
            record.mutatedOffspring++;
        }

        if (sexual)
        {
            record.sexualOffspring++;
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
            record = new SpeciesTelemetryRecord
            {
                speciesName = speciesName,
                originTime = Time.time,
                color = speciesColors.TryGetValue(speciesName, out Color color) ? color : Color.white
            };
            telemetry.Add(speciesName, record);
        }

        return record;
    }

    // Splits species whose members have drifted into genetically separate groups, then refreshes every
    // species' average traits.
    void RunSpeciesCensus()
    {
        Dictionary<string, List<SeekFood>> membersBySpecies = new Dictionary<string, List<SeekFood>>();
        foreach (SeekFood agent in activeAgents)
        {
            if (agent == null || !agent.IsAlive || agent.Genome == null || !agent.Genome.IsValid)
            {
                continue;
            }

            if (!membersBySpecies.TryGetValue(agent.speciesName, out List<SeekFood> members))
            {
                members = new List<SeekFood>();
                membersBySpecies.Add(agent.speciesName, members);
            }

            members.Add(agent);
        }

        foreach (KeyValuePair<string, List<SeekFood>> species in membersBySpecies)
        {
            SplitSeparatedGroups(species.Key, species.Value);
        }

        RefreshAverageTraits();
    }

    // A group splits off when none of its members is within the speciation threshold of anyone outside
    // it. The largest group keeps the species name; each other large-enough group becomes a daughter
    // species. Smaller groups stay put until they grow.
    void SplitSeparatedGroups(string speciesName, List<SeekFood> members)
    {
        int minimumSize = Mathf.Max(1, minimumNewSpeciesSize);
        if (members.Count < minimumSize * 2)
        {
            return;
        }

        List<AnimalGenome> genomes = new List<AnimalGenome>(members.Count);
        foreach (SeekFood member in members)
        {
            genomes.Add(member.Genome);
        }

        List<List<int>> groups = AnimalGenome.GroupByDistance(genomes, speciationThreshold);
        for (int groupIndex = 1; groupIndex < groups.Count && groups[groupIndex].Count >= minimumSize; groupIndex++)
        {
            List<int> group = groups[groupIndex];
            string daughterName = GetNextSpeciesName();
            Color parentColor = speciesColors.TryGetValue(speciesName, out Color color) ? color : members[0].speciesColor;
            Color daughterColor = GenerateDaughterColor(parentColor);

            speciesColors[daughterName] = daughterColor;
            speciesPopulation[daughterName] = group.Count;
            speciesPopulation[speciesName] -= group.Count;
            GetOrCreateTelemetry(daughterName).parentSpeciesName = speciesName;

            foreach (int memberIndex in group)
            {
                members[memberIndex].AssignSpecies(daughterName, daughterColor);
            }

            Debug.Log($"Speciation: {daughterName} ({group.Count} animals) split from {speciesName} " +
                      $"at {Time.time:F0} simulated seconds.");
        }
    }

    void RefreshAverageTraits()
    {
        Dictionary<string, int> sampleCounts = new Dictionary<string, int>();
        long generationTotal = 0;
        int livingCount = 0;
        int highestGeneration = 0;
        foreach (SeekFood agent in activeAgents)
        {
            if (agent == null || !agent.IsAlive)
            {
                continue;
            }

            sampleCounts.TryGetValue(agent.speciesName, out int count);
            count++;
            sampleCounts[agent.speciesName] = count;
            SpeciesTelemetryRecord record = GetOrCreateTelemetry(agent.speciesName);
            BlendTraits(record, agent, 1f / count);
            WidenTraitRanges(record, agent, count == 1);

            generationTotal += agent.Generation;
            livingCount++;
            highestGeneration = Mathf.Max(highestGeneration, agent.Generation);
        }

        AverageGeneration = livingCount > 0 ? (float)generationTotal / livingCount : 0f;
        HighestGeneration = highestGeneration;
    }

    // Variation among members shows evolution long before the averages move or a species splits.
    static void WidenTraitRanges(SpeciesTelemetryRecord record, SeekFood agent, bool isFirstMember)
    {
        if (isFirstMember)
        {
            record.dietRange = new Vector2(agent.dietAffinity, agent.dietAffinity);
            record.bodyBulkRange = new Vector2(agent.bodyBulk, agent.bodyBulk);
            record.bodyHeightRange = new Vector2(agent.bodyHeight, agent.bodyHeight);
            return;
        }

        record.dietRange = Widen(record.dietRange, agent.dietAffinity);
        record.bodyBulkRange = Widen(record.bodyBulkRange, agent.bodyBulk);
        record.bodyHeightRange = Widen(record.bodyHeightRange, agent.bodyHeight);
    }

    static Vector2 Widen(Vector2 range, float value)
    {
        return new Vector2(Mathf.Min(range.x, value), Mathf.Max(range.y, value));
    }

    // Moves the record's traits towards the agent's. A weight of 1 copies the agent, and blending the
    // n-th agent with weight 1/n leaves the record at the average of all n.
    static void BlendTraits(SpeciesTelemetryRecord record, SeekFood agent, float weight)
    {
        record.dietAffinity = Mathf.Lerp(record.dietAffinity, agent.dietAffinity, weight);
        record.dietClassification = SeekFood.ClassifyDiet(record.dietAffinity);
        record.strength = Mathf.Lerp(record.strength, agent.strength, weight);
        record.bodyBulk = Mathf.Lerp(record.bodyBulk, agent.bodyBulk, weight);
        record.bodyHeight = Mathf.Lerp(record.bodyHeight, agent.bodyHeight, weight);
        record.bodyMassFactor = Mathf.Lerp(record.bodyMassFactor, agent.BodyMassFactor, weight);
        record.feedingReach = Mathf.Lerp(record.feedingReach, agent.FeedingReach, weight);
        record.maxStamina = Mathf.Lerp(record.maxStamina, agent.maxStamina, weight);
        record.maturityTime = Mathf.Lerp(record.maturityTime, agent.maturityTime, weight);
        record.maxLifespan = Mathf.Lerp(record.maxLifespan, agent.maxLifespan, weight);
        record.sexualDrive = Mathf.Lerp(record.sexualDrive, agent.sexualDrive, weight);
        record.harmfulMutations = Mathf.Lerp(record.harmfulMutations, agent.HarmfulMutations, weight);
        if (agent.ThermalResponse != null)
        {
            record.preferredTemperature = Mathf.Lerp(record.preferredTemperature,
                                                     agent.ThermalResponse.preferredTemperature, weight);
            record.coldTolerance = Mathf.Lerp(record.coldTolerance, agent.ThermalResponse.coldTolerance, weight);
            record.heatTolerance = Mathf.Lerp(record.heatTolerance, agent.ThermalResponse.heatTolerance, weight);
        }
    }

    [ContextMenu("Log Telemetry Snapshot")]
    public void LogTelemetrySnapshot()
    {
        StringBuilder builder = new StringBuilder("Ecosystem telemetry");
        foreach (SpeciesTelemetryRecord record in telemetry.Values)
        {
            speciesPopulation.TryGetValue(record.speciesName, out int living);
            string lineage = string.IsNullOrEmpty(record.parentSpeciesName)
                ? "founder"
                : $"from {record.parentSpeciesName}";
            string lifetime = record.extinctionTime >= 0f
                ? $"{record.originTime:F0}-{record.extinctionTime:F0}s, extinct"
                : $"since {record.originTime:F0}s";
            builder.Append($"\n{record.speciesName} [{lineage}, {lifetime}] ");
            builder.Append($"({record.dietClassification}, {record.dietAffinity:F2}, ");
            builder.Append($"strength={record.strength:F1}, bulk={record.bodyBulk:F2}, ");
            builder.Append($"height={record.bodyHeight:F2}, mass={record.bodyMassFactor:F2}, ");
            builder.Append($"reach={record.feedingReach:F1}, stamina={record.maxStamina:F1}, ");
            builder.Append($"maturity={record.maturityTime:F1}s, lifespan={record.maxLifespan:F1}s): ");
            builder.Append($"preferredC={record.preferredTemperature:F1}, coldTolerance={record.coldTolerance:F1}, ");
            builder.Append($"heatTolerance={record.heatTolerance:F1}, sexualDrive={record.sexualDrive:F2}, ");
            builder.Append($"harmfulMutations={record.harmfulMutations:F1}, ");
            builder.Append($"living={living}, births={record.births}, deaths={record.deaths}, ");
            builder.Append($"oldAge={record.oldAgeDeaths}, ");
            builder.Append($"coldExposure={record.coldExposureDeaths}, heatExposure={record.heatExposureDeaths}, ");
            builder.Append($"fight={record.fightResponses}, flee={record.fleeResponses}, ");
            builder.Append($"offspring={record.reproductionEvents} ({record.sexualOffspring} with two parents), ");
            builder.Append($"plants={record.plantMeals} ");
            builder.Append($"({record.digestiblePlantEnergyGained:F1} energy), meat={record.meatMeals} ");
            builder.Append($"({record.digestibleMeatEnergyGained:F1} energy), kills={record.successfulKills}");
        }

        Debug.Log(builder.ToString());
    }
}
