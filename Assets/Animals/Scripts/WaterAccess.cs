using Boreal.TerrainGeneration;
using UnityEngine;

namespace Boreal.Animals
{
    // How an animal moves in water. Every animal can wade in shallow water, but slowly and at a steep energy cost.
    // Deep water needs a swimming ability of at least deepWaterAbility, which comes from fins (and a little from
    // long legs; see AnimalBodyPlan). The better the swimmer, the faster it moves in water and the less extra
    // energy water costs it, so even small fins pay off in the shallows.
    [System.Serializable]
    public struct WaterMovement
    {
        [Tooltip("Speed in water with no swimming ability, as a fraction of the animal's gene speed.")]
        public float wadingSpeed;
        [Tooltip("Speed in water at full swimming ability, as a fraction of the animal's gene speed.")]
        public float swimmingSpeed;
        [Tooltip("Energy use in water with no swimming ability, as a multiple of the animal's normal energy use.")]
        public float wadingEnergyCost;
        [Tooltip("Swimming ability needed to enter deep water. From this ability on, water costs no extra energy.")]
        [Range(0f, 1f)] public float deepWaterAbility;

        public static WaterMovement Default => new WaterMovement
        {
            wadingSpeed = 0.3f,
            swimmingSpeed = 1.3f,
            wadingEnergyCost = 4f,
            deepWaterAbility = 0.5f
        };

        public bool CanSwimDeepWater(float swimmingAbility) => swimmingAbility >= deepWaterAbility;

        // Fraction of the gene speed the animal moves at in water.
        public float SpeedMultiplier(float swimmingAbility) =>
            Mathf.Max(0.01f, Mathf.Lerp(wadingSpeed, swimmingSpeed, Mathf.Clamp01(swimmingAbility)));

        // Multiple of the animal's normal energy use while it is in water.
        public float EnergyMultiplier(float swimmingAbility)
        {
            float progress = deepWaterAbility > 0f ? Mathf.Clamp01(swimmingAbility / deepWaterAbility) : 1f;
            return Mathf.Max(0f, Mathf.Lerp(wadingEnergyCost, 1f, progress));
        }

        // What a metre of water costs the animal's route planning compared with a metre of land: how much longer it
        // takes, times how much more energy it burns. Routes then only cross water when it saves enough walking.
        // landSpeedMultiplier is the body's effect on land speed (legs, plates, fins). Unity needs costs of at least 1.
        public float PathCost(float swimmingAbility, float landSpeedMultiplier)
        {
            float slowdown = Mathf.Max(0.01f, landSpeedMultiplier) / SpeedMultiplier(swimmingAbility);
            return Mathf.Max(1f, slowdown * EnergyMultiplier(swimmingAbility));
        }
    }

    // Where water is and which parts of it an animal may use. HabitatNavigation builds the water surface into the
    // NavMesh as extra areas, and each animal's area mask and costs come from its swimming ability. Rivers and lakes
    // have areas of their own, because they freeze in winter: while they are frozen every animal may walk on them as
    // on land, without the NavMesh being rebuilt.
    public static class WaterAccess
    {
        // NavMesh areas of the water surface. Areas 0-2 are Unity's Walkable, Not Walkable and Jump. These are used
        // by number, so the project's Navigation settings don't need to name them. Dead zones (see SeaBiomes) are
        // deep water with their own area, so routes can be charged extra for crossing them. Fresh water (rivers and
        // lakes, see FreshWater) is shallow or deep like the sea, and never a dead zone.
        public const int ShallowWaterArea = 3;
        public const int DeepWaterArea = 4;
        public const int DeadZoneArea = 5;
        public const int FreshShallowWaterArea = 6;
        public const int FreshDeepWaterArea = 7;
        public const int FreshWaterAreas = (1 << FreshShallowWaterArea) | (1 << FreshDeepWaterArea);
        public const int WaterAreas = (1 << ShallowWaterArea) | (1 << DeepWaterArea) | (1 << DeadZoneArea) | FreshWaterAreas;
        public const int ShallowWaterAreas = (1 << ShallowWaterArea) | (1 << FreshShallowWaterArea);
        public const int AllAreas = -1;
        public const int LandAreas = AllAreas & ~WaterAreas;
        public const int WadingAreas = AllAreas & ~((1 << DeepWaterArea) | (1 << DeadZoneArea) | (1 << FreshDeepWaterArea));

        // How far rivers and lakes have frozen over, 0-1, set by the season clock. They bear an animal's weight once
        // they are covered (1), and stay walkable while they thaw until half the ice has gone, so animals out on the
        // ice in spring have time to reach the shore.
        public const float ThawedBelowIce = 0.5f;
        public static float IceCover { get; private set; }
        public static bool FreshWaterFrozen { get; private set; }

        public static void SetIce(float cover)
        {
            IceCover = Mathf.Clamp01(cover);
            if (IceCover >= 0.999f) FreshWaterFrozen = true;
            else if (IceCover < ThawedBelowIce) FreshWaterFrozen = false;
        }

        // Land, and frozen rivers and lakes: where an animal that can't swim may choose to wander.
        public static int WalkableAreas => FreshWaterFrozen ? LandAreas | FreshWaterAreas : LandAreas;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IceCover = 0f;
            FreshWaterFrozen = false;
        }

        // How far above the water level the NavMesh may sit and still count as water. The NavMesh stores heights in
        // small steps, so its flat water surface can end up slightly above the true level.
        public const float SurfaceTolerance = 0.4f;

        // Height of the water surface in the current world, set by HabitatNavigation. Negative infinity when there
        // is no water to use, so nothing counts as being in water.
        public static float SurfaceHeight { get; set; } = float.NegativeInfinity;

        public static bool HasWater => !float.IsNegativeInfinity(SurfaceHeight);

        // Water up to this deep is shallow enough for every animal to wade, set by HabitatNavigation with the surface.
        public static float WadingDepth { get; set; } = 1f;

        // True when an animal whose NavMesh position is at this height is in the water. Land is never below the
        // water level, so only the flat water surface (and the foot of the shore) is this low.
        public static bool IsInWater(float navigationHeight) => navigationHeight <= SurfaceHeight + SurfaceTolerance;

        // The areas an animal may travel through: deep water only if it can swim there, or once rivers and lakes have
        // frozen over, on their ice.
        public static int AreaMask(bool canSwimDeepWater) =>
            canSwimDeepWater ? AllAreas : FreshWaterFrozen ? WadingAreas | (1 << FreshDeepWaterArea) : WadingAreas;

        public enum Surface { None, Land, ShallowWater, DeepWater, DeadZone, FreshShallowWater, FreshDeepWater }

        // One corner of a navigation triangle: land an animal can stand on, water of some depth (perhaps in a dead
        // zone, perhaps fresh), or neither (a cliff, or outside the habitat).
        public readonly struct Corner
        {
            public readonly bool usable;
            public readonly bool isWater;
            public readonly float depth;
            public readonly bool deadZone;
            public readonly bool fresh;

            Corner(bool usable, bool isWater, float depth, bool deadZone, bool fresh)
            {
                this.usable = usable;
                this.isWater = isWater;
                this.depth = depth;
                this.deadZone = deadZone;
                this.fresh = fresh;
            }

            public static Corner Unusable => default;
            public static Corner Land => new Corner(true, false, 0f, false, false);
            public static Corner Water(float depth, bool deadZone = false, bool fresh = false) =>
                new Corner(true, true, Mathf.Max(0f, depth), deadZone && !fresh, fresh);
        }

        // The surface a navigation triangle becomes. Any unusable corner leaves a hole. A triangle with no water is
        // land. A triangle with water is water, including the ramp from the shore down into it. Water no deeper than
        // wadingDepth at every corner is shallow; anything deeper is deep water, or dead zone if any corner is in one.
        // Water is fresh when every water corner is: where a river meets the sea, the sea wins, so the mouth never
        // freezes into a bridge out to sea.
        public static Surface ClassifyTriangle(Corner a, Corner b, Corner c, float wadingDepth)
        {
            if (!a.usable || !b.usable || !c.usable) return Surface.None;
            if (!a.isWater && !b.isWater && !c.isWater) return Surface.Land;

            bool fresh = (!a.isWater || a.fresh) && (!b.isWater || b.fresh) && (!c.isWater || c.fresh);
            float deepest = Mathf.Max(a.depth, Mathf.Max(b.depth, c.depth));
            if (deepest <= wadingDepth) return fresh ? Surface.FreshShallowWater : Surface.ShallowWater;
            if (fresh) return Surface.FreshDeepWater;
            return a.deadZone || b.deadZone || c.deadZone ? Surface.DeadZone : Surface.DeepWater;
        }

        public static int AreaOf(Surface surface) => surface switch
        {
            Surface.ShallowWater => ShallowWaterArea,
            Surface.DeepWater => DeepWaterArea,
            Surface.DeadZone => DeadZoneArea,
            Surface.FreshShallowWater => FreshShallowWaterArea,
            Surface.FreshDeepWater => FreshDeepWaterArea,
            _ => 0
        };

        // Rivers and lakes; the sea is salt.
        public static bool IsFresh(in EnvironmentSample sample)
        {
            return sample.isValid && sample.isWater && FreshWater.IsFresh(sample.riverStrength, sample.lakeStrength);
        }
    }
}
