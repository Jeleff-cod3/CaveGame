using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
public class NavMeshWASDMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float rotationSpeed = 14f;
    public float destinationDistance = 1.8f;
    public float destinationUpdateRate = 0.03f;

    [Header("Input")]
    public bool useCameraRelativeMovement = false;
    public Transform cameraTransform;

    [Header("NavMesh")]
    public float navMeshSampleDistance = 2f;
    public int areaMask = NavMesh.AllAreas;

    private NavMeshAgent agent;
    private Vector3 lastDestination;
    private float nextDestinationUpdateTime;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        agent.speed = moveSpeed;
        agent.angularSpeed = 720f;
        agent.acceleration = 40f;
        agent.stoppingDistance = 0f;
        agent.autoBraking = false;

        agent.updateRotation = false;
        agent.isStopped = false;
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
            StopAgent();
            return;
        }

        RotateTowards(inputDirection);

        if (Time.time < nextDestinationUpdateTime)
        {
            return;
        }

        nextDestinationUpdateTime = Time.time + destinationUpdateRate;

        Vector3 wantedDestination = transform.position + inputDirection * destinationDistance;

        if (!NavMesh.SamplePosition(wantedDestination, out NavMeshHit navHit, navMeshSampleDistance, areaMask))
        {
            StopAgent();
            return;
        }

        if ((navHit.position - lastDestination).sqrMagnitude < 0.03f)
        {
            return;
        }

        lastDestination = navHit.position;

        agent.isStopped = false;
        agent.SetDestination(navHit.position);
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
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            forward.y = 0f;
            right.y = 0f;

            forward.Normalize();
            right.Normalize();

            return (forward * rawInput.z + right * rawInput.x).normalized;
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
    }
}