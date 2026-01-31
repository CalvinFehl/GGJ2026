using UnityEngine;

public class PlayerGraphicObject : MonoBehaviour
{
    public void Reorient(Quaternion targetRotation, float deltaTime, float rotSpeed = 1f)
    {
        if (targetRotation != Quaternion.identity)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotSpeed * deltaTime);
        }
    }

    public void Rotate(Quaternion rotation)
    {
        transform.rotation = rotation;
    }
}
