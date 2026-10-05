using System.Collections.Generic;
using UnityEngine;

// Things filed by position into square cells on the ground plane, so a search near a point only looks at the
// cells around it instead of at everything. SpeciesManager files the animals into one so each animal only checks
// the animals near it when it looks for prey, threats and mates.
public sealed class SpatialGrid<T>
{
    // Far beyond any world, and small enough that cell numbers never overflow.
    private const float MaximumRadius = 1000000f;

    private readonly float cellSize;
    private readonly Dictionary<Vector2Int, List<T>> cells = new Dictionary<Vector2Int, List<T>>();
    // Lists of emptied cells, kept to fill again instead of allocating new ones.
    private readonly Stack<List<T>> spareLists = new Stack<List<T>>();
    private readonly List<Vector2Int> emptyCells = new List<Vector2Int>();

    public SpatialGrid(float cellSize)
    {
        this.cellSize = Mathf.Max(0.01f, cellSize);
    }

    public float CellSize => cellSize;
    public int Count { get; private set; }
    // Cells holding at least one thing, plus cells that were emptied by the last Clear.
    public int CellCount => cells.Count;

    // Empties the grid. Cells that were already empty are let go, so the grid only keeps cells in use lately
    // however far things roam.
    public void Clear()
    {
        emptyCells.Clear();
        foreach (KeyValuePair<Vector2Int, List<T>> cell in cells)
        {
            if (cell.Value.Count == 0) emptyCells.Add(cell.Key);
            else cell.Value.Clear();
        }

        foreach (Vector2Int key in emptyCells)
        {
            spareLists.Push(cells[key]);
            cells.Remove(key);
        }

        Count = 0;
    }

    public void Add(T item, Vector3 position)
    {
        Vector2Int key = ToCell(position.x, position.z);
        if (!cells.TryGetValue(key, out List<T> items))
        {
            items = spareLists.Count > 0 ? spareLists.Pop() : new List<T>();
            cells.Add(key, items);
        }

        items.Add(item);
        Count++;
    }

    // Fills results with everything in the cells that the square around position, radius to each side, overlaps.
    // That includes everything within radius and some things a little further, so callers still check exact
    // distances.
    public void Query(Vector3 position, float radius, List<T> results)
    {
        results.Clear();
        radius = Mathf.Clamp(radius, 0f, MaximumRadius);
        Vector2Int minimum = ToCell(position.x - radius, position.z - radius);
        Vector2Int maximum = ToCell(position.x + radius, position.z + radius);

        // A square covering more cells than are in use: going through the cells in use is quicker.
        long cellsCovered = (long)(maximum.x - minimum.x + 1) * (maximum.y - minimum.y + 1);
        if (cellsCovered > cells.Count)
        {
            foreach (KeyValuePair<Vector2Int, List<T>> cell in cells)
            {
                Vector2Int key = cell.Key;
                if (key.x >= minimum.x && key.x <= maximum.x && key.y >= minimum.y && key.y <= maximum.y)
                {
                    results.AddRange(cell.Value);
                }
            }

            return;
        }

        for (int cellZ = minimum.y; cellZ <= maximum.y; cellZ++)
        {
            for (int cellX = minimum.x; cellX <= maximum.x; cellX++)
            {
                if (cells.TryGetValue(new Vector2Int(cellX, cellZ), out List<T> items))
                {
                    results.AddRange(items);
                }
            }
        }
    }

    Vector2Int ToCell(float x, float z)
    {
        return new Vector2Int(Mathf.FloorToInt(x / cellSize), Mathf.FloorToInt(z / cellSize));
    }
}
