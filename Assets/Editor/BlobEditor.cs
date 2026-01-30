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
    }
}
