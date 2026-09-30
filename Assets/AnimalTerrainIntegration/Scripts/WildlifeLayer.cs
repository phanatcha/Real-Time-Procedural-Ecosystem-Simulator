using UnityEngine;

// The layer animals, plant food and carcasses are drawn on. AnimalTerrainDemoBootstrap tells the camera to
// stop drawing this layer where the terrain stops, so nothing floats over ground that is not drawn.
// Objects stay on their current layer if the project has no Wildlife layer.
public static class WildlifeLayer
{
    public const string Name = "Wildlife";

    public static int Index => LayerMask.NameToLayer(Name);

    public static void Apply(GameObject target)
    {
        int layer = Index;
        if (target != null && layer >= 0) target.layer = layer;
    }
}
