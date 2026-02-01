using UnityEngine;

public class WizardLook : MonoBehaviour
{
    [Header("Transforms")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform body;
    [SerializeField] private Transform hat;
    [SerializeField] private Transform reference;

    [Header("Random Look")]
    [SerializeField] private float minHoldTime = 1.5f;
    [SerializeField] private float maxHoldTime = 4.0f;
    [SerializeField] private float headTurnSpeed = 2.5f;

    [Header("Pitch Limits")]
    [SerializeField, Range(0f, 89f)] private float maxPitch = 70f;

    [Header("Body Follow")]
    [SerializeField] private float bodyFollowSpeed = 1.6f;
    [SerializeField] private float bodyFollowThreshold = 40f;
    [SerializeField] private float bodyFollowStrength = 0.6f;

    private Vector2 targetHeadAngles;
    private float holdTimer;

    private float headYaw;
    private float headPitch;

    private Quaternion headBaseRotation = Quaternion.identity;
    private Quaternion bodyBaseRotation = Quaternion.identity;
    private Quaternion headRestRelative = Quaternion.identity;
    private Quaternion bodyRestRelative = Quaternion.identity;
    private Quaternion referenceBaseRotation = Quaternion.identity;
    private float bodyYawOffset;

    private Vector3 headOffsetFromBody = Vector3.zero;

    private Vector3 hatLocalOffset;
    private Quaternion hatRotationOffset = Quaternion.identity;

    private void Reset()
    {
        head = transform;
        body = transform.parent;
    }

    private void Awake()
    {
        if (head == null)
        {
            head = transform;
        }

        if (reference != null)
        {
            referenceBaseRotation = reference.rotation;
        }
        else
        {
            Vector3 forward = body != null ? body.forward : head.forward;
            Vector3 flattened = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flattened.sqrMagnitude < 0.0001f)
            {
                flattened = Vector3.forward;
            }

            referenceBaseRotation = Quaternion.LookRotation(flattened, Vector3.up);
        }

        headBaseRotation = head.rotation;
        headRestRelative = Quaternion.Inverse(referenceBaseRotation) * headBaseRotation;
        if (body != null)
        {
            bodyBaseRotation = body.rotation;
            bodyRestRelative = Quaternion.Inverse(referenceBaseRotation) * bodyBaseRotation;
        }
        CacheHatOffsets();
        CacheHeadOffset();
    }

    private void OnEnable()
    {
        PickNewTarget();
        headYaw = 0f;
        headPitch = 0f;
        bodyYawOffset = 0f;
    }

    private void Update()
    {
        if (head == null || body == null)
        {
            return;
        }

        holdTimer -= Time.deltaTime;
        if (holdTimer <= 0f)
        {
            PickNewTarget();
        }

        headYaw = Mathf.LerpAngle(headYaw, targetHeadAngles.x, headTurnSpeed * Time.deltaTime);
        headPitch = Mathf.LerpAngle(headPitch, targetHeadAngles.y, headTurnSpeed * Time.deltaTime);
        headPitch = Mathf.Clamp(headPitch, -maxPitch, maxPitch);

        Quaternion yawRot = Quaternion.AngleAxis(headYaw, Vector3.up);
        Quaternion pitchRot = Quaternion.AngleAxis(headPitch, Vector3.right);
        head.rotation = referenceBaseRotation * yawRot * pitchRot * headRestRelative;

        float yawToBody = Mathf.DeltaAngle(0f, headYaw);
        if (Mathf.Abs(yawToBody) > bodyFollowThreshold)
        {
            float followAmount = yawToBody * bodyFollowStrength;
            float newBodyYaw = Mathf.LerpAngle(bodyYawOffset, followAmount, bodyFollowSpeed * Time.deltaTime);
            bodyYawOffset = newBodyYaw;
            body.rotation = referenceBaseRotation * Quaternion.AngleAxis(bodyYawOffset, Vector3.up) * bodyRestRelative;
        }

        if (body != null)
        {
            head.position = body.position + (body.rotation * headOffsetFromBody);
        }
    }

    private void LateUpdate()
    {
        if (hat == null || head == null)
        {
            return;
        }

        hat.position = head.TransformPoint(hatLocalOffset);
        hat.rotation = head.rotation * hatRotationOffset;
    }

    private void PickNewTarget()
    {
        holdTimer = Random.Range(minHoldTime, maxHoldTime);

        Vector3 dir = Random.onUnitSphere;
        Vector3 dirRef = Quaternion.Inverse(referenceBaseRotation) * dir;
        float pitch = Mathf.Asin(Mathf.Clamp(dirRef.y, -1f, 1f)) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        float yaw = Mathf.Atan2(dirRef.x, dirRef.z) * Mathf.Rad2Deg;
        targetHeadAngles = new Vector2(yaw, pitch);
    }

    private void CacheHatOffsets()
    {
        if (hat == null || head == null)
        {
            return;
        }

        hatLocalOffset = head.InverseTransformPoint(hat.position);
        hatRotationOffset = Quaternion.Inverse(head.rotation) * hat.rotation;
    }

    private void CacheHeadOffset()
    {
        if (head == null || body == null)
        {
            return;
        }

        headOffsetFromBody = Quaternion.Inverse(body.rotation) * (head.position - body.position);
    }

}
