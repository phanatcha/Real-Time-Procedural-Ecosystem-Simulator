using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Boreal.Animals.Tests
{
    // The family lines view (item 65) in a running scene: it records each animal's life from the Species Manager, a
    // newborn under its parent and a death with its time, and builds a line for each founder. Built as
    // AnimalBehaviourPlayModeTests builds its scenes.
    public class FamilyLinesPlayModeTests
    {
        [UnityTest]
        public IEnumerator TheViewRecordsBirthsAndDeathsAndBuildsAFounderLineEach()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode(ReloadsDomain);

            BakeNavMesh(CreateGround(Vector3.zero, 60f));
            SpeciesManager manager = new GameObject("Species Manager").AddComponent<SpeciesManager>();
            manager.simulationSpeed = 5f;
            FamilyLinesView view = new GameObject("Family Lines").AddComponent<FamilyLinesView>();
            view.rules.minimumAnimals = 1;
            SeekFood cloner = CreateAnimal(new Vector3(-10f, 0f, 0f), Genome(maturityTime: 5f), energyFraction: 1f, age: 10f);
            SeekFood doomed = CreateAnimal(new Vector3(10f, 0f, 0f), Genome(maturityTime: 200f), energyFraction: 0.5f, age: 10f);
            yield return null;
            yield return null;
            int clonerId = cloner.AnimalId, doomedId = doomed.AnimalId;
            Assert.Greater(clonerId, 0);
            Assert.Greater(doomedId, 0);
            doomed.Kill(AgentDeathCause.Other);

            float deadline = Time.time + 20f;
            while (Time.time < deadline && Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None).Length < 2) yield return null;
            Assert.GreaterOrEqual(Object.FindObjectsByType<SeekFood>(FindObjectsSortMode.None).Length, 2, "No clone was born.");
            // The clone can come within the first frames; give the chart a couple of seconds to span.
            yield return new WaitForSeconds(2f);

            view.Redraw(manager);
            Dictionary<int, LifeSpan> byId = new Dictionary<int, LifeSpan>();
            foreach (LifeSpan life in view.Lives) byId[life.id] = life;
            Assert.IsTrue(byId.ContainsKey(clonerId) && byId.ContainsKey(doomedId), "A founder's life is missing.");
            Assert.IsFalse(double.IsInfinity(byId[doomedId].ended), "The killed animal's life has no end.");
            Assert.IsTrue(double.IsInfinity(byId[clonerId].ended));
            bool childFound = false;
            foreach (LifeSpan life in view.Lives) childFound |= life.parent == clonerId;
            Assert.IsTrue(childFound, "The clone's life doesn't name its parent.");

            FamilyLineChart chart = view.Latest;
            Assert.IsNotNull(chart);
            List<string> names = new List<string>();
            foreach (FamilyLine line in chart.lines) names.Add(line.name);
            CollectionAssert.Contains(names, "F" + clonerId);
            CollectionAssert.Contains(names, "F" + doomedId);
            int last = chart.times.Length - 1;
            Assert.AreEqual(0, chart.lines[names.IndexOf("F" + doomedId)].clade[last], "The killed founder's line should be empty.");
            Assert.GreaterOrEqual(chart.lines[names.IndexOf("F" + clonerId)].clade[last], 2);
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        static bool ReloadsDomain => !EditorSettings.enterPlayModeOptionsEnabled ||
                                     (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0;

        static GameObject CreateGround(Vector3 centre, float size)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = centre;
            ground.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            return ground;
        }

        static void BakeNavMesh(GameObject ground)
        {
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh, sourceObject = ground.GetComponent<MeshFilter>().sharedMesh,
                    transform = ground.transform.localToWorldMatrix, area = 0
                }
            };
            Bounds bounds = ground.GetComponent<Renderer>().bounds;
            bounds.Expand(new Vector3(2f, 10f, 2f));
            NavMesh.AddNavMeshData(UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds,
                                                                                  Vector3.zero, Quaternion.identity));
        }

        // Founder-like genes that only clone, as in AnimalBehaviourPlayModeTests.
        static AnimalGenome Genome(float maturityTime)
        {
            AnimalGenome genome = AnimalGenome.Create();
            genome[AnimalGene.MoveSpeed] = 6f;
            genome[AnimalGene.Strength] = 25f;
            genome[AnimalGene.BodyBulk] = 1f;
            genome[AnimalGene.BodyHeight] = 1f;
            genome[AnimalGene.VisionRadius] = 110f;
            genome[AnimalGene.MaxEnergy] = 150f;
            genome[AnimalGene.MaxStamina] = 100f;
            genome[AnimalGene.MaxHealth] = 100f;
            genome[AnimalGene.MaturityTime] = maturityTime;
            genome[AnimalGene.MaxLifespan] = 1200f;
            genome[AnimalGene.AgeingOnset] = 1200f;
            genome[AnimalGene.DietAffinity] = 0.15f;
            genome[AnimalGene.PreferredTemperature] = 2f;
            genome[AnimalGene.ColdTolerance] = 18f;
            genome[AnimalGene.HeatTolerance] = 16f;
            genome[AnimalGene.SexualDrive] = 0f;
            genome[AnimalGene.BreedingThreshold] = 0.8f;
            genome[AnimalGene.MutationRate] = 1f;
            return genome;
        }

        static SeekFood CreateAnimal(Vector3 position, AnimalGenome genome, float energyFraction, float age)
        {
            GameObject animal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            animal.SetActive(false);
            animal.name = "Test Animal";
            animal.transform.position = position;
            NavMeshAgent agent = animal.AddComponent<NavMeshAgent>();
            agent.radius = 1.1f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.angularSpeed = 240f;
            agent.acceleration = 36f;
            animal.AddComponent<UtilityDecisionPolicy>().minimumFoodUtility = 0.05f;
            animal.AddComponent<AnimalTemperature>();
            SeekFood behaviour = animal.AddComponent<SeekFood>();
            behaviour.foodInteractionRange = 5f;
            behaviour.attackRange = 5f;
            behaviour.baseFeedingReach = 5f;
            behaviour.wanderRadius = 20f;
            behaviour.enableThirst = false;
            behaviour.PrepareFromOffscreenPopulation(genome, "Test", Color.green, 0, age, energyFraction);
            animal.SetActive(true);
            return behaviour;
        }
    }
}
