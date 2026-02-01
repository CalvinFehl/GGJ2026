using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    public static event System.Action<Transform> PlayerTransformed;
    #region Variables
    [Header("Components")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform cameraObject;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0.1f, -1f);
    [SerializeField] private float cameraCollisionRadius = 0.04f;
    [SerializeField] private float cameraCollisionBuffer = 0.01f;
    [SerializeField] private LayerMask cameraCollisionMask = ~0;
    [SerializeField] private PlayerGraphicObject graphicObject;
    [SerializeField] private Blob blob;
    [SerializeField] private float colliderRadius = 0.6f;
    [SerializeField] private bool matchColliderToMesh = true;
    [SerializeField] private float colliderRadiusMultiplier = 1f;
    [SerializeField] private float minColliderRadius = 0.05f;
    [SerializeField] private float growDuration = 2f;
    private float assimilateSimilarityThreshold = 0.7f;
    private SphereCollider collider;
    private Vector3 desiredCameraLocalPosition;

    [Header("Movement Settings")]
    [SerializeField] private float MaxSpeed = 20f;
    [SerializeField] public float MoveSpeedMultiplyer = 5f;
    [SerializeField] private float movementDeadzone = 0.1f;
    [SerializeField] public float LookSensitivityMultiplyer = 1f;
    [SerializeField] private float RisingSinkingMultiplier = 1f;
    private Vector3 lastLinearVelocity;

    [SerializeField] public float BrakeStrength = 0.1f;
    private bool IsBraking = false;
    private bool IsMorphing = false;
    private bool IsRising = false;
    private bool IsSinking = false;

    [Header("PlayerState Variables")]
    [SerializeField] public float CurrentEnergyAmount = 5f;
    [SerializeField] public float Size = 1f;
    [SerializeField] private float energyConsumptionMultiplyer = 1f;
    [SerializeField] private float growthDamping = 0.2f;
    private float winSizeThreshold = 0.8f;
    private bool winTriggered;

    [Header("Graphic Object Settings")]
    [SerializeField] private float reorientationMultiplyer = 5f;
    private bool isReorienting = false;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip engineStartSound;
    [SerializeField] private AudioClip assimilateSound;
    [SerializeField] private AudioClip growSound;
    [SerializeField] private AudioClip morphSound;

    private InputSystem_Actions inputActions;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private readonly System.Collections.Generic.HashSet<int> alertedAssimilateColliders =
        new System.Collections.Generic.HashSet<int>();


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

        inputActions.Player.Morph.performed += ctx => IsMorphing = true;
        inputActions.Player.Morph.canceled += ctx => IsMorphing = false;
        inputActions.Player.Brake.performed += ctx => IsBraking = true;
        inputActions.Player.Brake.canceled += ctx => IsBraking = false;
        inputActions.Player.Rise.performed += ctx => IsRising = true;
        inputActions.Player.Rise.canceled += ctx => IsRising = false;
        inputActions.Player.Sink.performed += ctx => IsSinking = true;
        inputActions.Player.Sink.canceled += ctx => IsSinking = false;

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
    private void LateUpdate()
    {
        UpdateCameraObstruction();
        if (matchColliderToMesh)
        {
            UpdateColliderRadiusFromMesh();
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
        TryHandleAssimilateCollision(collision.collider);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryHandleAssimilateCollision(collision.collider);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.collider == null)
        {
            return;
        }

        alertedAssimilateColliders.Remove(collision.collider.GetInstanceID());
    }

    private void TryHandleAssimilateCollision(Collider collider)
    {
        if (collider == null || !collider.CompareTag("Assimilateable"))
        {
            return;
        }

        int colliderId = collider.GetInstanceID();
        if (alertedAssimilateColliders.Contains(colliderId))
        {
            return;
        }

        alertedAssimilateColliders.Add(colliderId);

        Assimilateable assimilateable = collider.GetComponentInParent<Assimilateable>();
        if (assimilateable == null)
        {
            return;
        }

        float blobVolume = GetBlobVolume();
        if (blobVolume <= assimilateable.Volume)
        {
            return;
        }

        PlayerTransformed?.Invoke(transform);

        if (HandleAssimilateCollision(collider))
        {
            float targetSize = GetTargetSizeFromAssimilateable(assimilateable.Volume, blobVolume);
            Debug.Log($"New size: {targetSize:0.###}");
            GrowInSize(targetSize);
            Destroy(assimilateable.gameObject);
            PlaySoundOneShot(assimilateSound);
        }
    }

    private void GrowInSize(float targetSize)
    {
        StartCoroutine(GrowInSizeCoroutine(targetSize, growDuration));
        PlaySoundOneShot(growSound);
    }

    private float GetBlobVolume()
    {
        float scale = GetMaxAbsScale(transform.lossyScale);
        float radius = colliderRadius * Size * scale;
        if (radius > 0f)
        {
            return (4f / 3f) * Mathf.PI * radius * radius * radius * 1.0f;
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
            float dampedVolume = GetDampedAssimilationVolume(assimilateableVolume);
            return Size + dampedVolume;
        }

        float baseVolume = (4f / 3f) * Mathf.PI * baseRadius * baseRadius * baseRadius;
        float targetVolume = blobVolume + GetDampedAssimilationVolume(assimilateableVolume * 0.5f);
        float targetSize = Mathf.Pow(targetVolume / baseVolume, 1f / 3f);
        return Mathf.Max(Size, targetSize);
    }

    private float GetDampedAssimilationVolume(float assimilateableVolume)
    {
        if (assimilateableVolume <= 0f)
        {
            return 0f;
        }

        float damping = 1f / (1f + Mathf.Max(0f, growthDamping) * Size);
        return assimilateableVolume * damping;
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
            CheckWinCondition();

            yield return null;
        }
        
        // Ensure we reach exact target size
        Size = targetSize;
        UpdateComponentSizes(Size);
        CheckWinCondition();
    }

    private void CheckWinCondition()
    {
        if (winTriggered)
        {
            return;
        }

        if (Size > winSizeThreshold)
        {
            winTriggered = true;
            SceneManager.LoadScene("WinScreen");
        }
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
            float targetRadius = colliderRadius * Size;
            if (matchColliderToMesh)
            {
                targetRadius = GetTargetRadiusFromMesh(targetRadius);
            }

            collider.radius = Mathf.Max(minColliderRadius, targetRadius);
        }

        // Update Camera Distance
        if(cameraObject != null)
        {
            desiredCameraLocalPosition = cameraOffset * Size;
            cameraObject.localPosition = desiredCameraLocalPosition;
        }
    }

    private void UpdateColliderRadiusFromMesh()
    {
        if (collider == null || blob == null)
        {
            return;
        }

        float targetRadius = GetTargetRadiusFromMesh(colliderRadius * Size);
        collider.radius = Mathf.Max(minColliderRadius, targetRadius);
    }

    private float GetTargetRadiusFromMesh(float fallbackRadius)
    {
        float meshRadiusWorld = GetBlobMeshRadiusWorld();
        float scale = GetMaxAbsScale(transform.lossyScale);
        if (meshRadiusWorld > 0f && scale > 0f)
        {
            return (meshRadiusWorld / scale) * colliderRadiusMultiplier;
        }

        return fallbackRadius;
    }

    private float GetBlobMeshRadiusWorld()
    {
        if (blob == null)
        {
            return 0f;
        }

        Renderer[] renderers = blob.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return 0f;
        }

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        Vector3 extents = combined.extents;
        return Mathf.Max(extents.x, extents.y, extents.z);
    }

    private void UpdateCameraObstruction()
    {
        if (cameraPivot == null || cameraObject == null)
        {
            return;
        }

        Vector3 pivotPosition = cameraPivot.position;
        Vector3 desiredWorldPosition = cameraPivot.TransformPoint(desiredCameraLocalPosition);
        Vector3 toDesired = desiredWorldPosition - pivotPosition;
        float distance = toDesired.magnitude;
        if (distance <= 0.001f)
        {
            cameraObject.position = desiredWorldPosition;
            return;
        }

        Vector3 direction = toDesired / distance;
        float minDistanceFromPlayer = 0f;
        if (collider != null)
        {
            minDistanceFromPlayer = collider.radius * GetMaxAbsScale(transform.lossyScale);
        }

        float clampedDesiredDistance = Mathf.Max(distance, minDistanceFromPlayer);
        float castDistance = Mathf.Max(0f, clampedDesiredDistance);
        Vector3 castOrigin = pivotPosition;

        RaycastHit[] hits = Physics.RaycastAll(
            castOrigin,
            direction,
            castDistance,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        RaycastHit closestHit = default;
        bool foundHit = false;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            // Ignore player colliders (self or children).
            if (hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hits[i].distance < closestDistance)
            {
                closestDistance = hits[i].distance;
                closestHit = hits[i];
                foundHit = true;
            }
        }

        if (foundHit)
        {
            float safeDistance = Mathf.Max(minDistanceFromPlayer, closestHit.distance - cameraCollisionBuffer);
            cameraObject.position = pivotPosition + direction * safeDistance;
            return;
        }

        cameraObject.position = pivotPosition + direction * clampedDesiredDistance;
    }

    private bool HandleAssimilateCollision(Collider hitCollider)
    {
        if (blob == null || hitCollider == null)
        {
            return false;
        }

        GameObject target = ResolveAssimilateTarget(hitCollider);
        if (target == null)
        {
            return false;
        }

        blob.ScanObjectToGrid(target);
        float bestRotationDegrees;
        float score = blob.CompareToScanGrid(8, true, out bestRotationDegrees);
        Debug.Log($"Assimilate compare score: {score:F3} (best rotation {bestRotationDegrees:F1} deg) vs {target.name}");
        if (score < assimilateSimilarityThreshold)
        {
            return false;
        }

        blob.ApplyScanColorsToGrid();
        blob.RebuildMesh();
        return true;
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
        float speed = currentLinearVelocity.magnitude;


        if (speed < movementDeadzone)
        {
            currentLinearVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
        }
        else if (lastLinearVelocity == Vector3.zero)
        {
            PlaySoundOneShot(engineStartSound);
        }

        // Add movement force
        if (CurrentEnergyAmount > 0f)
        {
            Vector2 movement = new Vector3(moveInput.x, moveInput.y) * MoveSpeedMultiplyer;
            CurrentEnergyAmount -= movement.magnitude * energyConsumptionMultiplyer * dt;

            float risingSinkingForce = IsRising && IsSinking ? 0f : IsRising ? RisingSinkingMultiplier : IsSinking ? -RisingSinkingMultiplier : 0f;

            // Berechne die gew�nsste Bewegungsrichtung
            Vector3 desiredDirection = (cameraPivot.transform.forward * movement.y + transform.right * movement.x + Vector3.up * risingSinkingForce);

            if (desiredDirection.sqrMagnitude > 0.01f)
            {
                Vector3 normalizedDirection = desiredDirection.normalized;

                // Projiziere aktuelle Geschwindigkeit auf die gewollte Richtung
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
        if (cameraPivot == null || IsMorphing || (blob != null && blob.IsScanlineActive)) return;

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

    private void PlaySound(AudioClip clip)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        // Spiele Sound nur ab wenn gerade kein anderer läuft oder ersetze den aktuellen
        if (!audioSource.isPlaying)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }
        else if (audioSource.clip != clip)
        {
            // Stoppe aktuellen Sound und spiele neuen ab
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.Play();
        }
    }

    // Optional: Eine einfachere Variante die den Sound immer abspielt
    private void PlaySoundOneShot(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }
    #endregion
}
