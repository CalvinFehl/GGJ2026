using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
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

    [Header("Movement Settings")]
    [SerializeField] public float MoveSpeedMultiplyer = 5f;
    [SerializeField] public float LookSensitivityMultiplyer = 1f;

    [SerializeField] public float BrakeStrength = 0.1f;
    private bool IsBraking = false;

    [Header("PlayerState Variables")]
    [SerializeField] public float CurrentEnergyAmount = 5f;
    [SerializeField] public float Size = 1f;
    [SerializeField] private float energyConsumptionMultiplyer = 1f;

    private InputSystem_Actions inputActions;

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        inputActions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        inputActions.Enable();

        inputActions.Player.Crouch.performed += ctx => Brake();
        inputActions.Player.Crouch.canceled += ctx => Brake();
        inputActions.Player.Interact.performed += ctx => Interact();

    }

    private void OnDisable()
    {
        inputActions.Disable();
    }

    private void Update()
    {
        HandleMovementInput();
        HandleCameraInput();
    }

    private void HandleMovementInput()
    {
        if (rb == null) return;

        Vector2 moveInput = inputActions.Player.Move.ReadValue<Vector2>();
        
        if (CurrentEnergyAmount > 0f)
        {
            Vector3 movement = new Vector3(moveInput.x, 0f, moveInput.y) * MoveSpeedMultiplyer;
            CurrentEnergyAmount -= movement.magnitude * energyConsumptionMultiplyer * Time.deltaTime;

            rb.AddForce(transform.forward * movement.y + transform.right * movement.x, ForceMode.Force);
        }
    }

    private void HandleCameraInput()
    {
        Vector2 cameraInput = inputActions.Player.Look.ReadValue<Vector2>();
        
    }

    private void Brake()
    {
        if (rb == null) return;
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, BrakeStrength);
        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, BrakeStrength);
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
