using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MapPreview))]
public class MapPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MapPreview mapPreview = (MapPreview)target;

        if (GUILayout.Button("Open Terrain Report Preview"))
        {
            TerrainReportPreviewWindow.Open();
        }

        if (!mapPreview.HasPreviewReferences)
        {
            EditorGUILayout.HelpBox("The legacy scene preview is missing its scene objects or settings. Use Terrain Report Preview above; it does not need a plane or mesh GameObject.", MessageType.Info);
        }

        if (DrawDefaultInspector())
        {
            if (mapPreview.autoUpdate)
            {
                mapPreview.DrawMapInEditor();
            }
        }

        using (new EditorGUI.DisabledScope(!mapPreview.HasPreviewReferences))
        {
            if (GUILayout.Button("Generate legacy scene preview"))
            {
                mapPreview.DrawMapInEditor();
            }
        }
    }
}
