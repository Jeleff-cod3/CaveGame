using UnityEngine;

public class MammothState : MonoBehaviour
{
    [Header("Action State")]
    public MammothActionType currentAction = MammothActionType.Idle;
    public MammothActionType previousAction = MammothActionType.Idle;

    [Header("Busy Flags")]
    public bool isBusy;
    public bool isAttacking;
    public bool isCharging;
    public bool isRecovering;

    [Header("Memory")]
    public Transform currentTarget;
    public Vector3 lastKnownTargetPosition;
    public float lastActionChangeTime;
    public float lastDamageTime;

    public void SetAction(MammothActionType newAction)
    {
        if (currentAction == newAction)
        {
            return;
        }

        previousAction = currentAction;
        currentAction = newAction;
        lastActionChangeTime = Time.time;

        Debug.Log($"Mammoth action changed: {previousAction} -> {currentAction}");
    }

    public void SetTarget(Transform target)
    {
        currentTarget = target;

        if (target != null)
        {
            lastKnownTargetPosition = target.position;
        }
    }

    public void MarkDamaged()
    {
        lastDamageTime = Time.time;
    }

    public bool WasDamagedRecently(float recentTime)
    {
        return Time.time - lastDamageTime <= recentTime;
    }

    public bool CanStartNewAction()
    {
        return !isBusy && !isAttacking && !isCharging && !isRecovering;
    }
}