using UnityEngine;
using UnityEngine.Rendering;

// Draws ground plants as Norway's summer forest foods, in the same painterly style as the island's trees. Only the
// look depends on where a plant grows; the food is the same plant food whatever it is drawn as. Autumn foods
// (lingonberries, porcini, juniper berries) and spring ones (wild garlic, spruce tips) join when seasons arrive.
public static class ForestFoodLooks
{
    const int Variants = 3;
    static readonly Mesh[] meshes = new Mesh[System.Enum.GetValues(typeof(ForestFood)).Length * Variants];
    static VegetationSettings meshesBuiltFor;

    // Which food a plant is drawn as, from where it grows: cloudberries on wet open ground (bogs), raspberry bushes
    // and wild strawberries in sunny clearings, wood sorrel in dark spruce groves, chanterelles on damp forest floor,
    // and bilberry bushes, which carpet most of the forest, everywhere else. roll is a 0-1 number fixed for the spot.
    public static ForestFood Choose(float moisture, float treeCover, float roll)
    {
        if (moisture > 0.72f && treeCover < 0.5f && roll < 0.7f) return ForestFood.Cloudberry;
        if (treeCover < 0.25f && roll < 0.45f) return ForestFood.Raspberry;
        if (treeCover < 0.25f && roll < 0.75f) return ForestFood.WildStrawberry;
        if (treeCover > 0.65f && roll < 0.35f) return ForestFood.WoodSorrel;
        if (treeCover > 0.4f && moisture > 0.5f && roll < 0.55f) return ForestFood.Chanterelle;
        return ForestFood.Bilberry;
    }

    // Hides the food's box and stands the plant on the ground below it, sized by the plant's size multiplier. Berry
    // bushes take the island's bush colours from the world's vegetation settings.
    public static void Dress(GameObject food, ForestFood kind, float heightAboveGround, float size, Material material,
                             VegetationSettings vegetation)
    {
        if (material == null || vegetation == null || !food.TryGetComponent(out MeshRenderer foodRenderer)) return;
        foodRenderer.enabled = false;

        // The food box is scaled; this unrotated holder undoes that, so the plant is in metres.
        Vector3 foodScale = food.transform.lossyScale;
        Transform holder = new GameObject("Look").transform;
        holder.gameObject.layer = food.layer;
        holder.SetParent(food.transform, false);
        holder.localScale = new Vector3(1f / foodScale.x, 1f / foodScale.y, 1f / foodScale.z);

        Vector3 position = food.transform.position;
        int hash = (Mathf.FloorToInt(position.x * 5.3f) * 73856093) ^ (Mathf.FloorToInt(position.z * 5.3f) * 19349663);
        hash &= int.MaxValue;

        GameObject plant = new GameObject(kind.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
        plant.layer = food.layer;
        plant.transform.SetParent(holder, false);
        plant.transform.localPosition = Vector3.down * heightAboveGround;
        plant.transform.localRotation = Quaternion.Euler(0f, hash % 360, 0f);
        plant.transform.localScale = Vector3.one * size;
        plant.GetComponent<MeshFilter>().sharedMesh = SharedMesh(kind, (hash >> 9) % Variants, vegetation);

        MeshRenderer renderer = plant.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        // Berry bushes cast shadows like the island's other bushes, so edible ones look the same. The low plants are
        // small and very many: their shadows would cost more than they show.
        bool bush = kind == ForestFood.Bilberry || kind == ForestFood.Raspberry;
        renderer.shadowCastingMode = bush ? ShadowCastingMode.On : ShadowCastingMode.Off;
    }

    static Mesh SharedMesh(ForestFood kind, int variant, VegetationSettings vegetation)
    {
        // A new world brings new vegetation settings; build the meshes again in its colours.
        if (meshesBuiltFor != vegetation)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] != null) Object.Destroy(meshes[i]);
                meshes[i] = null;
            }

            meshesBuiltFor = vegetation;
        }

        int index = (int)kind * Variants + variant;
        if (meshes[index] == null)
        {
            meshes[index] = VegetationMeshBuilder.BuildForestFood(kind, vegetation, new System.Random(index * 7919 + 13));
            meshes[index].name = $"{kind} {variant}";
        }

        return meshes[index];
    }
}
