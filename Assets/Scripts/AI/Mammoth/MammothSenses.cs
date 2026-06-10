using UnityEngine;

public class MammothSenses : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string playerTag = "Player";

    [Header("Ranges")]
    [SerializeField] private float detectionRange = 35f;
    [SerializeField] private float chaseRange = 25f;
    [SerializeField] private float chargeRange = 16f;
    [SerializeField] private float normalAttackRange = 4f;
    [SerializeField] private float stompRange = 3f;
    [SerializeField] private float twistAttackRange = 4.5f;

    [Header("Vision")]
    [SerializeField] private float fieldOfView = 150f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    public Transform Target => target;
    public float DistanceToTarget { get; private set; }
    public Vector3 DirectionToTarget { get; private set; }
    public bool HasTarget => target != null;
    public bool CanSeeTarget { get; private set; }
    public bool IsTargetDetected { get; private set; }
    public bool IsTargetInChaseRange { get; private set; }
    public bool IsTargetInChargeRange { get; private set; }
    public bool IsTargetInNormalAttackRange { get; private set; }
    public bool IsTargetInStompRange { get; private set; }
    public bool IsTargetInTwistAttackRange { get; private set; }
    public bool IsTargetBehind { get; private set; }

    private void Update()
    {
        if (target == null)
        {
            FindTarget();
        }

        UpdateSenses();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void FindTarget()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
        {
            target = playerObject.transform;
            return;
        }

        PlayerHealth playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (playerHealth != null)
        {
            target = playerHealth.transform;
        }
    }

    private void UpdateSenses()
    {
        if (target == null)
        {
            DistanceToTarget = float.MaxValue;
            DirectionToTarget = Vector3.zero;
            CanSeeTarget = false;
            IsTargetDetected = false;
            IsTargetInChaseRange = false;
            IsTargetInChargeRange = false;
            IsTargetInNormalAttackRange = false;
            IsTargetInStompRange = false;
            IsTargetInTwistAttackRange = false;
            IsTargetBehind = false;
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        DistanceToTarget = toTarget.magnitude;
        DirectionToTarget = DistanceToTarget > 0.001f ? toTarget.normalized : transform.forward;

        float angle = Vector3.Angle(transform.forward, DirectionToTarget);

        IsTargetBehind = angle > 110f;
        CanSeeTarget = DistanceToTarget <= detectionRange && angle <= fieldOfView * 0.5f && HasLineOfSight();
        IsTargetDetected = CanSeeTarget || DistanceToTarget <= normalAttackRange;

        IsTargetInChaseRange = DistanceToTarget <= chaseRange;
        IsTargetInChargeRange = DistanceToTarget <= chargeRange && DistanceToTarget > normalAttackRange;
        IsTargetInNormalAttackRange = DistanceToTarget <= normalAttackRange;
        IsTargetInStompRange = DistanceToTarget <= stompRange;
        IsTargetInTwistAttackRange = DistanceToTarget <= twistAttackRange;
    }

    private bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 destination = target.position + Vector3.up * 0.8f;
        Vector3 direction = destination - origin;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, direction.magnitude, lineOfSightMask, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == target || hit.transform.IsChildOf(target);
        }

        return true;
    }
}