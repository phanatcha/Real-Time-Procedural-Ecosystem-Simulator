# Distant animals as numbers

The live simulation can afford about 400 animals, because each one is a GameObject with its own AI, navigation and food search. `OffscreenPopulationBridge` keeps animals far from the camera as numbers instead, so the whole island can hold many more, while animals near the camera stay fully simulated.

It is **off by default**. Turn on **Keep Distant Animals As Numbers** on the Animal Terrain Integration object before pressing Play.

## How it works

The island is divided into 250 m cells. Each cell keeps, per species, a count and up to eight real genomes that stand for the rest.

- **Leaving:** a live animal more than 550 m from the camera joins its cell's population. Its genome and its energy use are kept. It is not recorded as a death.
- **Returning:** within 400 m of the camera, populations become animals again, up to 12 per cell and species and within the live population ceiling. Each carries a real genome from its population, a random age and 70% energy. It is not recorded as a birth.
- **Off-screen:** populations grow, share food and move to neighbouring cells, updated every 0.5 s near the camera and every 6 s far away.

The statistics panel shows the off-screen total next to the live count. A species with animals off-screen is still alive.

## The maths

When a world is generated, each cell's walkable ground is surveyed in the background (8 × 8 samples) for its plant energy and temperature.

**Plant energy per cell** follows `FoodSpawner`: one plant site per 16 m square, holding a plant with a chance equal to the grass biomass, worth 75 × (0.6–1.4 by moisture), regrowing every 90 s ÷ the temperature growth rate.

**Carrying capacity** for an animal with genome *g* in cell *c*:

```
K = harvest efficiency × plant energy × plant digestion(diet) ÷ (energy use × temperature stress)
```

Digestion and temperature stress use the same formulas as `SeekFood` and `AnimalTemperature`. Energy use is measured on the live animal before it leaves.

**Sharing food:** each animal of a population eats 1/K of the cell's food. A population grows toward the room the others leave it, including the live animals standing in the cell:

```
room = K × (1 − food share eaten by everyone else)
N(t) = room / (1 + (room − N) / N · e^(−r·t))
```

- *r* comes from the genes: (1 − maturity/lifespan) ÷ 60 s cooldown − 1/lifespan, about 0.011 per second for the founders.
- With no room, the population dies at 1/lifespan plus energy use ÷ energy store.

**Migration:** 0.4% per second leaves for the side neighbours, favouring those with more room, and takes genomes with it.

## Not yet included

- **Off-screen evolution (step 3b):** off-screen genomes are copied, not mutated or selected. Evolution still happens only among live animals. Harmful mutations are neither gained nor shed off-screen, but those a genome already carries stay with it and raise its energy use, so a heavily loaded population has a lower carrying capacity. Perks don't flip off-screen either. Their upkeep is part of the energy use measured on the live animal, thick fur shifts the comfort range, and a scavenger gut digests less of the cell's plant food; venom, camouflage and regeneration have no off-screen effect, since hunting isn't modelled there.
- **Off-screen speciation (step 3c):** only live animals are checked for species splits.
- **Hunting and tall forest food** are not modelled off-screen.
- **Mating** is not modelled off-screen: populations grow at the same rate whatever their sexual drive, and genomes are not recombined.
- **Harvest efficiency (0.5)** is an estimate. Compare the live density near the camera with the off-screen density to tune it.

## Tests

`OffscreenPopulationTests` checks plant energy, capacity, growth and death rates, food sharing between species and with live animals, migration and extinction, and that digestion and temperature stress match `SeekFood` and `AnimalTemperature`.
