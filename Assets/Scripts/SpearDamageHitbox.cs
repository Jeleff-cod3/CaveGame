using System.Collections.Generic;
using UnityEngine;

public class SpearDamageHitbox : MonoBehaviour
{
    [SerializeField] private PickupableWeapon weapon;

    private readonly HashSet<Component> damagedTargets = new HashSet<Component>();
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
        damagedTargets.Clear();
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

        damagedTargets.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!canDamage || weapon == null)
        {
            return;
        }

        if (weapon.ShouldIgnoreCollider(other))
        {
            return;
        }

        // Let the weapon own melee impact logic (damage + optional stick).
        if (weapon.TryRegisterMeleeContact(other))
        {
            return;
        }

        Component damageableComponent = other.GetComponent(typeof(IDamageable)) as Component;

        if (damageableComponent == null)
        {
            damageableComponent = other.GetComponentInParent(typeof(IDamageable)) as Component;
        }

        if (damageableComponent == null || damageableComponent.transform.IsChildOf(weapon.transform))
        {
            return;
        }

        if (damagedTargets.Contains(damageableComponent))
        {
            return;
        }

        if (!(damageableComponent is IDamageable damageable))
        {
            return;
        }

        damagedTargets.Add(damageableComponent);
        damageable.TakeDamage(weapon.Damage);

        Debug.Log($"Spear tip hit {damageableComponent.name} for {weapon.Damage} damage.");
    }
}
