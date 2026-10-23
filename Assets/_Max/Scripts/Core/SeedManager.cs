using UnityEngine;
using TMPro;

// Runs before AnimalTerrainDemoBootstrap, so the world is generated from the seed string before the
// ecosystem would otherwise generate a default one.
[DefaultExecutionOrder(-1100)]
public class SeedManager : MonoBehaviour
{
    public static SeedManager Instance { get; private set; }



    [Header("Seed")]
    [SerializeField]
    private string seedString = "FOREST-001";

    public int Seed { get; private set; }

    public string SeedString => seedString;

    // Raised whenever the seed changes, so the seed panel can show it.
    public event System.Action SeedChanged;



    [Header("UI")]
    [Tooltip("Show the collapsible seed panel in the top-right corner instead of the older seed box assigned below.")]
    [SerializeField]
    private bool useSeedPanel = true;

    [SerializeField]
    private TMP_Text seedText;

    [SerializeField]
    private TMP_InputField seedInputField;



    [Header("Generators")]
    [SerializeField]
    private TerrainGenerator terrainGenerator;

    [Tooltip("Generate the world from the seed string as soon as the scene starts.")]
    [SerializeField]
    private bool generateOnStart = true;


    private void Awake()
    {
     
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

     
        DontDestroyOnLoad(gameObject);

        GenerateSeed();

        if (useSeedPanel)
        {
            ShowSeedPanel();
        }
    }



    // The older seed box (seed text, input field and Generate button) is only hidden, so turning
    // useSeedPanel off brings it back.
    private void ShowSeedPanel()
    {
        WorldSeedPanel.Create(this, seedText != null ? seedText.font : null);

        Transform oldSeedBox = seedInputField != null ? seedInputField.transform.parent
            : seedText != null ? seedText.transform.parent : null;

        if (oldSeedBox != null && oldSeedBox.GetComponent<Canvas>() == null)
        {
            oldSeedBox.gameObject.SetActive(false);
        }
    }


    private void Start()
    {
        if (generateOnStart)
        {
            GenerateWorld();
        }
    }


    private void GenerateSeed()
    {
        Seed = StringToSeed(seedString);

        UpdateUI();

        SeedChanged?.Invoke();

        Debug.Log($"Seed String: {seedString}");
        Debug.Log($"World Seed: {Seed}");
    }



    public void GenerateFromInput()
    {
        if (seedInputField == null)
        {
            Debug.LogError("Seed Input Field is not assigned!");
            return;
        }

        string input = seedInputField.text.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            Debug.LogWarning("Please enter a seed.");
            return;
        }

        SetSeed(input);

        GenerateWorld();
    }




    public void SetSeed(string newSeed)
    {
        if (string.IsNullOrWhiteSpace(newSeed))
        {
            Debug.LogWarning("Seed cannot be empty.");
            return;
        }

        seedString = newSeed.Trim();

        GenerateSeed();
    }




    // Switches to the given seed, or keeps the current one when it is empty, and rebuilds the world.
    public void GenerateWorld(string newSeed)
    {
        if (!string.IsNullOrWhiteSpace(newSeed))
        {
            SetSeed(newSeed);
        }

        GenerateWorld();
    }




    // Only the terrain is generated here. The ecosystem (navigation, plant food and founder animals)
    // rebuilds itself whenever TerrainGenerator reports new terrain.
    private void GenerateWorld()
    {
        Debug.Log($"Generating world: {seedString}");

        if (terrainGenerator != null)
        {
            WorldSeeds seeds = GetWorldSeeds(seedString);

            Debug.Log($"World seeds: {seeds}");

            terrainGenerator.GenerateTerrain(seeds);
        }
        else
        {
            Debug.LogError("TerrainGenerator is not assigned!");
        }
    }



    public int GetSeed(string systemName)
    {
        return GetSeed(seedString, systemName);
    }



    // The seed a system gets from a seed string, e.g. GetSeed("FOREST-001", "Terrain"). Editor tools use
    // it to rebuild the same world without a SeedManager in the scene.
    public static int GetSeed(string seedString, string systemName)
    {
        return StringToSeed(seedString + "_" + systemName);
    }



    // The seed of every part of the world for a seed string. Editor tools use it to rebuild the same world.
    public static WorldSeeds GetWorldSeeds(string seedString)
    {
        return new WorldSeeds
        {
            terrain = GetSeed(seedString, "Terrain"),
            ridges = GetSeed(seedString, "Ridges"),
            rivers = GetSeed(seedString, "Rivers"),
            lakes = GetSeed(seedString, "Lakes"),
            vegetation = GetSeed(seedString, "Vegetation"),
            moisture = GetSeed(seedString, "Moisture"),
            temperature = GetSeed(seedString, "Temperature"),
            resourcePatches = GetSeed(seedString, "ResourcePatches")
        };
    }



    private static int StringToSeed(string input)
    {
        unchecked
        {
            int hash = 23;

            foreach (char character in input)
            {
                hash = hash * 31 + character;
            }

            return hash;
        }
    }


 

    private void UpdateUI()
    {
        if (seedText == null)
            return;

        seedText.text =
            $"<b>WORLD SEED</b>\n\n" +
            $"String: {seedString}\n" +
            $"Numeric: {Seed}\n\n" +

            $"<b>SYSTEM SEEDS</b>\n\n" +
            $"Terrain and plants: {GetSeed("Terrain")}";
    }
}