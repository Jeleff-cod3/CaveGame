using UnityEngine;

public class MammothBrain : MonoBehaviour
{
    [Header("Decision Settings")]
    [SerializeField] private float decisionInterval = 0.35f;
    [SerializeField] private float recentDamageMemoryTime = 3f;

    private MammothState state;
    private MammothPersonality personality;
    private MammothSenses senses;
    private MammothCombat combat;
    private MammothActionController actionController;
    private EnemyHealth health;

    private float nextDecisionTime;

    private void Awake()
    {
        state = GetComponent<MammothState>();
        personality = GetComponent<MammothPersonality>();
        senses = GetComponent<MammothSenses>();
        combat = GetComponent<MammothCombat>();
        actionController = GetComponent<MammothActionController>();
        health = GetComponent<EnemyHealth>();
    }

    private void Update()
    {
        if (Time.time < nextDecisionTime)
        {
            return;
        }

        nextDecisionTime = Time.time + decisionInterval;

        if (state != null && !state.CanStartNewAction())
        {
            return;
        }

        MammothActionType chosenAction = ChooseAction();
        actionController.Execute(chosenAction);
    }

    private MammothActionType ChooseAction()
    {
        if (senses == null || !senses.HasTarget || !senses.IsTargetDetected)
        {
            return Random.value < personality.curiosity ? MammothActionType.Roam : MammothActionType.Idle;
        }

        if (state != null && senses.Target != null)
        {
            state.SetTarget(senses.Target);
        }

        float healthPercent = GetHealthPercent();
        float fightDrive = personality != null ? personality.GetFightDrive() : 0.5f;
        float flightDrive = personality != null ? personality.GetFlightDrive() : 0.5f;

        bool lowHealth = healthPercent <= personality.panicHealthThreshold;
        bool damagedRecently = state != null && state.WasDamagedRecently(recentDamageMemoryTime);

        if (damagedRecently)
        {
            personality.AddAnger(0.08f);
            personality.AddFear(0.04f);
        }

        if (lowHealth && flightDrive > fightDrive)
        {
            return MammothActionType.RunAway;
        }

        if (senses.IsTargetBehind && senses.IsTargetInTwistAttackRange && combat.CanTwistAttack)
        {
            return MammothActionType.TwistAttack;
        }

        if (senses.IsTargetInStompRange && combat.CanStomp)
        {
            return MammothActionType.Stomp;
        }

        if (senses.IsTargetInNormalAttackRange && combat.CanNormalAttack)
        {
            return MammothActionType.NormalAttack;
        }

        if (senses.IsTargetInChargeRange && combat.CanCharge && fightDrive > 0.45f)
        {
            return MammothActionType.Charge;
        }

        if (senses.IsTargetInChaseRange && fightDrive >= flightDrive)
        {
            return MammothActionType.ChasePlayer;
        }

        if (flightDrive > fightDrive + 0.2f)
        {
            return MammothActionType.RunAway;
        }

        return MammothActionType.ChasePlayer;
    }
        private float GetHealthPercent()
    {
        if (health == null)
        {
            return 1f;
        }

        return health.HealthPercent;
    }
}