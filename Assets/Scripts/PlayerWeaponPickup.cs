using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerWeaponPickup : MonoBehaviour
{
    private Transform weaponHolder;

    private PickupableWeapon nearbyWeapon;
    private PickupableWeapon equippedWeapon;

    public PickupableWeapon EquippedWeapon => equippedWeapon;
    public bool HasWeapon => equippedWeapon != null;

    public void Initialize(Transform holder)
    {
        weaponHolder = holder;
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (equippedWeapon == null && nearbyWeapon != null)
            {
                PickUpWeapon();
            }
            else if (equippedWeapon != null)
            {
                DropWeapon();
            }
        }
    }

    private void PickUpWeapon()
    {
        if (weaponHolder == null)
        {
            Debug.LogWarning("Cannot pick up weapon because WeaponHolder is missing.");
            return;
        }

        equippedWeapon = nearbyWeapon;
        equippedWeapon.PickUp(weaponHolder);
        nearbyWeapon = null;

        Debug.Log("Weapon picked up.");
    }

    private void DropWeapon()
    {
        equippedWeapon.Drop();
        equippedWeapon = null;

        Debug.Log("Weapon dropped.");
    }

    public void ThrowEquippedWeapon(Vector3 direction)
    {
        if (equippedWeapon == null)
        {
            return;
        }

        PickupableWeapon thrownWeapon = equippedWeapon;
        equippedWeapon = null;

        thrownWeapon.Throw(direction);
    }

    private void OnTriggerEnter(Collider other)
    {
        PickupableWeapon weapon = other.GetComponent<PickupableWeapon>();

        if (weapon != null && equippedWeapon == null && !weapon.IsBroken)
        {
            nearbyWeapon = weapon;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PickupableWeapon weapon = other.GetComponent<PickupableWeapon>();

        if (weapon != null && weapon == nearbyWeapon)
        {
            nearbyWeapon = null;
        }
    }
}