using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Structs
    struct RigidbodyVelocity
    {
        public Vector3 Linear;
        public Vector3 Angular;
    }

    #endregion

    [Header("Components")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private PlayerGraphicObject graphicObject;

    [Header("Movement Settings")]
    [SerializeField] private float MaxSpeed = 20f;
    [SerializeField] public float MoveSpeedMultiplyer = 5f;
    [SerializeField] public float LookSensitivityMultiplyer = 1f;

    [SerializeField] public float BrakeStrength = 0.1f;
    private bool IsBraking = false;

    [Header("PlayerState Variables")]
    [SerializeField] public float CurrentEnergyAmount = 5f;
    [SerializeField] public float Size = 1f;
    [SerializeField] private float energyConsumptionMultiplyer = 1f;

    [Header("Graphic Object Settings")]
    [SerializeField] private float reorientationMultiplyer = 5f;

    private InputSystem_Actions inputActions;
    private Vector2 moveInput;
    private Vector2 lookInput;

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        inputActions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        inputActions.Enable();

        inputActions.Player.Crouch.performed += ctx => IsBraking = true;
        inputActions.Player.Crouch.canceled += ctx => IsBraking = false;

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

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("Assimilateable"))
        {
            Debug.Log("Assimilate");
        }
    }

    private void HandleMovementInput()
    {
        if (rb == null) return;

        float dt = Time.fixedDeltaTime;

        RigidbodyVelocity rigidbodyVelocity = new RigidbodyVelocity 
        { 
            Linear = rb.linearVelocity, 
            Angular = rb.angularVelocity 
        };


        bool isMoving = false;
        if (CurrentEnergyAmount > 0f)
        {
            Vector2 movement = new Vector3(moveInput.x, moveInput.y) * MoveSpeedMultiplyer;
            CurrentEnergyAmount -= movement.magnitude * energyConsumptionMultiplyer * dt;

            if (rigidbodyVelocity.Linear.magnitude != 0f)
            {
                isMoving = true;
            }

            if (rigidbodyVelocity.Linear.magnitude < MaxSpeed * Size)
            {
                rb.AddForce(cameraPivot.transform.forward * movement.y + transform.right * movement.x, ForceMode.Force);
            }
        }

        if (graphicObject != null)
        {
            if (cameraPivot == null || !isMoving) return;
            //graphicObject.Reorient(cameraPivot.rotation, Time.deltaTime, rb.linearVelocity.magnitude * reorientationMultiplyer);
        }
    }

    private void HandleCameraInput()
    {
        if (cameraPivot == null) return;

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

        if(graphicObject != null)
        {
            graphicObject.Rotate(originalRotation);
        }
    }

    private void FixedUpdate()
    {
        HandleMovementInput();
        HandleCameraInput();
    }

    private void Brake()
    {
        if (rb == null) return;
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, BrakeStrength * Time.deltaTime);
        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, BrakeStrength * Time.deltaTime);
    }

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
}
