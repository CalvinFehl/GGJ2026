using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Blob))]
public class BlobEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        Blob blob = (Blob)target;
        GUILayout.Space(8f);

        if (GUILayout.Button("Fill Solid"))
        {
            Undo.RecordObject(blob, "Fill Solid");
            blob.FillSolid();
            blob.RebuildMesh();
            EditorUtility.SetDirty(blob);
        }

        if (GUILayout.Button("Fill Sphere"))
        {
            Undo.RecordObject(blob, "Fill Sphere");
            blob.FillSphere();
            blob.RebuildMesh();
            EditorUtility.SetDirty(blob);
        }

        GUILayout.Space(8f);

        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("Run Scanline"))
        {
            blob.StartScanline();
        }
        GUI.enabled = true;

        GUILayout.Space(8f);

        if (GUILayout.Button("Scan Target To Grid"))
        {
            Undo.RecordObject(blob, "Scan Target To Grid");
            blob.ScanObjectToGrid(blob.scanTarget);
            EditorUtility.SetDirty(blob);
        }

        if (GUILayout.Button("Apply Scan Grid To Mesh"))
        {
            Undo.RecordObject(blob, "Apply Scan Grid To Mesh");
            blob.ApplyScanGridToMesh();
            EditorUtility.SetDirty(blob);
        }

        if (GUILayout.Button("Apply Scan Colors To Mesh"))
        {
            Undo.RecordObject(blob, "Apply Scan Colors To Mesh");
            blob.ApplyScanColorsToGrid();
            blob.RebuildMesh();
            EditorUtility.SetDirty(blob);
        }
    }
}
