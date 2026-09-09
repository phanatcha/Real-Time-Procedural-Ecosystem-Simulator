using UnityEngine;

public interface IEcosystemMaterializationLifecycle
{
    void PrepareForAbstraction();
}

public class EcosystemAnimalProxy : MonoBehaviour
{
    [SerializeField]
    string speciesId;
    [SerializeField]
    float representedPopulation = 1f;

    EcosystemSimulationController owner;

    public string SpeciesId => speciesId;
    public float RepresentedPopulation => representedPopulation;
    public Vector2Int LastHabitableCell { get; private set; }
    public bool HasLastHabitableCell { get; private set; }
    public bool IsBeingAbstracted { get; private set; }

    internal void Initialize(EcosystemSimulationController owner, string speciesId,
        float representedPopulation, Vector2Int cellCoordinate)
    {
        this.owner = owner;
        this.speciesId = speciesId;
        this.representedPopulation = Mathf.Max(0.01f, representedPopulation);
        LastHabitableCell = cellCoordinate;
        HasLastHabitableCell = true;
        IsBeingAbstracted = false;
    }

    internal void SetLastHabitableCell(Vector2Int cellCoordinate)
    {
        LastHabitableCell = cellCoordinate;
        HasLastHabitableCell = true;
    }

    internal void Detach(bool isBeingAbstracted)
    {
        IsBeingAbstracted = isBeingAbstracted;
        owner = null;
    }

    public void ReturnToPopulation()
    {
        if (owner != null) owner.AbstractAnimal(this);
    }

    public void MarkDead()
    {
        if (owner != null)
        {
            owner.ReportAnimalDeath(this);
            return;
        }

        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}
