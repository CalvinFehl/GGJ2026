using UnityEngine;

public class PlayerGraphicObject : MonoBehaviour
{
    public void RotateYaw(float yawRot)
    {
        transform.Rotate(0f, yawRot, 0f, Space.World);
    }

    public void Tilt(Vector3 tiltDirection, float deltaTime, float speed = 1f)
    {
        Quaternion currentRotation = transform.rotation;
        
        Vector3 targetUp = tiltDirection.normalized;
        Quaternion targetRotation = Quaternion.FromToRotation(transform.up, targetUp) * currentRotation;
        transform.rotation = Quaternion.Slerp(currentRotation, targetRotation, speed * deltaTime);
    }
}
