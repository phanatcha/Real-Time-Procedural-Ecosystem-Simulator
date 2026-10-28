using UnityEngine;

namespace Boreal.TerrainGeneration
{
    [System.Serializable]
    public class RiverSettings
    {
        public bool enabled = true;
        [Tooltip("Replaced by a seed from the world seed when SeedManager generates the world.")]
        public int seed;
        [Tooltip("World units per meander wavelength.")]
        public float scale = 250f;
        [Range(0.001f, 0.2f)]
        [Tooltip("How wide the carved channel is, in noise-threshold units.")]
        public float width = 0.025f;
        [Range(0, 1)]
        [Tooltip("Rivers only carve where the ground, after the island falloff and before the height curve, falls in this range. " +
                 "Start it about 0.05 below the shoreline (about 0.55 with the current height curve and water level) so rivers reach the sea.")]
        public float minHeightPercent = 0.5f;
        [Range(0, 1)]
        public float maxHeightPercent = 0.8f;
        [Range(0, 1)]
        [Tooltip("Height the channel bed is pulled down to, after the island falloff and before the height curve. " +
                 "Keep it below the shoreline (about 0.55) so the channel fills with water, and no lower than Min Height Percent, " +
                 "or the carving raises the ground at the bottom of the range.")]
        public float bedLevel = 0.5f;

        public void ValidateValues()
        {
            scale = Mathf.Max(scale, 0.01f);
            width = Mathf.Max(width, 0.001f);
            maxHeightPercent = Mathf.Max(maxHeightPercent, minHeightPercent + 0.05f);
        }
    }

    // Wide, flat-floored valleys with steep walls (the U shape glaciers leave), each with a river along its floor.
    // A valley follows a line where a large-scale noise crosses zero, so a valley cut through high ground leaves
    // mountains standing on both sides.
    [System.Serializable]
    public class ValleySettings
    {
        public bool enabled;
        [Tooltip("Replaced by the rivers' seed from the world seed when SeedManager generates the world.")]
        public int seed;
        [Tooltip("World units per valley-noise feature. Valleys follow the lines where this noise crosses zero, so a bigger scale gives fewer, longer valleys.")]
        public float scale = 1200f;
        [Tooltip("How far, in world units, valleys wind from side to side. Keep it below about a tenth of the scale, or the bends fold over.")]
        public float meander = 80f;
        [Tooltip("Half the width of the flat valley floor, in world units.")]
        public float floorHalfWidth = 30f;
        [Tooltip("Half the width of the whole valley, rim to rim, in world units. The walls rise between the floor and this distance, so a smaller gap gives steeper walls.")]
        public float halfWidth = 110f;
        [Range(0, 1)]
        [Tooltip("Height the valley floor is cut down to, after the island falloff and before the height curve. " +
                 "Keep it above the shoreline (about 0.55) so the floor stays dry land. Ground already lower is left alone.")]
        public float floorLevel = 0.6f;
        [Range(0, 1)]
        [Tooltip("How far the ground is cut towards the floor: 1 cuts all the way, 0 turns valleys off.")]
        public float strength = 1f;
        [Tooltip("Half the width of the river along the valley floor, in world units. It is cut down to the rivers' bed level, so it fills with water. 0 leaves the floor dry.")]
        public float riverHalfWidth = 6f;
        [Range(0, 1)]
        [Tooltip("How far the land beside a valley is raised towards the highest height, so mountains stand on both sides. " +
                 "Only ground above the valley floor is raised, so the coastline and the sea stay as they are.")]
        public float shoulderHeight;
        [Tooltip("How far beyond the valley's rim the raised land reaches, in world units. It is highest about a third of the way out.")]
        public float shoulderWidth = 250f;

        public void ValidateValues()
        {
            scale = Mathf.Max(scale, 0.01f);
            meander = Mathf.Max(meander, 0f);
            floorHalfWidth = Mathf.Max(floorHalfWidth, 0f);
            halfWidth = Mathf.Max(halfWidth, floorHalfWidth + 1f);
            riverHalfWidth = Mathf.Max(riverHalfWidth, 0f);
            shoulderWidth = Mathf.Max(shoulderWidth, 1f);
        }
    }

    [System.Serializable]
    public class LakeSettings
    {
        public bool enabled = true;
        [Tooltip("Replaced by a seed from the world seed when SeedManager generates the world.")]
        public int seed;
        [Tooltip("World units per lake-noise feature - bigger = fewer, larger lakes.")]
        public float scale = 400f;
        [Range(0, 1)]
        [Tooltip("Only the noise values above this become a lake.")]
        public float threshold = 0.62f;
        [Range(0, 1)]
        [Tooltip("Lakes only settle where the ground, after the island falloff and before the height curve, falls in this range. " +
                 "Start it above the shoreline (about 0.55 with the current height curve and water level) so lakes stay inland.")]
        public float minHeightPercent = 0.6f;
        [Range(0, 1)]
        public float maxHeightPercent = 0.82f;
        [Range(0, 1)]
        [Tooltip("Height the lake bed is pulled down to, after the island falloff and before the height curve. " +
                 "Keep it just below the shoreline (about 0.55) so the lake fills with water.")]
        public float bedLevel = 0.5f;

        public void ValidateValues()
        {
            scale = Mathf.Max(scale, 0.01f);
            maxHeightPercent = Mathf.Max(maxHeightPercent, minHeightPercent + 0.05f);
        }
    }
}
