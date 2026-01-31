using UnityEngine;

public class PlayerGraphicObject : MonoBehaviour
{
    public void RotateYaw(float yawRot)
    {
        transform.Rotate(0f, yawRot, 0f, Space.World);
    }

    public void Reorient(float deltaTime, float speed = 1f)
    {
        Quaternion currentRotation = transform.rotation;
        
        Vector3 targetUp = Camera.main.transform.forward;
        Vector3 newForward = Vector3.Cross(Camera.main.transform.right, targetUp).normalized;
        
        Quaternion desiredRotation = Quaternion.LookRotation(newForward, targetUp);
        transform.rotation = Quaternion.Slerp(currentRotation, desiredRotation, speed * deltaTime);
    }

    public void SmoothAutoReorient(float speed = 1f)
    {
        StartCoroutine(ReorientCoroutine(speed));
    }

    public System.Collections.IEnumerator ReorientCoroutine(float speed = 1f)
    {
        Quaternion currentRotation = transform.rotation;

        Vector3 targetUp = Camera.main.transform.forward;
        Vector3 newForward = Vector3.Cross(Camera.main.transform.right, targetUp).normalized;

        Quaternion desiredRotation = Quaternion.LookRotation(newForward, targetUp);

        float elapsedTime = 0f;

        if (speed < 1f) speed = 1f;

        float duration = 1f / speed;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);

            // Smooth interpolation
            transform.rotation = Quaternion.Slerp(currentRotation, desiredRotation, t);

            yield return null;
        }

        // Ensure we reach exact target rotation
        transform.rotation = desiredRotation;
    }
}
