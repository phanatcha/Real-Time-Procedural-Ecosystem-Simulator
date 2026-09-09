# Temperature foundation and biome integration

This prototype supplies ambient temperature independently of terrain generation. It targets this project's Unity 6000.4 version. Animal response values and food profiles are adjustable simulation rules, not measured species physiology.

## Start testing in the existing scene

1. Allow Unity to import and compile the scripts, then leave Play mode.
2. Choose **Tools > Ecosystem > Create Temperature Test Setup**. It fits the active terrain, selects the created root, and supports Undo. If this scene already has a temperature service, it selects that service instead of creating a duplicate.
3. The setup starts with **Manual Temperature Provider = 20°C**. Save the scene to retain it.
4. On Temperature System, assign its **Provider** by dragging the desired provider component's header into the field. All three providers are on the setup root:
   - **Manual**: one temperature everywhere, editable during Play.
   - **Gradient**: -10°C to 40°C across the terrain's world X extent; clamps outside its endpoints.
   - **Zone**: three child regions at -10°C, 20°C and 40°C. Change their transforms, sizes, temperatures or edge blend width. Enable Scene view Gizmos to see bounds and labels.
5. **Use Global Override** on the system forces one temperature regardless of provider. Turn it off to restore that provider. Disable the service to remove temperature effects.
6. Inspect **Animal Temperature** on an animal for its inherited traits, sampled temperature, stress and actual modifiers. On a carcass, inspect **Food Item > Spoilage Runtime** for freshness and its current decay rate.

Existing animals get AnimalTemperature automatically at startup; the Capsule prefab already includes it for editing before Play. No test climate is automatically inserted into the saved scene.

## Contract for the procedural terrain team

Implement a component deriving from `TemperatureProvider` and assign it to the scene's `TemperatureSystem.provider`:

```csharp
public override bool TryGetTemperature(Vector3 worldPosition, out float temperatureCelsius)
```

- Input is a Unity **world-space position**, not a terrain-local or chunk-local coordinate. Convert to the terrain generator's grid/chunk coordinates inside the adapter.
- Return **true and finite degrees Celsius** when data is available. Zero and negative Celsius values are valid temperatures; they must not mean "missing".
- Return **false** while a chunk/biome is unloaded, being generated, or otherwise unavailable. No fake biome temperature is required during generation.
- Read already generated/cached data on the main thread. Sampling must not generate terrain, start blocking loads, or allocate large collections.
- The adapter owns biome-edge blending. The service does not know biome IDs, terrain dimensions, or how the generator stores regions.
- Assign one service per simulation world. Its component reference can be switched at runtime. A disabled/unavailable source is handled at the consumers' next sample; they retry automatically.
- If multiple loaded scenes contain a service, the first registered service supplies temperature until disabled. Do not rely on additive-scene load order to choose a climate. Use one persistent service and replace its provider as needed.

The temporary zone/gradient sources are examples of this contract, not a required biome data format. A later adapter can calculate biome baseline plus weather, seasons or altitude adjustments and return the resulting local Celsius value. None of the animal or food code needs biome-specific dependencies.

Consumers use:

```csharp
if (TemperatureSystem.TryGetTemperatureAt(transform.position, out float celsius))
{
    // Apply this consumer's own response to the valid local temperature.
}
else
{
    // Source is unavailable: keep neutral effects and retry on the next sample.
}
```

`GetTemperatureAt(position)` is a convenience for displays that accept a fallback. It returns the service fallback (default 20°C), but does not expose availability. Use the **Try** API for gameplay. Animals receive no thermal penalties when it returns false, even if their preferred climate differs from 20°C. Food retains normal growth and reference spoilage rate. Global Override takes precedence even when no provider is assigned.

Preview scenes and Prefab Mode cannot replace the live world's service. `system.TrySampleTemperature(...)` permits isolated source checks in editor previews without registering a global source.

## Current animal model

`AnimalTemperature` holds preferred temperature, cold tolerance and heat tolerance, all inherited with independent mutation. Celsius preference uses an additive mutation so cold preferences can be negative; tolerance is bounded and nonnegative. The comfort interval is preference minus cold tolerance through preference plus heat tolerance.

The default comfort range is **10–30°C**. Each 20°C outside the range adds one stress unit, capped at three:

- Energy drain is multiplied by `1 + 0.5 × stress`.
- Beyond 0.5 cold stress, movement gradually falls to 50% by stress 1.
- Beyond 0.5 heat stress, stamina recovery gradually falls to 50% by stress 1.
- Above stress 1, direct exposure damage is `5 × (stress - 1)` health per simulated second.

At defaults, -10°C causes 1.5× energy drain and 0.5× movement without direct exposure damage. 40°C causes 1.25× energy drain; 50°C also reduces stamina recovery to 0.5×. -30°C or 70°C causes 5 health damage per simulated second. These examples are tuning cases, not predictions about real animals.

SeekFood applies the modifiers without permanently changing genetic speed or base metabolic drain. Cold also affects travel-cost estimates and escape speed. Heat/cold exposure have their own death causes and species counters. Reproduction resamples the child's location at startup, preventing copied parental stress from becoming inherited state.

## Plants and food

The existing spawner has no individual plant growth stages, so initial "growth" means the probability of producing a new food item. Each plant prefab has a growth profile: zero at/below 0°C, rising to full growth at 15°C, full growth through 25°C, and falling to zero at 45°C. At 40°C suitability is 0.25. The spawner applies suitability to per-second spawn probability before converting to the configured check interval, using the **final NavMesh placement position**, not the spawner's location.

Food with `referenceLifetime = 0` does not spoil. Existing plants retain that default. Carcasses use the animal's carcass lifetime as their reference lifetime. Freshness integrates simulated elapsed time; default spoilage is 1× at 20°C, 2× at 30°C, and clamped to 0.25–4×. A 120-second carcass therefore lasts approximately 120 seconds at 20°C, 60 at 30°C, or 30 at 40°C. Changing temperature changes the remaining decay rate, without resetting freshness. Nutrition stays intact until expiry; expiry immediately removes food from the available-item registry.

## Timing, verification and limits

Temperature is sampled by position every 0.5 **simulated** seconds with at most one sample per rendered frame, never through enter/exit trigger events. Animals and food apply the full elapsed simulated duration to penalties and freshness between samples. Pause adds no exposure or spoilage. A moving animal can skip a narrow region at high speed, so use broad zones/blend widths and test transitions at 1× as well as 50×. Provider work must remain cheap; a later large biome map should use direct chunk/grid lookup rather than scanning all biomes.

Repeatable checks are available under **Tools > Ecosystem > Run Temperature Source Checks** and **Run Temperature Response Checks**. They use temporary preview objects, restore the random generator, and throw on failure. Response checks include comfort boundaries, severe/extreme effects, inherited negative Celsius, mutation bounds, growth response, spoilage clamps, pause, and 1× versus 50× elapsed-time increments. These checks do not certify full NavMesh movement equivalence between speeds.

For a controlled scene comparison, use a uniform override, identical initial animals, mutation disabled, and equivalent **simulated** durations at each speed. Check age, energy use, stamina, and exposure deaths. Test food growth and spoilage separately before enabling all pressures in a population experiment. A 50× result remains subject to the simulation's existing physics approximation and achieved-speed limits.

Still to design: climate-seeking behavior/shelter, body temperature and acclimation, costs for broad thermal tolerance, interactions with body bulk/height, plant lifecycle damage, and a real biome adapter. These are intentionally not implied by the current temperature response. The procedural generator's API is not yet available; only its adapter needs implementation once that API is ready.
