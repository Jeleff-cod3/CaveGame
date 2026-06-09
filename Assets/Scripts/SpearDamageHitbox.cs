using System.Collections.Generic;
using UnityEngine;

public class SpearDamageHitbox : MonoBehaviour
{
    [SerializeField] private PickupableWeapon weapon;

    private readonly HashSet<EnemyHealth> damagedEnemies = new HashSet<EnemyHealth>();
    private Collider hitboxCollider;
    private bool canDamage;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();

        if (hitboxCollider != null)
        {
            hitboxCollider.isTrigger = true;
            hitboxCollider.enabled = false;
        }

        if (weapon == null)
        {
            weapon = GetComponentInParent<PickupableWeapon>();
        }
    }

    public void StartDamageWindow()
    {
        damagedEnemies.Clear();
        canDamage = true;

        if (hitboxCollider != null)
        {
            hitboxCollider.enabled = true;
        }
    }

    public void StopDamageWindow()
    {
        canDamage = false;

        if (hitboxCollider != null)
        {
            hitboxCollider.enabled = false;
        }

        damagedEnemies.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!canDamage || weapon == null)
        {
            return;
        }

        EnemyHealth enemyHealth = other.GetComponent<EnemyHealth>();

        if (enemyHealth == null)
        {
            enemyHealth = other.GetComponentInParent<EnemyHealth>();
        }

        if (enemyHealth == null)
        {
            return;
        }

        if (damagedEnemies.Contains(enemyHealth))
        {
            return;
        }

        damagedEnemies.Add(enemyHealth);
        enemyHealth.TakeDamage(weapon.Damage);

        Debug.Log($"Spear tip hit {enemyHealth.name} for {weapon.Damage} damage.");
    }
}