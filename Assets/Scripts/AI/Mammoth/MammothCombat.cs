using System.Collections;
using UnityEngine;

public class MammothCombat : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] private int normalAttackDamage = 15;
    [SerializeField] private int stompDamage = 25;
    [SerializeField] private int twistAttackDamage = 20;
    [SerializeField] private int chargeDamage = 35;

    [Header("Ranges")]
    [SerializeField] private float normalAttackRadius = 4f;
    [SerializeField] private float stompRadius = 3.2f;
    [SerializeField] private float twistAttackRadius = 4.5f;
    [SerializeField] private float chargeHitRadius = 3.5f;

    [Header("Timings")]
    [SerializeField] private float normalAttackDuration = 0.8f;
    [SerializeField] private float stompDuration = 1.1f;
    [SerializeField] private float twistDuration = 1f;
    [SerializeField] private float chargeDamageDelay = 0.4f;

    [Header("Cooldowns")]
    [SerializeField] private float normalAttackCooldown = 1.5f;
    [SerializeField] private float stompCooldown = 3f;
    [SerializeField] private float twistCooldown = 4f;
    [SerializeField] private float chargeCooldown = 5f;

    [Header("Hit Detection")]
    [SerializeField] private LayerMask playerLayerMask = ~0;

    private MammothState state;
    private float nextNormalAttackTime;
    private float nextStompTime;
    private float nextTwistTime;
    private float nextChargeTime;

    public bool CanNormalAttack => Time.time >= nextNormalAttackTime;
    public bool CanStomp => Time.time >= nextStompTime;
    public bool CanTwistAttack => Time.time >= nextTwistTime;
    public bool CanCharge => Time.time >= nextChargeTime;

    private void Awake()
    {
        state = GetComponent<MammothState>();
    }

    public void StartNormalAttack(Transform target)
    {
        if (!CanNormalAttack)
        {
            return;
        }

        StartCoroutine(AttackRoutine(
            MammothActionType.NormalAttack,
            target,
            normalAttackDamage,
            normalAttackRadius,
            normalAttackDuration,
            () => nextNormalAttackTime = Time.time + normalAttackCooldown
        ));
    }

    public void StartStomp(Transform target)
    {
        if (!CanStomp)
        {
            return;
        }

        StartCoroutine(AttackRoutine(
            MammothActionType.Stomp,
            target,
            stompDamage,
            stompRadius,
            stompDuration,
            () => nextStompTime = Time.time + stompCooldown
        ));
    }

    public void StartTwistAttack(Transform target)
    {
        if (!CanTwistAttack)
        {
            return;
        }

        StartCoroutine(AttackRoutine(
            MammothActionType.TwistAttack,
            target,
            twistAttackDamage,
            twistAttackRadius,
            twistDuration,
            () => nextTwistTime = Time.time + twistCooldown
        ));
    }

    public void StartChargeDamageWindow(Transform target)
    {
        if (!CanCharge)
        {
            return;
        }

        StartCoroutine(ChargeDamageRoutine(target));
    }

    private IEnumerator AttackRoutine(
        MammothActionType actionType,
        Transform target,
        int damage,
        float radius,
        float duration,
        System.Action setCooldown)
    {
        if (state != null)
        {
            state.isBusy = true;
            state.isAttacking = true;
            state.SetAction(actionType);
        }

        Debug.Log($"Mammoth started {actionType}.");

        yield return new WaitForSeconds(duration * 0.45f);

        TryDamageTarget(target, damage, radius, actionType.ToString());

        yield return new WaitForSeconds(duration * 0.55f);

        setCooldown?.Invoke();

        if (state != null)
        {
            state.isAttacking = false;
            state.isBusy = false;
        }

        Debug.Log($"Mammoth finished {actionType}.");
    }

    private IEnumerator ChargeDamageRoutine(Transform target)
    {
        if (state != null)
        {
            state.isCharging = true;
            state.isBusy = true;
            state.SetAction(MammothActionType.Charge);
        }

        nextChargeTime = Time.time + chargeCooldown;

        yield return new WaitForSeconds(chargeDamageDelay);

        TryDamageTarget(target, chargeDamage, chargeHitRadius, "Charge");

        yield return new WaitForSeconds(0.8f);

        if (state != null)
        {
            state.isCharging = false;
            state.isBusy = false;
        }
    }

    private void TryDamageTarget(Transform target, int damage, float radius, string attackName)
    {
        if (target == null)
        {
            return;
        }

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            radius,
            playerLayerMask,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            PlayerHealth playerHealth = hit.GetComponent<PlayerHealth>();

            if (playerHealth == null)
            {
                playerHealth = hit.GetComponentInParent<PlayerHealth>();
            }

            if (playerHealth == null)
            {
                continue;
            }

            playerHealth.TakeDamage(damage);
            Debug.Log($"Mammoth {attackName} hit player for {damage} damage.");
            return;
        }
    }
}