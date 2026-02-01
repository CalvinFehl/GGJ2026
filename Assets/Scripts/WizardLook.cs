using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    [Header("Player Focus")]
    [SerializeField] private float focusTurnDuration = 2.0f;
    [SerializeField] private float focusHoldDuration = 2.0f;
    [SerializeField] private bool invertFocusDirection = true;

    [Header("Alert")]
    private float playerSpeedThreshold = 0.01f;
    [SerializeField] private float alertBlinkDuration = 2.0f;
    [SerializeField] private float alertBlinkInterval = 0.2f;
    [SerializeField] private Color alertColor = Color.red;
    [SerializeField] private Light hatLight;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip alertSound;



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

    private Transform focusTarget;
    private Vector2 focusStartAngles;
    private float focusTurnTimer;
    private float focusHoldTimer;
    private bool isFocusing;
    private Coroutine alertRoutine;
    private bool isBlinking;
    private bool alertTriggered;
    private bool holdEntered;
    private Renderer[] hatRenderers;
    private Color[] hatRendererBaseColors;
    private Color hatLightBaseColor = Color.white;
    private bool hatLightBaseEnabled;

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
        
        // Initialize AudioSource if not assigned
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        
        CacheHatRenderers();
        CacheHatOffsets();
        CacheHeadOffset();
    }

    private void OnEnable()
    {
        PlayerController.PlayerTransformed += HandlePlayerTransformed;
        PickNewTarget();
        headYaw = 0f;
        headPitch = 0f;
        bodyYawOffset = 0f;
    }

    private void OnDisable()
    {
        PlayerController.PlayerTransformed -= HandlePlayerTransformed;
        StopAlertRoutine(false);
    }

    private void Update()
    {
        if (head == null || body == null)
        {
            return;
        }

        holdTimer -= Time.deltaTime;
        if (!isFocusing && holdTimer <= 0f)
        {
            PickNewTarget();
        }

        Vector2 desiredAngles = targetHeadAngles;
        if (isFocusing && focusTarget != null)
        {
            focusTurnTimer += Time.deltaTime;
            Vector2 targetAngles = GetAnglesToTarget(focusTarget);
            bool turnComplete = focusTurnDuration <= 0f || focusTurnTimer >= focusTurnDuration;
            if (turnComplete)
            {
                desiredAngles = targetAngles;
                if (!holdEntered)
                {
                    holdEntered = true;
                    UpdateHoldAlertState(false);
                }
                else
                {
                    UpdateHoldAlertState(true);
                }
                focusHoldTimer += Time.deltaTime;
                if (focusHoldTimer >= focusHoldDuration)
                {
                    EndFocus();
                }
            }
            else
            {
                float t = Mathf.Clamp01(focusTurnTimer / focusTurnDuration);
                desiredAngles = Vector2.Lerp(focusStartAngles, targetAngles, t);
                UpdateTurnAlertState();
            }
        }

        headYaw = Mathf.LerpAngle(headYaw, desiredAngles.x, headTurnSpeed * Time.deltaTime);
        headPitch = Mathf.LerpAngle(headPitch, desiredAngles.y, headTurnSpeed * Time.deltaTime);
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

    private void HandlePlayerTransformed(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        alertTriggered = false;
        holdEntered = false;
        focusTarget = playerTransform;
        isFocusing = true;
        focusTurnTimer = 0f;
        focusHoldTimer = 0f;
        focusStartAngles = new Vector2(headYaw, headPitch);
    }

    private void EndFocus()
    {
        isFocusing = false;
        focusTarget = null;
        if (alertTriggered)
        {
            return;
        }

        holdEntered = false;
        StopAlertRoutine(false);
        PickNewTarget();
    }

    private Vector2 GetAnglesToTarget(Transform target)
    {
        Vector3 toTarget = target.position - head.position;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return new Vector2(headYaw, headPitch);
        }

        Vector3 dirRef = Quaternion.Inverse(referenceBaseRotation) * toTarget.normalized;
        if (invertFocusDirection)
        {
            dirRef.x = -dirRef.x;
            dirRef.z = -dirRef.z;
        }
        float pitch = Mathf.Asin(Mathf.Clamp(dirRef.y, -1f, 1f)) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        float yaw = Mathf.Atan2(dirRef.x, dirRef.z) * Mathf.Rad2Deg;
        return new Vector2(yaw, pitch);
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

    private void CacheHatRenderers()
    {
        if (hat == null)
        {
            return;
        }

        hatRenderers = hat.GetComponentsInChildren<Renderer>(true);
        hatRendererBaseColors = new Color[hatRenderers.Length];
        for (int i = 0; i < hatRenderers.Length; i++)
        {
            Material mat = hatRenderers[i].material;
            hatRendererBaseColors[i] = mat != null ? mat.color : Color.white;
        }

        if (hatLight == null)
        {
            hatLight = hat.GetComponentInChildren<Light>(true);
        }

        if (hatLight != null)
        {
            hatLightBaseColor = hatLight.color;
            hatLightBaseEnabled = hatLight.enabled;
        }
    }

    private float GetTargetSpeed(Transform target)
    {
        Rigidbody targetRb = target.GetComponentInParent<Rigidbody>();
        if (targetRb == null)
        {
            targetRb = target.GetComponent<Rigidbody>();
        }

        return targetRb != null ? targetRb.linearVelocity.magnitude : 0f;
    }

    private bool IsTargetMovingTooFast()
    {
        return focusTarget != null && GetTargetSpeed(focusTarget) > playerSpeedThreshold;
    }

    private void UpdateTurnAlertState()
    {
        StopAlertRoutine(true);
        ApplyHatAlertState(true);
    }

    private void UpdateHoldAlertState(bool allowBlink)
    {
        if (alertTriggered)
        {
            return;
        }

        if (allowBlink && IsTargetMovingTooFast())
        {
            alertTriggered = true;
            
            // Play alert sound once
            if (audioSource != null && alertSound != null)
            {
                audioSource.PlayOneShot(alertSound);
            }
            
            StopAlertRoutine(true);
            alertRoutine = StartCoroutine(AlertAndEndRoutine());
        }
        else
        {
            StopAlertRoutine(true);
            ApplyHatAlertState(true);
        }
    }

    private IEnumerator AlertAndEndRoutine()
    {
        isBlinking = true;
        float elapsed = 0f;
        bool blinkOn = false;
        while (elapsed < alertBlinkDuration)
        {
            blinkOn = !blinkOn;
            ApplyHatAlertState(blinkOn);
            float wait = Mathf.Max(0.02f, alertBlinkInterval);
            yield return new WaitForSeconds(wait);
            elapsed += wait;
        }

        isBlinking = false;
        ApplyHatAlertState(true);
        LoadEndScene();
        alertRoutine = null;
    }

    private void ApplyHatAlertState(bool alertOn)
    {
        if (hatRenderers != null)
        {
            for (int i = 0; i < hatRenderers.Length; i++)
            {
                Material mat = hatRenderers[i].material;
                if (mat == null)
                {
                    continue;
                }
                mat.color = alertOn ? alertColor : hatRendererBaseColors[i];
            }
        }

        if (hatLight != null)
        {
            if (alertOn)
            {
                hatLight.color = alertColor;
                hatLight.enabled = true;
            }
            else
            {
                hatLight.color = hatLightBaseColor;
                hatLight.enabled = hatLightBaseEnabled;
            }
        }
    }

    private void StopAlertRoutine(bool keepRed)
    {
        if (alertRoutine != null)
        {
            StopCoroutine(alertRoutine);
            alertRoutine = null;
        }
        isBlinking = false;
        if (!keepRed)
        {
            ApplyHatAlertState(false);
        }
    }

    private void LoadEndScene()
    {
        SceneManager.LoadScene("LooseScreen");
    }

}
