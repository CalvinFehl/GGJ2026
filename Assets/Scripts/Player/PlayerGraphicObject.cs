using UnityEngine;

public class PlayerGraphicObject : MonoBehaviour
{
    public void Rotate(Quaternion rotation)
    {
        transform.rotation = rotation;
    }
}
