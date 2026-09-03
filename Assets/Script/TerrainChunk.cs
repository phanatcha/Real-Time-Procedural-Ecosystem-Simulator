using UnityEngine;

public class TerrainChunk {
	
	const float colliderGenerationDistanceThreshold = 5;
	public event System.Action<TerrainChunk, bool> onVisibilityChanged;
	public Vector2 coord;
	 
	GameObject meshObject;
	Vector2 sampleCentre;
	Bounds bounds;

	MeshRenderer meshRenderer;
	MeshFilter meshFilter;
	MeshCollider meshCollider;

	LODInfo[] detailLevels;
	LODMesh[] lodMeshes;
	int colliderLODIndex;

	HeightMap heightMap;
	bool heightMapReceived;
	int previousLODIndex = -1;
	bool hasSetCollider;
	float maxViewDst;

	HeightMapSettings heightMapSettings;
	MeshSettings meshSettings;
	Transform viewer;

	VegetationSettings vegetationSettings;
	Material vegetationMaterial;
	float moistureScale;
	Vector2 position;
	bool vegetationRequested;
	VegetationPlacementData vegetationData;
	GameObject vegetationObject;
	MeshFilter treesMeshFilter;
	MeshFilter grassMeshFilter;
	MeshFilter rocksMeshFilter;
	int vegetationLODIndex = -1;

	public TerrainChunk(Vector2 coord, HeightMapSettings heightMapSettings, MeshSettings meshSettings, LODInfo[] detailLevels, int colliderLODIndex, Transform parent, Transform viewer, Material material, VegetationSettings vegetationSettings, Material vegetationMaterial, float moistureScale) {
		this.coord = coord;
		this.detailLevels = detailLevels;
		this.colliderLODIndex = colliderLODIndex;
		this.heightMapSettings = heightMapSettings;
		this.meshSettings = meshSettings;
		this.viewer = viewer;
		this.vegetationSettings = vegetationSettings;
		this.vegetationMaterial = vegetationMaterial;
		this.moistureScale = moistureScale;

		sampleCentre = coord * meshSettings.meshWorldSize / meshSettings.meshScale;
		position = coord * meshSettings.meshWorldSize ;
		bounds = new Bounds(position,Vector2.one * meshSettings.meshWorldSize );


		meshObject = new GameObject("Terrain Chunk");
		meshRenderer = meshObject.AddComponent<MeshRenderer>();
		meshFilter = meshObject.AddComponent<MeshFilter>();
		meshCollider = meshObject.AddComponent<MeshCollider>();
		meshRenderer.material = material;

		meshObject.transform.position = new Vector3(position.x,0,position.y);
		meshObject.transform.parent = parent;
		SetVisible(false);

		lodMeshes = new LODMesh[detailLevels.Length];
		for (int i = 0; i < detailLevels.Length; i++) {
			lodMeshes[i] = new LODMesh(detailLevels[i].lod);
			lodMeshes[i].updateCallback += UpdateTerrainChunk;
			if (i == colliderLODIndex) {
				lodMeshes[i].updateCallback += UpdateCollisionMesh;
			}
		}

		maxViewDst = detailLevels [detailLevels.Length - 1].visibleDstThreshold;

	}

	public void Load() {
		ThreadedDataRequester.RequestData(() => HeightMapGenerator.GenerateHeightMap (meshSettings.numVertsPerLine, meshSettings.numVertsPerLine, heightMapSettings, sampleCentre), OnHeightMapReceived);
	}



	void OnHeightMapReceived(object heightMapObject) {
		this.heightMap = (HeightMap)heightMapObject;
		heightMapReceived = true;

		UpdateTerrainChunk ();
	}

	Vector2 viewerPosition {
		get {
			return new Vector2 (viewer.position.x, viewer.position.z);
		}
	}


	public void UpdateTerrainChunk() {
		if (heightMapReceived) {
			float viewerDstFromNearestEdge = Mathf.Sqrt (bounds.SqrDistance (viewerPosition));

			bool wasVisible = IsVisible ();
			bool visible = viewerDstFromNearestEdge <= maxViewDst;

			if (visible) {
				int lodIndex = 0;

				for (int i = 0; i < detailLevels.Length - 1; i++) {
					if (viewerDstFromNearestEdge > detailLevels [i].visibleDstThreshold) {
						lodIndex = i + 1;
					} else {
						break;
					}
				}

				if (lodIndex != previousLODIndex) {
					LODMesh lodMesh = lodMeshes [lodIndex];
					if (lodMesh.hasMesh) {
						previousLODIndex = lodIndex;
						meshFilter.mesh = lodMesh.mesh;
						UpdateVegetationForLOD(lodIndex);
					} else if (!lodMesh.hasRequestedMesh) {
						lodMesh.RequestMesh (heightMap, meshSettings);
					}
				}


			}

			if (wasVisible != visible) {

				SetVisible (visible);
				if (onVisibilityChanged != null) {
					onVisibilityChanged (this, visible);
				}
			}

			if (visible && !vegetationRequested && vegetationSettings != null && vegetationMaterial != null) {
				vegetationRequested = true;
				RequestVegetation();
			}
		}
	}

	void RequestVegetation() {
		Vector2 halfSize = Vector2.one * (meshSettings.meshWorldSize / 2f);
		Vector2 chunkWorldMin = position - halfSize;
		Vector2 chunkWorldMax = position + halfSize;
		float terrainMinHeight = heightMapSettings.minHeight;
		float terrainMaxHeight = heightMapSettings.maxHeight;

		ThreadedDataRequester.RequestData(() => VegetationGenerator.GeneratePlacements(
			chunkWorldMin, chunkWorldMax, heightMap, terrainMinHeight, terrainMaxHeight,
			vegetationSettings, moistureScale, meshSettings.numVertsPerLine, meshSettings.meshScale), OnVegetationDataReceived);
	}

	void OnVegetationDataReceived(object dataObject) {
		vegetationData = (VegetationPlacementData)dataObject;

		VegetationTemplateCache.EnsureBuilt(vegetationSettings);
		vegetationObject = new GameObject("Vegetation");
		vegetationObject.transform.SetParent(meshObject.transform, false);
		treesMeshFilter = AddVegetationLayer(vegetationObject.transform, "Trees");
		grassMeshFilter = AddVegetationLayer(vegetationObject.transform, "Grass");
		rocksMeshFilter = AddVegetationLayer(vegetationObject.transform, "Rocks");

		if (previousLODIndex >= 0) UpdateVegetationForLOD(previousLODIndex);
	}

	MeshFilter AddVegetationLayer(Transform parent, string layerName) {
		GameObject layer = new GameObject(layerName);
		layer.transform.SetParent(parent, false);
		MeshFilter filter = layer.AddComponent<MeshFilter>();
		layer.AddComponent<MeshRenderer>().sharedMaterial = vegetationMaterial;
		return filter;
	}

	void UpdateVegetationForLOD(int lodIndex) {
		if (vegetationData == null || vegetationObject == null || vegetationLODIndex == lodIndex) return;

		VegetationPlacementData projected = VegetationGenerator.ProjectPlacementsToLOD(
			vegetationData, heightMap, heightMapSettings.minHeight, heightMapSettings.maxHeight,
			vegetationSettings, position, meshSettings.numVertsPerLine, meshSettings.meshScale,
			detailLevels[lodIndex].lod);

		VegetationGenerator.BuildCombinedMeshes(projected, meshObject.transform.position,
			out Mesh treesMesh, out Mesh grassMesh, out Mesh rocksMesh);

		SetVegetationLayer(treesMeshFilter, treesMesh, "Trees (" + projected.trees.Count + ")");
		SetVegetationLayer(grassMeshFilter, grassMesh, "Grass (" + projected.grass.Count + ")");
		SetVegetationLayer(rocksMeshFilter, rocksMesh, "Rocks (" + projected.rocks.Count + ")");
		vegetationLODIndex = lodIndex;
	}

	void SetVegetationLayer(MeshFilter filter, Mesh mesh, string layerName) {
		Mesh previousMesh = filter.sharedMesh;
		filter.sharedMesh = mesh;
		filter.gameObject.name = layerName;
		filter.gameObject.SetActive(mesh != null);
		if (previousMesh != null) Object.Destroy(previousMesh);
	}

	public void UpdateCollisionMesh() {
		if (!hasSetCollider) {
			float sqrDstFromViewerToEdge = bounds.SqrDistance (viewerPosition);

			if (sqrDstFromViewerToEdge < detailLevels [colliderLODIndex].sqrVisibleDstThreshold) {
				if (!lodMeshes [colliderLODIndex].hasRequestedMesh) {
					lodMeshes [colliderLODIndex].RequestMesh (heightMap, meshSettings);
				}
			}

			if (sqrDstFromViewerToEdge < colliderGenerationDistanceThreshold * colliderGenerationDistanceThreshold) {
				if (lodMeshes [colliderLODIndex].hasMesh) {
					meshCollider.sharedMesh = lodMeshes [colliderLODIndex].mesh;
					hasSetCollider = true;
				}
			}
		}
	}

	public void SetVisible(bool visible) {
		meshObject.SetActive (visible);
	}

	public bool IsVisible() {
		return meshObject.activeSelf;
	}

}

class LODMesh {

	public Mesh mesh;
	public bool hasRequestedMesh;
	public bool hasMesh;
	int lod;
	public event System.Action updateCallback;

	public LODMesh(int lod) {
		this.lod = lod;
	}

	void OnMeshDataReceived(object meshDataObject) {
		mesh = ((MeshData)meshDataObject).CreateMesh ();
		hasMesh = true;

		updateCallback ();
	}

	public void RequestMesh(HeightMap heightMap, MeshSettings meshSettings) {
		hasRequestedMesh = true;
		ThreadedDataRequester.RequestData (() => MeshGenerator.GenerateTerrainMesh (heightMap.values, meshSettings, lod), OnMeshDataReceived);
	}

}
