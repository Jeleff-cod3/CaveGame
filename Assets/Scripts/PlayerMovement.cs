using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerControllerLoose : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;
    public float rotationSpeed = 5f; // slower rotation = looser feel
    public float movementSmooth = 0.1f; // how fast movement direction catches input
    public float jumpForce = 5f;

    private Rigidbody rb;
    private bool isGrounded = true;

    private Vector3 currentVelocity = Vector3.zero; // for smoothing movement
    private Vector3 moveDirection = Vector3.zero;
    private PlayerMouseAim mouseAim;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        mouseAim = GetComponent<PlayerMouseAim>();
    }

    void Update()
    {
        HandleMovement();
        HandleJump();
    }

    void HandleMovement()
    {
        if (mouseAim == null)
        {
            mouseAim = GetComponent<PlayerMouseAim>();
        }

        // Read WASD input
        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1;
        if (Keyboard.current.sKey.isPressed) input.y -= 1;
        if (Keyboard.current.dKey.isPressed) input.x += 1;
        if (Keyboard.current.aKey.isPressed) input.x -= 1;

        bool isAimLocked = mouseAim != null && mouseAim.IsAimModifierPressed;
        Vector3 targetDirection = new Vector3(input.x, 0f, input.y).normalized;

        if (isAimLocked)
        {
            targetDirection = Vector3.zero;
        }

        // Smoothly interpolate current move direction
        moveDirection = Vector3.SmoothDamp(moveDirection, targetDirection, ref currentVelocity, movementSmooth);

        // Move the player with physics
        Vector3 newPos = rb.position + moveDirection * speed * Time.deltaTime;
        rb.MovePosition(newPos);

        // Rotate toward movement smoothly if moving
        if (isAimLocked && mouseAim != null && mouseAim.TryGetAimDirection(out Vector3 aimDirection, false))
        {
            Quaternion targetRotation = Quaternion.LookRotation(aimDirection);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
        else if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    void HandleJump()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f)
            {
                isGrounded = true;
                break;
            }
        }
    }
}
