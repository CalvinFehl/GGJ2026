using UnityEngine;

public class Assimilateable : MonoBehaviour
{
    [SerializeField]
    private float volume = 1f;

    [SerializeField]
    private bool autoRecalculateOnEnable = true;

    public float Volume => volume;

    private void OnEnable()
    {
        if (autoRecalculateOnEnable)
        {
            RecalculateVolume();
        }
    }

    private void OnValidate()
    {
        if (autoRecalculateOnEnable)
        {
            RecalculateVolume();
        }
    }

    [ContextMenu("Recalculate Volume")]
    public void RecalculateVolume()
    {
        volume = EstimateVolume();
    }

    private float EstimateVolume()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return 0f;
        }

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        Vector3 size = combined.size;
        if (size == Vector3.zero)
        {
            return 0f;
        }

        return Mathf.Abs(size.x * size.y * size.z);
    }
}
