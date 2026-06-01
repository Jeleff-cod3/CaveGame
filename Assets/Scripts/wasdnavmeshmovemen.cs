using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
public class NavMeshWASDMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float rotationSpeed = 14f;

    [Header("Input")]
    public bool useCameraRelativeMovement = false;
    public Transform cameraTransform;

    [Header("Conflict Handling")]
    public bool disablePlayerControllerLoose = true;
    public bool forceRigidbodyKinematic = true;

    private NavMeshAgent agent;
    private Rigidbody rb;
    private bool isIdle;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

        ResolveMovementConflicts();

        agent.speed = moveSpeed;
        agent.angularSpeed = 720f;
        agent.acceleration = 40f;
        agent.stoppingDistance = 0f;
        agent.autoBraking = false;

        agent.updateRotation = false;
        agent.isStopped = false;
    }

    private void ResolveMovementConflicts()
    {
        if (disablePlayerControllerLoose)
        {
            PlayerControllerLoose looseController = GetComponent<PlayerControllerLoose>();
            if (looseController != null && looseController.enabled)
            {
                looseController.enabled = false;
                Debug.LogWarning("Disabled PlayerControllerLoose on player because NavMeshWASDMovement is active.");
            }
        }

        if (forceRigidbodyKinematic && rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled)
        {
            return;
        }

        if (!agent.isOnNavMesh)
        {
            return;
        }

        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector3 inputDirection = GetInputDirection();

        if (inputDirection.sqrMagnitude < 0.001f)
        {
            EnterIdleState();
            return;
        }

        ExitIdleState();
        RotateTowards(inputDirection);
        agent.Move(inputDirection * moveSpeed * Time.deltaTime);
    }

    private Vector3 GetInputDirection()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return Vector3.zero;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            horizontal -= 1f;
        }

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            horizontal += 1f;
        }

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            vertical += 1f;
        }

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            vertical -= 1f;
        }

        Vector3 rawInput = new Vector3(horizontal, 0f, vertical).normalized;

        if (rawInput.sqrMagnitude < 0.001f)
        {
            return Vector3.zero;
        }

        if (!useCameraRelativeMovement)
        {
            return rawInput;
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform == null)
        {
            return rawInput;
        }

        Vector3 cameraForward = cameraTransform.forward;
        Vector3 cameraRight = cameraTransform.right;

        cameraForward.y = 0f;
        cameraRight.y = 0f;

        cameraForward.Normalize();
        cameraRight.Normalize();

        return (cameraForward * rawInput.z + cameraRight * rawInput.x).normalized;
    }

    private void RotateTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private void StopAgent()
    {
        if (!agent.isOnNavMesh)
        {
            return;
        }

        agent.ResetPath();
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    private void EnterIdleState()
    {
        if (!agent.isOnNavMesh)
        {
            return;
        }

        if (!isIdle)
        {
            StopAgent();
            // Keep internal NavMeshAgent simulation anchored to the exact current transform.
            // This prevents subtle drift/jitter when no input is held.
            agent.nextPosition = transform.position;
            isIdle = true;
            return;
        }

        // If something external nudged agent simulation while idle, hard-snap it back.
        if ((agent.nextPosition - transform.position).sqrMagnitude > 0.0004f)
        {
            agent.Warp(transform.position);
        }
    }

    private void ExitIdleState()
    {
        if (!isIdle)
        {
            return;
        }

        isIdle = false;
        agent.isStopped = false;
    }
}