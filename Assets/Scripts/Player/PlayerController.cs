using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Variables
    [Header("Components")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform cameraObject;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0.1f, -1f);
    [SerializeField] private PlayerGraphicObject graphicObject;
    [SerializeField] private Blob blob;
    [SerializeField] private float colliderRadius = 0.6f;
    [SerializeField] private float growDuration = 2f;
    private SphereCollider collider;

    [Header("Movement Settings")]
    [SerializeField] private float MaxSpeed = 20f;
    [SerializeField] public float MoveSpeedMultiplyer = 5f;
    [SerializeField] private float movementDeadzone = 0.1f;
    [SerializeField] public float LookSensitivityMultiplyer = 1f;
    [SerializeField] private float RisingSinkingMultiplier = 1f;
    private Vector3 lastLinearVelocity;

    [SerializeField] public float BrakeStrength = 0.1f;
    private bool IsBraking = false;
    private bool IsScanning = false;
    private bool IsRising = false;
    private bool IsSinking = false;

    [Header("PlayerState Variables")]
    [SerializeField] public float CurrentEnergyAmount = 5f;
    [SerializeField] public float Size = 1f;
    [SerializeField] private float energyConsumptionMultiplyer = 1f;

    [Header("Graphic Object Settings")]
    [SerializeField] private float reorientationMultiplyer = 5f;
    private bool isReorienting = false;

    private InputSystem_Actions inputActions;
    private Vector2 moveInput;
    private Vector2 lookInput;
    #endregion

    #region Monobehaviour Methods
    private void Awake()
    {
        collider = GetComponent<SphereCollider>();

        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (blob == null)
        {
            blob = GetComponent<Blob>();
            if (blob == null)
            {
                blob = GetComponentInChildren<Blob>();
            }
        }

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        if (cameraObject == null && cameraPivot != null)
        {
            cameraObject = cameraPivot.GetChild(0);
        }

        inputActions = new InputSystem_Actions();
    }

    private void Start()
    {
        UpdateComponentSizes(Size);
    }

    private void OnEnable()
    {
        inputActions.Enable();

        inputActions.Player.Crouch.performed += ctx => IsBraking = true;
        inputActions.Player.Crouch.canceled += ctx => IsBraking = false;
        inputActions.Player.ScanMode.performed += ctx => IsScanning = true;
        inputActions.Player.ScanMode.canceled += ctx => IsScanning = false;
        inputActions.Player.Rise.performed += ctx => IsRising = true;
        inputActions.Player.Rise.canceled += ctx => IsRising = false;
        inputActions.Player.Sink.performed += ctx => IsSinking = true;
        inputActions.Player.Sink.canceled += ctx => IsSinking = false;

        inputActions.Player.Interact.performed += ctx => Interact();

    }

    private void OnDisable()
    {
        inputActions.Disable();
    }

    private void Update()
    {
        moveInput = inputActions.Player.Move.ReadValue<Vector2>();
        lookInput = inputActions.Player.Look.ReadValue<Vector2>();

        if (IsBraking)
        {
            Brake();
        }

    }
    private void FixedUpdate()
    {
        HandleMovementInput();
        HandleCameraInput();
    }
    #endregion

    #region Assimilation Methods

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.collider.CompareTag("Assimilateable"))
        {
            return;
        }

        Assimilateable assimilateable = collision.collider.GetComponentInParent<Assimilateable>();
        if (assimilateable == null)
        {
            return;
        }

        float blobVolume = GetBlobVolume();
        if (blobVolume <= assimilateable.Volume)
        {
            return;
        }

        HandleAssimilateCollision(collision.collider);
        GrowInSize(GetTargetSizeFromAssimilateable(assimilateable.Volume, blobVolume));
        Destroy(assimilateable.gameObject);
    }

    private void GrowInSize(float targetSize)
    {
        StartCoroutine(GrowInSizeCoroutine(targetSize, growDuration));
    }

    private float GetBlobVolume()
    {
        float scale = GetMaxAbsScale(transform.lossyScale);
        if (collider != null)
        {
            float radius = colliderRadius * Size * scale;
            if (radius > 0f)
            {
                return (4f / 3f) * Mathf.PI * radius * radius * radius;
            }
        }

        if (blob != null)
        {
            Renderer[] renderers = blob.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    combined.Encapsulate(renderers[i].bounds);
                }

                Vector3 size = combined.size;
                return Mathf.Abs(size.x * size.y * size.z);
            }
        }

        return 0f;
    }

    private float GetTargetSizeFromAssimilateable(float assimilateableVolume, float blobVolume)
    {
        float scale = GetMaxAbsScale(transform.lossyScale);
        float baseRadius = colliderRadius * scale;
        if (baseRadius <= 0f || blobVolume <= 0f)
        {
            return Size + assimilateableVolume;
        }

        float baseVolume = (4f / 3f) * Mathf.PI * baseRadius * baseRadius * baseRadius;
        float targetVolume = blobVolume + assimilateableVolume;
        float targetSize = Mathf.Pow(targetVolume / baseVolume, 1f / 3f);
        return Mathf.Max(Size, targetSize);
    }

    private float GetMaxAbsScale(Vector3 scale)
    {
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private System.Collections.IEnumerator GrowInSizeCoroutine(float targetSize, float duration)
    {
        float startSize = Size;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);
            
            // Smooth interpolation
            Size = Mathf.Lerp(startSize, targetSize, t);
            
            UpdateComponentSizes(Size);

            yield return null;
        }
        
        // Ensure we reach exact target size
        Size = targetSize;
        UpdateComponentSizes(Size);
    }

    private void UpdateComponentSizes(float newSize)
    {
        // Update the scale of the Blob
        if (blob != null)
        {
            blob.transform.localScale = Vector3.one * Size;
        }

        // Update the scale of the Collider
        if (collider != null)
        {
            collider.radius = colliderRadius * Size;
        }

        // Update Camera Distance
        if(cameraObject != null)
        {
            cameraObject.localPosition = cameraOffset * Size;
        }
    }

    private void HandleAssimilateCollision(Collider hitCollider)
    {
        if (blob == null || hitCollider == null)
        {
            return;
        }

        GameObject target = ResolveAssimilateTarget(hitCollider);
        if (target == null)
        {
            return;
        }

        blob.ScanObjectToGrid(target);
        blob.ApplyScanGridToMesh();
        float bestRotationDegrees;
        float score = blob.CompareToScanGrid(8, true, out bestRotationDegrees);
        Debug.Log($"Assimilate compare score: {score:F3} (best rotation {bestRotationDegrees:F1} deg) vs {target.name}");
    }

    private GameObject ResolveAssimilateTarget(Collider hitCollider)
    {
        MeshFilter meshFilter = hitCollider.GetComponentInParent<MeshFilter>();
        if (meshFilter != null)
        {
            return meshFilter.gameObject;
        }

        if (hitCollider.attachedRigidbody != null)
        {
            return hitCollider.attachedRigidbody.gameObject;
        }

        return hitCollider.gameObject;
    }
    #endregion

    #region Movement Methods

    private void HandleMovementInput()
    {
        if (rb == null) return;

        float dt = Time.fixedDeltaTime;

        Vector3 currentLinearVelocity = rb.linearVelocity;

        bool isMoving = false;
        float speed = currentLinearVelocity.magnitude;

        if (CurrentEnergyAmount > 0f)
        {
            Vector2 movement = new Vector3(moveInput.x, moveInput.y) * MoveSpeedMultiplyer;
            CurrentEnergyAmount -= movement.magnitude * energyConsumptionMultiplyer * dt;

            if (speed < movementDeadzone)
            {
                currentLinearVelocity = Vector3.zero;
                rb.linearVelocity = Vector3.zero;
            }

            if (speed != 0f)
            {
                isMoving = true;
            }

            float risingSinkingForce = IsRising && IsSinking ? 0f : IsRising ? RisingSinkingMultiplier : IsSinking ? -RisingSinkingMultiplier : 0f;

            // Berechne die gew�nschte Bewegungsrichtung
            Vector3 desiredDirection = (cameraPivot.transform.forward * movement.y + transform.right * movement.x + Vector3.up * risingSinkingForce * Size);
            
            if (desiredDirection.sqrMagnitude > 0.01f)
            {
                Vector3 normalizedDirection = desiredDirection.normalized;
                
                // Projiziere aktuelle Geschwindigkeit auf die gew�nschte Richtung
                float speedInDirection = Vector3.Dot(rb.linearVelocity, normalizedDirection);
                
                // Erlaube Kraft nur wenn Geschwindigkeit in dieser Richtung unter MaxSpeed ist
                if (speedInDirection < MaxSpeed * Size)
                {
                    rb.AddForce(desiredDirection, ForceMode.Force);
                }
            }

        }

        if (graphicObject != null)
        {
            Vector3 velocityChange = (lastLinearVelocity - currentLinearVelocity).normalized;
            graphicObject.Tilt(new Vector3(velocityChange.x, 1f, velocityChange.z), dt, reorientationMultiplyer);
        }
        lastLinearVelocity = currentLinearVelocity;
    }


    private void HandleCameraInput()
    {
        if (cameraPivot == null || IsScanning) return;

        float dt = Time.fixedDeltaTime;
        Quaternion originalRotation = rb != null ? rb.rotation : transform.rotation;

        // Horizontale Rotation (Spieler dreht sich um Y-Achse) via Rigidbody
        float yawDelta = lookInput.x * LookSensitivityMultiplyer * dt;
        if (rb != null)
        {
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
        }
        else
        {
            transform.Rotate(Vector3.up, yawDelta);
        }

        if (graphicObject != null)
        {
            graphicObject.RotateYaw(-yawDelta);
        }

        // Vertikale Rotation (Kamera neigt sich um X-Achse)
        Vector3 currentRotation = cameraPivot.localEulerAngles;
        float currentXRotation = currentRotation.x;
        
        // Konvertiere zu -180 bis 180 Bereich
        if (currentXRotation > 180f)
            currentXRotation -= 360f;

        // Berechne neue X-Rotation und begrenze sie
        float newXRotation = currentXRotation - lookInput.y * LookSensitivityMultiplyer * dt;
        newXRotation = Mathf.Clamp(newXRotation, -80f, 80f);

        // Wende die neue Rotation an
        cameraPivot.localEulerAngles = new Vector3(newXRotation, 0f, 0f);
    }

    private void Brake()
    {
        if (rb == null) return;
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, BrakeStrength * Time.deltaTime);
        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, BrakeStrength * Time.deltaTime);
    }
    #endregion

    #region Interaction Methods
    private void Interact()
    {
        if(Physics.SphereCast(transform.position, Size, transform.forward, out RaycastHit hitInfo, 2f))
        {
            switch (hitInfo.collider.tag)
            {
                case "Collectible":
                    HandleCollectibleInteraction(hitInfo.collider.gameObject);
                    break;
                case "Enemy":
                    HandleEnemyInteraction(hitInfo.collider.gameObject);
                    break;
                case "Item":
                    HandleItemInteraction(hitInfo.collider.gameObject);
                    break;
                default:
                    Debug.Log("No valid interaction found.");
                    break;
            }
        }
    }

    private void HandleCollectibleInteraction(GameObject collectible)
    {
        // Energie vom Collectible aufnehmen
        Debug.Log($"Collected: {collectible.name}");
        // Beispiel: CurrentEnergyAmount += 10f;
        Destroy(collectible);
    }

    private void HandleEnemyInteraction(GameObject enemy)
    {
        // Interaktion mit Gegner
        Debug.Log($"Interacting with enemy: {enemy.name}");
        // Beispiel: CurrentEnergyAmount -= 5f;
    }

    private void HandleItemInteraction(GameObject item)
    {
        // Interaktion mit Item
        Debug.Log($"Picked up item: {item.name}");
        // Beispiel: Item ins Inventar aufnehmen
    }
    #endregion
}
