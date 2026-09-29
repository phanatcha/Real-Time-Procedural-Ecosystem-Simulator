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



    [Header("UI")]
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




    // Only the terrain is generated here. The ecosystem (navigation, plant food and founder animals)
    // rebuilds itself whenever TerrainGenerator reports new terrain.
    private void GenerateWorld()
    {
        Debug.Log($"Generating world: {seedString}");

        if (terrainGenerator != null)
        {
            int terrainSeed = GetSeed("Terrain");

            Debug.Log($"Terrain Seed: {terrainSeed}");

            terrainGenerator.GenerateTerrain(terrainSeed);
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