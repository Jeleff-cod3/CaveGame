using UnityEngine;

public class MammothActionController : MonoBehaviour
{
    private MammothState state;
    private MammothSenses senses;
    private MammothMovement movement;
    private MammothCombat combat;

    private void Awake()
    {
        state = GetComponent<MammothState>();
        senses = GetComponent<MammothSenses>();
        movement = GetComponent<MammothMovement>();
        combat = GetComponent<MammothCombat>();
    }

    public void Execute(MammothActionType action)
    {
        Transform target = senses != null ? senses.Target : null;

        if (state != null && !state.CanStartNewAction())
        {
            return;
        }

        switch (action)
        {
            case MammothActionType.Idle:
                movement.Stop();
                state.SetAction(MammothActionType.Idle);
                break;

            case MammothActionType.Roam:
                movement.Roam();
                state.SetAction(MammothActionType.Roam);
                break;

            case MammothActionType.ChasePlayer:
                movement.Chase(target);
                state.SetAction(MammothActionType.ChasePlayer);
                break;

            case MammothActionType.RunAway:
                movement.RunAwayFrom(target);
                state.SetAction(MammothActionType.RunAway);
                break;

            case MammothActionType.Charge:
                movement.ChargeToward(target);
                combat.StartChargeDamageWindow(target);
                break;

            case MammothActionType.NormalAttack:
                movement.Stop();
                movement.FaceTarget(target);
                combat.StartNormalAttack(target);
                break;

            case MammothActionType.Stomp:
                movement.Stop();
                combat.StartStomp(target);
                break;

            case MammothActionType.TwistAttack:
                movement.Stop();
                combat.StartTwistAttack(target);
                break;

            case MammothActionType.Recover:
                movement.Stop();
                state.SetAction(MammothActionType.Recover);
                break;
        }
    }
}