using System;
using UnityEngine;

public static class TerrainGrid
{
    public static Vector2Int WorldToCell(Vector2 worldPosition, float cellSize)
    {
        ValidateSize(cellSize, nameof(cellSize));
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / cellSize),
            Mathf.FloorToInt(worldPosition.y / cellSize));
    }

    public static Vector2Int WorldToCell(Vector3 worldPosition, float cellSize)
    {
        return WorldToCell(new Vector2(worldPosition.x, worldPosition.z), cellSize);
    }

    public static Vector2 CellToWorldPosition(Vector2Int cell, float cellSize)
    {
        ValidateSize(cellSize, nameof(cellSize));
        return new Vector2(
            (cell.x + 0.5f) * cellSize,
            (cell.y + 0.5f) * cellSize);
    }

    public static Vector3 CellToWorldPosition(Vector2Int cell, float cellSize, float worldY)
    {
        Vector2 position = CellToWorldPosition(cell, cellSize);
        return new Vector3(position.x, worldY, position.y);
    }

    public static Vector2Int WorldToChunkCoordinate(Vector2 worldPosition, MeshSettings meshSettings)
    {
        if (meshSettings == null) throw new ArgumentNullException(nameof(meshSettings));
        return WorldToChunkCoordinate(worldPosition, meshSettings.meshWorldSize);
    }

    public static Vector2Int WorldToChunkCoordinate(Vector3 worldPosition, MeshSettings meshSettings)
    {
        return WorldToChunkCoordinate(new Vector2(worldPosition.x, worldPosition.z), meshSettings);
    }

    public static Vector2Int WorldToChunkCoordinate(Vector2 worldPosition, float chunkWorldSize)
    {
        ValidateSize(chunkWorldSize, nameof(chunkWorldSize));
        float halfSize = chunkWorldSize * 0.5f;
        return new Vector2Int(
            Mathf.FloorToInt((worldPosition.x + halfSize) / chunkWorldSize),
            Mathf.FloorToInt((worldPosition.y + halfSize) / chunkWorldSize));
    }

    public static Vector2 ChunkCoordinateToWorldPosition(Vector2Int chunkCoordinate, MeshSettings meshSettings)
    {
        if (meshSettings == null) throw new ArgumentNullException(nameof(meshSettings));
        return ChunkCoordinateToWorldPosition(chunkCoordinate, meshSettings.meshWorldSize);
    }

    public static Vector2 ChunkCoordinateToWorldPosition(Vector2Int chunkCoordinate, float chunkWorldSize)
    {
        ValidateSize(chunkWorldSize, nameof(chunkWorldSize));
        return new Vector2(
            chunkCoordinate.x * chunkWorldSize,
            chunkCoordinate.y * chunkWorldSize);
    }

    static void ValidateSize(float size, string parameterName)
    {
        if (size <= 0f || float.IsNaN(size) || float.IsInfinity(size))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Grid size must be finite and greater than zero.");
        }
    }
}
