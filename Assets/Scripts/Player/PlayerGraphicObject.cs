using UnityEngine;

public class PlayerGraphicObject : MonoBehaviour
{
    public void RotateYaw(float yawRot)
    {
        Debug.Log("Rotating Graphic Object Yaw");
        transform.Rotate(0f, yawRot, 0f, Space.World);
    }

    public void Reorient(Vector3 targetRotation, float deltaTime, float speed = 1f)
    {
        Debug.Log("Reorienting Graphic Object");
        Quaternion currentRotation = transform.rotation;
        Quaternion desiredRotation = Quaternion.Euler(targetRotation);
        transform.rotation = Quaternion.Slerp(currentRotation, desiredRotation, speed * deltaTime);
    }
}
