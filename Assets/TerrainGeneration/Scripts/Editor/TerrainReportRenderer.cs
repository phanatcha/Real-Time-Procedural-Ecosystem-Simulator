using System;
using UnityEditor;
using UnityEngine;

public sealed class TerrainReportRenderer : IDisposable
{
    readonly PreviewRenderUtility preview;
    readonly Material material;

    public TerrainReportRenderer()
    {
        Shader shader = Shader.Find("Hidden/TerrainGeneration/ReportPreview");
        if (shader == null) throw new InvalidOperationException("The terrain report preview shader has not imported yet.");
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        preview = new PreviewRenderUtility();
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(0.075f, 0.105f, 0.14f, 1f);
        preview.camera.orthographic = true;
    }

    void Draw(TerrainReportData data, float aspect, Vector2 orbit, float zoom)
    {
        float size = data.WorldSize;
        float relief = data.MaximumHeight - data.MinimumHeight;
        Vector3 centre = new Vector3(0f, (data.MinimumHeight + data.MaximumHeight) * 0.5f, 0f);
        Quaternion rotation = Quaternion.Euler(orbit.y, orbit.x, 0f);
        preview.camera.transform.SetPositionAndRotation(centre - rotation * Vector3.forward * (size * 3f + relief), rotation);
        preview.camera.orthographicSize = (size * 0.76f + relief * 0.5f) / Mathf.Min(1f, aspect) / zoom;
        preview.camera.nearClipPlane = 0.1f;
        preview.camera.farClipPlane = size * 8f + relief * 4f + 100f;
        foreach (TerrainReportData.Chunk chunk in data.Chunks)
            preview.DrawMesh(chunk.mesh, chunk.offset, Quaternion.identity, material, 0);
        preview.Render(false);
    }

    public Texture Render(TerrainReportData data, Rect rect, Vector2 orbit, float zoom)
    {
        preview.BeginPreview(rect, GUIStyle.none);
        Draw(data, rect.width / rect.height, orbit, zoom);
        return preview.EndPreview();
    }

    public Texture2D Export(TerrainReportData data, int resolution, Vector2 orbit, float zoom)
    {
        preview.BeginStaticPreview(new Rect(0f, 0f, resolution, resolution));
        Draw(data, 1f, orbit, zoom);
        return preview.EndStaticPreview();
    }

    public void Dispose()
    {
        preview.Cleanup();
        UnityEngine.Object.DestroyImmediate(material);
    }
}
