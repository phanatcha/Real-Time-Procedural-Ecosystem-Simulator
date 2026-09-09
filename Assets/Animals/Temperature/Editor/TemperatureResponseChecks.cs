using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class TemperatureResponseChecks
{
    [MenuItem("Tools/Ecosystem/Run Temperature Response Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run response checks outside Play mode.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("Temporary Thermal Response Checks");
        SceneManager.MoveGameObjectToScene(root, preview);
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        try
        {
            AnimalTemperature animal = root.AddComponent<AnimalTemperature>();
            int checks = 0;
            var neutral = animal.EvaluateAtTemperature(20f);
            Near(neutral.energyMultiplier, 1f, "Comfort has no metabolic penalty", ref checks);
            Near(animal.EvaluateAtTemperature(10f).coldStress, 0f, "Lower comfort boundary", ref checks);
            Near(animal.EvaluateAtTemperature(30f).heatStress, 0f, "Upper comfort boundary", ref checks);
            var cold = animal.EvaluateAtTemperature(-10f);
            var hot = animal.EvaluateAtTemperature(50f);
            Near(cold.energyMultiplier, hot.energyMultiplier, "Equal exposure has equal metabolic cost", ref checks);
            Near(cold.movementMultiplier, 0.5f, "Severe cold slows movement", ref checks);
            Near(cold.recoveryMultiplier, 1f, "Cold does not apply heat recovery penalty", ref checks);
            Near(hot.recoveryMultiplier, 0.5f, "Severe heat reduces recovery", ref checks);
            Near(hot.movementMultiplier, 1f, "Heat does not apply cold movement penalty", ref checks);
            Near(animal.EvaluateAtTemperature(70f).damagePerSecond, 5f, "Extreme heat damage", ref checks);
            Near(animal.EvaluateAtTemperature(-30f).damagePerSecond, 5f, "Extreme cold damage", ref checks);
            Near(animal.EvaluateAtTemperature(10000f).damagePerSecond, 10f, "Extreme penalty is capped", ref checks);
            Near(animal.EvaluateAtTemperature(float.NaN).energyMultiplier, 1f, "Invalid sample is neutral", ref checks);
            animal.coldTolerance = 30f;
            Near(animal.EvaluateAtTemperature(-10f).coldStress, 0f, "Cold tolerance widens comfort range", ref checks);
            animal.enableTemperatureEffects = false;
            Near(animal.EvaluateAtTemperature(100f).damagePerSecond, 0f, "Effects can be disabled", ref checks);
            animal.enableTemperatureEffects = true;
            animal.preferredTemperature = -15f;
            GameObject childObject = new GameObject("Thermal Offspring");
            childObject.transform.SetParent(root.transform);
            AnimalTemperature child = childObject.AddComponent<AnimalTemperature>();
            bool changed = child.InheritAndMutate(animal, 0f, 0.1f);
            Near(changed ? 1f : 0f, 0f, "Zero mutation preserves inheritance", ref checks);
            Near(child.preferredTemperature, -15f, "Negative Celsius preference inherits intact", ref checks);
            UnityEngine.Random.InitState(4301);
            for (int i = 0; i < 100; i++)
            {
                child.InheritAndMutate(animal, 100f, 10f);
                if (child.preferredTemperature < child.minimumPreferredTemperature ||
                    child.preferredTemperature > child.maximumPreferredTemperature ||
                    child.coldTolerance < 0f || child.coldTolerance > child.maximumTolerance ||
                    child.heatTolerance < 0f || child.heatTolerance > child.maximumTolerance)
                    throw new InvalidOperationException("Thermal mutation exceeded trait bounds.");
            }
            checks++;

            FoodItem food = root.AddComponent<FoodItem>();
            Near(food.EvaluateGrowthMultiplier(20f), 1f, "Plant growth optimum", ref checks);
            Near(food.EvaluateGrowthMultiplier(-10f), 0f, "Cold prevents plant spawning", ref checks);
            Near(food.EvaluateGrowthMultiplier(45f), 0f, "Upper growth boundary", ref checks);
            Near(food.EvaluateGrowthMultiplier(40f), 0.25f, "Hot region slows growth", ref checks);
            Near(food.EvaluateSpoilageMultiplier(20f), 1f, "Reference spoilage", ref checks);
            Near(food.EvaluateSpoilageMultiplier(30f), 2f, "Hotter spoilage", ref checks);
            Near(food.EvaluateSpoilageMultiplier(-100f), 0.25f, "Cold spoilage bounded", ref checks);
            Near(food.EvaluateSpoilageMultiplier(100f), 4f, "Hot spoilage bounded", ref checks);

            food.temperatureAffectsSpoilage = false;
            food.Configure(FoodType.Meat, 50f, lifetime: 120f);
            for (int i = 0; i < 3600; i++) food.AdvanceSpoilage(1f / 60f);
            float freshnessAtNormalSpeed = food.Freshness;
            Near(freshnessAtNormalSpeed, 0.5f, "60 simulated seconds at normal frame increments", ref checks);
            food.Configure(FoodType.Meat, 50f, lifetime: 120f);
            for (int i = 0; i < 72; i++) food.AdvanceSpoilage(50f / 60f);
            Near(food.Freshness, freshnessAtNormalSpeed, "Same spoilage at 50x frame increments", ref checks);
            food.AdvanceSpoilage(0f);
            Near(food.Freshness, 0.5f, "Pause adds no spoilage", ref checks);
            food.Configure(FoodType.Plant, 50f);
            food.AdvanceSpoilage(10000f);
            Near(food.Freshness, 1f, "Plants remain nonperishable by default", ref checks);

            Debug.Log($"Temperature response checks passed ({checks} checks). Objects and random state restored.");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    public static void RunAll()
    {
        TemperatureTestSetup.RunSourceChecks();
        Run();
    }

    private static void Near(float actual, float expected, string label, ref int checks)
    {
        if (float.IsNaN(actual) || float.IsInfinity(actual) || Mathf.Abs(actual - expected) > 0.001f)
            throw new InvalidOperationException($"Temperature response check failed: {label}, expected {expected}, got {actual}.");
        checks++;
    }
}
