using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class TerrainGenerator : MonoBehaviour {

	const float viewerMoveThresholdForChunkUpdate = 25f;
	const float sqrViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;


	public int colliderLODIndex;
	public LODInfo[] detailLevels;

	public MeshSettings meshSettings;
	public HeightMapSettings heightMapSettings;
	public TextureData textureSettings;
	public VegetationSettings vegetationSettings;

	public Transform viewer;
	public Material mapMaterial;
	public Material vegetationMaterial;

	Vector2 viewerPosition;
	Vector2 viewerPositionOld;

	float meshWorldSize;
	int chunksVisibleInViewDst;

    private bool terrainGenerated = false;

    Dictionary<Vector2Int, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2Int, TerrainChunk>();
	List<TerrainChunk> visibleTerrainChunks = new List<TerrainChunk>();
	TerrainEnvironmentSampler worldEnvironmentSampler;


    private HeightMapSettings originalHeightMapSettings;

    public EnvironmentDefinitions EnvironmentDefinitions {
		get {
			return textureSettings == null ? null : textureSettings.environmentDefinitions;
		}
	}

	public TerrainEnvironmentSampler WorldEnvironmentSampler {
		get {
			EnsureEnvironmentSampler();
			return worldEnvironmentSampler;
		}
	}


    private void Awake()
    {
        originalHeightMapSettings = heightMapSettings;
    }
    void Start() {
		//EnsureEnvironmentSampler();

		//textureSettings.ApplyToMaterial (mapMaterial);
		//textureSettings.UpdateMeshHeights (mapMaterial, heightMapSettings.minHeight, heightMapSettings.maxHeight);

		//float maxViewDst = detailLevels [detailLevels.Length - 1].visibleDstThreshold;
		//meshWorldSize = meshSettings.meshWorldSize;
		//chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / meshWorldSize);

		//UpdateVisibleChunks ();
	}

    void Update()
    {
        if (!terrainGenerated)
            return;

        viewerPosition = new Vector2(viewer.position.x, viewer.position.z);

        if (viewerPosition != viewerPositionOld)
        {
            foreach (TerrainChunk chunk in visibleTerrainChunks)
            {
                chunk.UpdateCollisionMesh();
            }
        }

        if ((viewerPositionOld - viewerPosition).sqrMagnitude >
            sqrViewerMoveThresholdForChunkUpdate)
        {
            viewerPositionOld = viewerPosition;
            UpdateVisibleChunks();
        }
    }

    void UpdateVisibleChunks() {
		HashSet<Vector2Int> alreadyUpdatedChunkCoords = new HashSet<Vector2Int> ();
		for (int i = visibleTerrainChunks.Count-1; i >= 0; i--) {
			alreadyUpdatedChunkCoords.Add (visibleTerrainChunks [i].coord);
			visibleTerrainChunks [i].UpdateTerrainChunk ();
		}

		Vector2Int currentChunkCoordinate = TerrainGrid.WorldToChunkCoordinate(viewerPosition, meshWorldSize);

		for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++) {
			for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++) {
				Vector2Int viewedChunkCoord = new Vector2Int(currentChunkCoordinate.x + xOffset, currentChunkCoordinate.y + yOffset);
				if (!alreadyUpdatedChunkCoords.Contains (viewedChunkCoord)) {
					if (terrainChunkDictionary.ContainsKey (viewedChunkCoord)) {
						terrainChunkDictionary [viewedChunkCoord].UpdateTerrainChunk ();
					} else {
						TerrainChunk newChunk = new TerrainChunk (viewedChunkCoord,heightMapSettings,meshSettings, detailLevels, colliderLODIndex, transform, viewer, mapMaterial, vegetationSettings, vegetationMaterial, EnvironmentDefinitions, WorldEnvironmentSampler);
						terrainChunkDictionary.Add (viewedChunkCoord, newChunk);
						newChunk.onVisibilityChanged += OnTerrainChunkVisibilityChanged;
						newChunk.Load ();
					}
				}

			}
		}
	}

	void OnTerrainChunkVisibilityChanged(TerrainChunk chunk, bool isVisible) {
		if (isVisible) {
			visibleTerrainChunks.Add (chunk);
		} else {
			visibleTerrainChunks.Remove (chunk);
		}
	}

	public bool TryGetEnvironmentSample(Vector3 worldPosition, out EnvironmentSample sample) {
		return WorldEnvironmentSampler.TrySample(worldPosition, out sample);
	}

	public bool TryGetEnvironmentSample(Vector2 worldPosition, out EnvironmentSample sample) {
		return WorldEnvironmentSampler.TrySample(worldPosition, out sample);
	}

	void EnsureEnvironmentSampler() {
		EnvironmentDefinitions definitions = EnvironmentDefinitions;
		if (worldEnvironmentSampler != null
			&& worldEnvironmentSampler.HeightMapSettings == heightMapSettings
			&& worldEnvironmentSampler.MeshSettings == meshSettings
			&& worldEnvironmentSampler.EnvironmentDefinitions == definitions
			&& worldEnvironmentSampler.VegetationSettings == vegetationSettings) {
			return;
		}

		worldEnvironmentSampler = new TerrainEnvironmentSampler(
			heightMapSettings, meshSettings, definitions, vegetationSettings);
	}

    public void GenerateTerrain(int terrainSeed)
    {
        Debug.Log($"Generating terrain with seed: {terrainSeed}");

		ClearTerrain();

        // Make runtime copy so we don't modify the original asset
        heightMapSettings = Instantiate(originalHeightMapSettings);

        // Apply our SeedManager terrain seed
        heightMapSettings.noiseSettings.seed = terrainSeed;

        EnsureEnvironmentSampler();

        textureSettings.ApplyToMaterial(mapMaterial);

        textureSettings.UpdateMeshHeights(
            mapMaterial,
            heightMapSettings.minHeight,
            heightMapSettings.maxHeight
        );

        float maxViewDst =
            detailLevels[detailLevels.Length - 1].visibleDstThreshold;

        meshWorldSize = meshSettings.meshWorldSize;

        chunksVisibleInViewDst =
            Mathf.RoundToInt(maxViewDst / meshWorldSize);

        viewerPosition =
            new Vector2(viewer.position.x, viewer.position.z);

        viewerPositionOld = viewerPosition;

        terrainGenerated = true;

        UpdateVisibleChunks();
    }


    private void ClearTerrain()
    {
        // Delete generated chunk GameObjects
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        terrainChunkDictionary.Clear();
        visibleTerrainChunks.Clear();

        worldEnvironmentSampler = null;

        terrainGenerated = false;
    }

}

[System.Serializable]
public struct LODInfo {
	[Range(0,MeshSettings.numSupportedLODs-1)]
	public int lod;
	public float visibleDstThreshold;


	public float sqrVisibleDstThreshold {
		get {
			return visibleDstThreshold * visibleDstThreshold;
		}
	}
}
