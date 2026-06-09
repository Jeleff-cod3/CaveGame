using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    private PlayerWeaponPickup weaponPickup;
    private Transform attackPoint;
    private LayerMask enemyLayer;

    private float nextAttackTime;

    public void Initialize(PlayerWeaponPickup pickup, Transform point, LayerMask layer)
    {
        weaponPickup = pickup;
        attackPoint = point;
        enemyLayer = layer;
    }

    private void Update()
    {
        if (Mouse.current == null)
        {
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryMeleeAttack();
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            TryThrowWeapon();
        }
    }

    private void TryMeleeAttack()
    {
        if (weaponPickup == null || !weaponPickup.HasWeapon)
        {
            Debug.Log("No weapon equipped.");
            return;
        }

        PickupableWeapon weapon = weaponPickup.EquippedWeapon;

        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + weapon.AttackCooldown;

        weapon.StartMeleeAttack();

        Debug.Log("Spear attack started. Damage now depends on spear tip collision.");
    }

    private void TryThrowWeapon()
    {
        if (weaponPickup == null || !weaponPickup.HasWeapon)
        {
            return;
        }

        Vector3 throwDirection = transform.forward;
        weaponPickup.ThrowEquippedWeapon(throwDirection);

        Debug.Log("Spear thrown.");
    }
}