using System.Collections;
using UnityEngine;

public class PickupableWeapon : MonoBehaviour
{
    private enum SpearState
    {
        World,
        Held,
        Thrown,
        Stuck,
        Broken
    }

    [Header("Stats")]
    [SerializeField] private int damage = 25;
    [SerializeField] private float attackCooldown = 0.6f;

    [Header("Melee Attack")]
    [SerializeField] private float thrustDistance = 0.9f;
    [SerializeField] private float thrustForwardTime = 0.08f;
    [SerializeField] private float thrustReturnTime = 0.18f;
    [SerializeField] private SpearDamageHitbox tipHitbox;

    [Header("Throw Physics")]
    [SerializeField] private Transform spearTip;
    [SerializeField] private float throwSpeed = 18f;
    [SerializeField] private float throwUpwardBoost = 4.5f;
    [SerializeField] private float gravity = 22f;
    [SerializeField] private float tipCastRadius = 0.18f;
    [SerializeField] private float maxThrownLifetime = 6f;

    [Header("Sticking")]
    [SerializeField] private LayerMask stickableLayers;
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float groundBreakChance = 0.5f;
    [SerializeField] private float stuckDepth = 0.25f;

    [Header("Visual")]
    [SerializeField] private bool alignSpearToVelocity = true;

    private Rigidbody rb;
    private Collider mainCollider;
    private Coroutine attackRoutine;
    private SpearState state = SpearState.World;

    private Vector3 heldLocalPosition;
    private Quaternion heldLocalRotation;

    private Vector3 throwStartPosition;
    private Vector3 throwVelocity;
    private Vector3 previousTipPosition;
    private float thrownTimer;

    public int Damage => damage;
    public float AttackCooldown => attackCooldown;
    public bool IsHeld => state == SpearState.Held;
    public bool IsBroken => state == SpearState.Broken;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mainCollider = GetComponent<Collider>();

        if (tipHitbox == null)
        {
            tipHitbox = GetComponentInChildren<SpearDamageHitbox>();
        }

        if (spearTip == null && tipHitbox != null)
        {
            spearTip = tipHitbox.transform;
        }

        if (spearTip == null)
        {
            spearTip = transform;
        }

        SetupWorldPhysics();
    }

    private void Update()
    {
        if (state == SpearState.Thrown)
        {
            SimulateThrownSpear();
        }
    }

    public void PickUp(Transform weaponHolder)
    {
        if (state == SpearState.Broken)
        {
            Debug.Log("Cannot pick up broken spear.");
            return;
        }

        state = SpearState.Held;

        StopAttackRoutine();
        StopTipDamage();

        transform.SetParent(weaponHolder);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        heldLocalPosition = transform.localPosition;
        heldLocalRotation = transform.localRotation;

        FreezeRigidbody();

        if (mainCollider != null)
        {
            mainCollider.enabled = false;
            mainCollider.isTrigger = false;
        }
    }

    public void Drop()
    {
        if (state == SpearState.Broken)
        {
            return;
        }

        StopAttackRoutine();
        StopTipDamage();

        transform.SetParent(null);
        SetupWorldPhysics();
    }

    public void StartMeleeAttack()
    {
        if (state != SpearState.Held)
        {
            return;
        }

        if (attackRoutine != null)
        {
            return;
        }

        attackRoutine = StartCoroutine(MeleeAttackRoutine());
    }

    private IEnumerator MeleeAttackRoutine()
    {
        heldLocalPosition = transform.localPosition;
        heldLocalRotation = transform.localRotation;

        Vector3 startPosition = heldLocalPosition;
        Vector3 endPosition = heldLocalPosition + Vector3.forward * thrustDistance;

        if (tipHitbox != null)
        {
            tipHitbox.StartDamageWindow();
        }

        float timer = 0f;

        while (timer < thrustForwardTime)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / thrustForwardTime);
            transform.localPosition = Vector3.Lerp(startPosition, endPosition, t);
            yield return null;
        }

        timer = 0f;

        while (timer < thrustReturnTime)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / thrustReturnTime);
            transform.localPosition = Vector3.Lerp(endPosition, startPosition, t);
            yield return null;
        }

        transform.localPosition = startPosition;
        transform.localRotation = heldLocalRotation;

        StopTipDamage();
        attackRoutine = null;
    }

    public void Throw(Vector3 direction)
    {
        if (state != SpearState.Held)
        {
            return;
        }

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = transform.forward;
        }

        state = SpearState.Thrown;

        StopAttackRoutine();
        StopTipDamage();

        transform.SetParent(null);
        FreezeRigidbody();

        if (mainCollider != null)
        {
            mainCollider.enabled = false;
        }

        Vector3 throwDirection = direction.normalized;

        throwStartPosition = transform.position;
        throwVelocity = throwDirection * throwSpeed + Vector3.up * throwUpwardBoost;
        previousTipPosition = spearTip.position;
        thrownTimer = 0f;

        if (alignSpearToVelocity && throwVelocity.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(throwVelocity.normalized, Vector3.up);
        }
    }

    private void SimulateThrownSpear()
    {
        thrownTimer += Time.deltaTime;

        if (thrownTimer > maxThrownLifetime)
        {
            Debug.LogWarning("Spear lifetime ended without hitting anything.");
            SetupWorldPhysics();
            return;
        }

        Vector3 previousPosition = transform.position;

        Vector3 newPosition =
            throwStartPosition +
            throwVelocity * thrownTimer +
            0.5f * Vector3.down * gravity * thrownTimer * thrownTimer;

        Vector3 currentVelocity = throwVelocity + Vector3.down * gravity * thrownTimer;

        Vector3 nextTipPosition = EstimateNextTipPosition(previousPosition, newPosition);

        if (CheckTipCollision(previousTipPosition, nextTipPosition, currentVelocity))
        {
            return;
        }

        transform.position = newPosition;

        if (alignSpearToVelocity && currentVelocity.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(currentVelocity.normalized, Vector3.up);
        }

        previousTipPosition = spearTip.position;
    }

    private Vector3 EstimateNextTipPosition(Vector3 previousRootPosition, Vector3 nextRootPosition)
    {
        Vector3 tipOffset = spearTip.position - previousRootPosition;
        return nextRootPosition + tipOffset;
    }

    private bool CheckTipCollision(Vector3 from, Vector3 to, Vector3 velocity)
    {
        Vector3 move = to - from;
        float distance = move.magnitude;

        if (distance <= 0.001f)
        {
            return false;
        }

        int mask = stickableLayers.value;

        if (mask == 0)
        {
            mask = Physics.DefaultRaycastLayers;
            Debug.LogWarning("Stickable Layers is empty. Using DefaultRaycastLayers.");
        }

        bool didHit = Physics.SphereCast(
            from,
            tipCastRadius,
            move.normalized,
            out RaycastHit hit,
            distance,
            mask,
            QueryTriggerInteraction.Ignore
        );

        if (!didHit)
        {
            return false;
        }

        Debug.Log($"Spear hit {hit.collider.name} on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}");

        HandleSpearHit(hit, velocity);
        return true;
    }

    private void HandleSpearHit(RaycastHit hit, Vector3 velocity)
    {
        IDamageable damageable = hit.collider.GetComponent<IDamageable>();

        if (damageable == null)
        {
            damageable = hit.collider.GetComponentInParent<IDamageable>();
        }

        bool hitGround = IsGroundHit(hit.collider);

        if (damageable != null)
        {
            damageable.TakeDamage(damage);
            StickIntoTarget(hit, velocity);
            Debug.Log($"Spear stabbed into {hit.collider.name}.");
            return;
        }

        if (hitGround)
        {
            if (Random.value < groundBreakChance)
            {
                BreakSpear(hit);
            }
            else
            {
                StickIntoGround(hit, velocity);
            }

            return;
        }

        StickIntoGround(hit, velocity);
    }

    private bool IsGroundHit(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return false;
        }

        if (IsInLayerMask(hitCollider.gameObject.layer, groundLayers))
        {
            return true;
        }

        if (hitCollider.GetComponent<MeshCollider>() != null)
        {
            return true;
        }

        if (hitCollider.gameObject.name.StartsWith("Chunk"))
        {
            return true;
        }

        return false;
    }

    private void StickIntoTarget(RaycastHit hit, Vector3 velocity)
    {
        state = SpearState.Stuck;

        Vector3 direction = GetSafeVelocityDirection(velocity);
        Vector3 stickPosition = hit.point - direction * stuckDepth;

        transform.position = stickPosition;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        Transform stickParent = hit.collider.attachedRigidbody != null
            ? hit.collider.attachedRigidbody.transform
            : hit.collider.transform;

        transform.SetParent(stickParent, true);
        FreezeAsStuckPickup();
    }

    private void StickIntoGround(RaycastHit hit, Vector3 velocity)
    {
        state = SpearState.Stuck;

        Vector3 direction = GetSafeVelocityDirection(velocity);
        Vector3 stickPosition = hit.point - direction * stuckDepth;

        transform.position = stickPosition;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        transform.SetParent(null);
        FreezeAsStuckPickup();

        Debug.Log("Spear stuck in the ground.");
    }

    private void BreakSpear(RaycastHit hit)
    {
        state = SpearState.Broken;

        transform.SetParent(null);
        transform.position = hit.point;

        FreezeRigidbody();

        if (mainCollider != null)
        {
            mainCollider.enabled = false;
        }

        StopTipDamage();

        Debug.Log("Spear broke after hitting the ground.");
        Destroy(gameObject, 1.5f);
    }

    private void FreezeAsStuckPickup()
    {
        FreezeRigidbody();

        if (mainCollider != null)
        {
            mainCollider.enabled = true;
            mainCollider.isTrigger = true;
        }

        StopTipDamage();
    }

    private void SetupWorldPhysics()
    {
        state = SpearState.World;

        FreezeRigidbody();

        if (mainCollider != null)
        {
            mainCollider.enabled = true;
            mainCollider.isTrigger = true;
        }

        StopTipDamage();
    }

    private void FreezeRigidbody()
    {
        if (rb == null)
        {
            return;
        }

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void StopAttackRoutine()
    {
        if (attackRoutine == null)
        {
            return;
        }

        StopCoroutine(attackRoutine);
        attackRoutine = null;
    }

    private void StopTipDamage()
    {
        if (tipHitbox != null)
        {
            tipHitbox.StopDamageWindow();
        }
    }

    private Vector3 GetSafeVelocityDirection(Vector3 velocity)
    {
        if (velocity.sqrMagnitude > 0.001f)
        {
            return velocity.normalized;
        }

        return transform.forward;
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}