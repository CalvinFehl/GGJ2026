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
        Debug.Log("Reorienting Graphic Object towards: " + targetRotation);

        Quaternion currentRotation = transform.rotation;
        
        Vector3 targetUp = Camera.main.transform.forward;
        Vector3 newForward = Vector3.Cross(Camera.main.transform.right, targetUp).normalized;
        
        Quaternion desiredRotation = Quaternion.LookRotation(newForward, targetUp);
        transform.rotation = Quaternion.Slerp(currentRotation, desiredRotation, speed * deltaTime);
    }
}
