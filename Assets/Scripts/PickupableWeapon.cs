using System;
using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private float meleeImpactPauseTime = 0.06f;
    [SerializeField] private SpearDamageHitbox tipHitbox;
    [SerializeField] private float meleeHitRadius = 0.3f;
    [SerializeField] private LayerMask meleeHitLayers = ~0;

    [Header("Held Pose")]
    [SerializeField] private Vector3 heldLocalPositionOffset = new Vector3(0.45f, 0.18f, 0.4f);
    [SerializeField] private Vector3 heldTipDirectionLocal = new Vector3(0f, -0.18f, 1f);
    [SerializeField] private Vector3 stabTipDirectionLocal = new Vector3(0f, -0.32f, 1f);

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
    [SerializeField] [Range(0f, 1f)] private float meleeStickChance = 0.1f;
    [SerializeField] private float groundBreakChance = 0.5f;
    [SerializeField] private float stuckDepth = 0.25f;

    [Header("Visual")]
    [SerializeField] private bool alignSpearToVelocity = true;

    private Rigidbody rb;
    private Collider mainCollider;
    private Coroutine attackRoutine;
    private SpearState state = SpearState.World;
    private Transform ownerRoot;

    private Vector3 heldLocalPosition;
    private Quaternion heldLocalRotation;

    private Vector3 throwStartPosition;
    private Vector3 throwVelocity;
    private Vector3 previousTipPosition;
    private float thrownTimer;

    private bool meleeImpactRegistered;
    private bool meleeShouldStick;
    private Collider meleeImpactCollider;
    private Vector3 meleeImpactPoint;

    private readonly HashSet<Component> damagedMeleeTargets = new HashSet<Component>();

    public int Damage => damage;
    public float AttackCooldown => attackCooldown;
    public float TipCastRadius => tipCastRadius;
    public bool IsHeld => state == SpearState.Held;
    public bool IsBroken => state == SpearState.Broken;

    public event Action<PickupableWeapon> RemovedFromWorldSupply;
    private bool hasNotifiedRemovedFromWorldSupply;

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
        ownerRoot = weaponHolder != null ? weaponHolder.root : null;
        NotifyRemovedFromWorldSupplyOnce();

        StopAttackRoutine();
        StopTipDamage();

        transform.SetParent(weaponHolder);
        heldLocalPosition = heldLocalPositionOffset;
        heldLocalRotation = GetPoseRotation(heldTipDirectionLocal);
        transform.localPosition = heldLocalPosition;
        transform.localRotation = heldLocalRotation;

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

        ownerRoot = null;
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
        ResetMeleeImpactState();
        damagedMeleeTargets.Clear();

        heldLocalPosition = transform.localPosition;
        heldLocalRotation = transform.localRotation;

        Vector3 startPosition = heldLocalPosition;
        Vector3 thrustDirection = GetSafeLocalDirection(stabTipDirectionLocal);
        Vector3 endPosition = heldLocalPosition + thrustDirection * thrustDistance;
        Quaternion endRotation = GetPoseRotation(stabTipDirectionLocal);

        if (tipHitbox != null)
        {
            tipHitbox.StartDamageWindow();
        }

        previousTipPosition = spearTip != null ? spearTip.position : transform.position;
        float timer = 0f;

        while (timer < thrustForwardTime)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / thrustForwardTime);
            transform.localPosition = Vector3.Lerp(startPosition, endPosition, t);
            transform.localRotation = Quaternion.Slerp(heldLocalRotation, endRotation, t);

            Vector3 currentTipPosition = spearTip != null ? spearTip.position : transform.position;
            TrySweepForMeleeContact(previousTipPosition, currentTipPosition);
            TryApplyFallbackMeleeDamage(previousTipPosition, currentTipPosition);
            previousTipPosition = currentTipPosition;

            if (meleeImpactRegistered)
            {
                SnapTipToImpactPoint();
                break;
            }

            yield return null;
        }

        if (meleeImpactRegistered)
        {
            if (meleeShouldStick && meleeImpactCollider != null)
            {
                CompleteMeleeStick();
                yield break;
            }

            if (meleeImpactPauseTime > 0f)
            {
                yield return new WaitForSeconds(meleeImpactPauseTime);
            }
        }

        timer = 0f;

        while (timer < thrustReturnTime)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / thrustReturnTime);
            transform.localPosition = Vector3.Lerp(endPosition, startPosition, t);
            transform.localRotation = Quaternion.Slerp(endRotation, heldLocalRotation, t);
            yield return null;
        }

        transform.localPosition = startPosition;
        transform.localRotation = heldLocalRotation;

        StopTipDamage();
        attackRoutine = null;
        ResetMeleeImpactState();
        damagedMeleeTargets.Clear();
    }

    private void TryApplyFallbackMeleeDamage(Vector3 from, Vector3 to)
    {
        // Keep legacy fallback from the original version for scenes without a working hitbox.
        if (tipHitbox != null)
        {
            return;
        }

        Vector3 sweep = to - from;
        float distance = sweep.magnitude;
        int hitMask = meleeHitLayers.value != 0 ? meleeHitLayers.value : Physics.DefaultRaycastLayers;

        if (distance <= 0.001f)
        {
            Collider[] overlaps = Physics.OverlapSphere(
                to,
                meleeHitRadius,
                hitMask,
                QueryTriggerInteraction.Ignore
            );

            foreach (Collider overlap in overlaps)
            {
                TryDamageTarget(overlap);
            }
        }
        else
        {
            RaycastHit[] hits = Physics.SphereCastAll(
                from,
                meleeHitRadius,
                sweep.normalized,
                distance,
                hitMask,
                QueryTriggerInteraction.Ignore
            );

            foreach (RaycastHit hit in hits)
            {
                TryDamageTarget(hit.collider);
            }
        }
    }

    private void TryDamageTarget(Collider targetCollider)
    {
        Component damageableComponent = FindDamageableComponent(targetCollider);
        if (!(damageableComponent is IDamageable damageable))
        {
            return;
        }

        if (damagedMeleeTargets.Contains(damageableComponent))
        {
            return;
        }

        damagedMeleeTargets.Add(damageableComponent);
        damageable.TakeDamage(damage);
        Debug.Log($"Fallback melee hit {damageableComponent.name} for {damage} damage.");
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
        ClearOwnerWeaponReference();

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
            transform.rotation = GetWorldRotationForTipDirection(throwVelocity.normalized);
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
            transform.rotation = GetWorldRotationForTipDirection(currentVelocity.normalized);
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
            if (!TryFindFallbackDamageableHit(from, move.normalized, distance, out hit))
            {
                return false;
            }
        }

        Debug.Log($"Spear hit {hit.collider.name} on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}");

        HandleSpearHit(hit, velocity);
        return true;
    }

    private void HandleSpearHit(RaycastHit hit, Vector3 velocity)
    {
        IDamageable damageable = FindDamageable(hit.collider);
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
            if (UnityEngine.Random.value < groundBreakChance)
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
        NotifyRemovedFromWorldSupplyOnce();

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

        if (mainCollider != null)
        {
            mainCollider.enabled = true;
            mainCollider.isTrigger = false;
        }

        EnableWorldPhysics();
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

    private void EnableWorldPhysics()
    {
        if (rb == null)
        {
            return;
        }

        rb.isKinematic = false;
        rb.useGravity = true;
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

    public bool ShouldIgnoreCollider(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return true;
        }

        if (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
        {
            return true;
        }

        return ownerRoot != null && hitCollider.transform.root == ownerRoot;
    }

    public void NotifyMeleeDamageHit(Collider hitCollider)
    {
        TryRegisterMeleeContact(hitCollider);
    }

    public bool TryRegisterMeleeContact(Collider hitCollider)
    {
        if (state != SpearState.Held || attackRoutine == null || meleeImpactRegistered || hitCollider == null)
        {
            return false;
        }

        Component damageableComponent = FindDamageableComponent(hitCollider);

        if (!(damageableComponent is IDamageable damageable))
        {
            return false;
        }

        if (damagedMeleeTargets.Contains(damageableComponent))
        {
            return false;
        }

        damagedMeleeTargets.Add(damageableComponent);
        meleeImpactRegistered = true;
        meleeImpactCollider = hitCollider;
        meleeImpactPoint = hitCollider.ClosestPoint(spearTip.position);
        meleeShouldStick = UnityEngine.Random.value < meleeStickChance;
        damageable.TakeDamage(damage);
        Debug.Log($"Spear tip hit {damageableComponent.name} for {damage} damage.");
        return true;
    }

    public Component FindDamageableComponent(Collider hitCollider)
    {
        if (ShouldIgnoreCollider(hitCollider))
        {
            return null;
        }

        Transform current = hitCollider != null ? hitCollider.transform : null;

        while (current != null)
        {
            Component[] components = current.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component is IDamageable)
                {
                    return component;
                }
            }

            current = current.parent;
        }

        return null;
    }

    private IDamageable FindDamageable(Collider hitCollider)
    {
        return FindDamageableComponent(hitCollider) as IDamageable;
    }

    private bool TryFindFallbackDamageableHit(
        Vector3 origin,
        Vector3 direction,
        float distance,
        out RaycastHit damageableHit)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            tipCastRadius,
            direction,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        foreach (RaycastHit hit in hits)
        {
            if (FindDamageable(hit.collider) != null)
            {
                damageableHit = hit;
                return true;
            }
        }

        damageableHit = default;
        return false;
    }

    private Quaternion GetPoseRotation(Vector3 desiredTipDirectionLocal)
    {
        Vector3 tipAxis = GetSpearTipAxisLocal();
        Vector3 desiredDirection = GetSafeLocalDirection(desiredTipDirectionLocal);
        return Quaternion.FromToRotation(tipAxis, desiredDirection);
    }

    private Vector3 GetSpearTipAxisLocal()
    {
        if (spearTip == null || spearTip == transform)
        {
            return Vector3.up;
        }

        Vector3 localAxis = spearTip.localPosition;

        if (localAxis.sqrMagnitude < 0.001f)
        {
            return Vector3.up;
        }

        return localAxis.normalized;
    }

    private Vector3 GetSafeLocalDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.001f)
        {
            return direction.normalized;
        }

        return Vector3.forward;
    }

    private Quaternion GetWorldRotationForTipDirection(Vector3 desiredTipDirectionWorld)
    {
        Vector3 safeDirection = desiredTipDirectionWorld.sqrMagnitude > 0.001f
            ? desiredTipDirectionWorld.normalized
            : transform.forward;
        return Quaternion.FromToRotation(GetSpearTipAxisLocal(), safeDirection);
    }

    private void CompleteMeleeStick()
    {
        StopTipDamage();
        ClearOwnerWeaponReference();

        Vector3 tipDirection = spearTip != null
            ? spearTip.position - transform.position
            : transform.forward;

        if (tipDirection.sqrMagnitude <= 0.001f)
        {
            tipDirection = transform.forward;
        }

        StickIntoTarget(meleeImpactCollider, meleeImpactPoint, tipDirection.normalized);
        StopAttackRoutine();
        ResetMeleeImpactState();
    }

    private void StickIntoTarget(Collider targetCollider, Vector3 hitPoint, Vector3 direction)
    {
        state = SpearState.Stuck;

        Vector3 safeDirection = direction.sqrMagnitude > 0.001f
            ? direction.normalized
            : transform.forward;
        Vector3 stickPosition = hitPoint - safeDirection * stuckDepth;

        transform.SetParent(null, true);
        transform.position = stickPosition;
        transform.rotation = GetWorldRotationForTipDirection(safeDirection);

        Transform stickParent = targetCollider != null && targetCollider.attachedRigidbody != null
            ? targetCollider.attachedRigidbody.transform
            : targetCollider != null
                ? targetCollider.transform
                : null;

        if (stickParent != null)
        {
            transform.SetParent(stickParent, true);
        }

        ownerRoot = null;
        FreezeAsStuckPickup();
        Debug.Log($"Spear stuck in {targetCollider?.name ?? "target"}.");
    }

    private void SnapTipToImpactPoint()
    {
        if (spearTip == null)
        {
            return;
        }

        Vector3 tipOffset = spearTip.position - transform.position;
        transform.position = meleeImpactPoint - tipOffset;
    }

    private void ResetMeleeImpactState()
    {
        meleeImpactRegistered = false;
        meleeShouldStick = false;
        meleeImpactCollider = null;
        meleeImpactPoint = Vector3.zero;
    }

    private void ClearOwnerWeaponReference()
    {
        if (ownerRoot == null)
        {
            return;
        }

        ownerRoot = null;
    }

    private void NotifyRemovedFromWorldSupplyOnce()
    {
        if (hasNotifiedRemovedFromWorldSupply)
        {
            return;
        }

        hasNotifiedRemovedFromWorldSupply = true;
        RemovedFromWorldSupply?.Invoke(this);
    }

    private void TrySweepForMeleeContact(Vector3 from, Vector3 to)
    {
        if (meleeImpactRegistered)
        {
            return;
        }

        Vector3 move = to - from;
        float distance = move.magnitude;

        if (distance <= 0.0001f)
        {
            TryOverlapForMeleeContact(to);
            return;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            from,
            tipCastRadius,
            move.normalized,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;
        Collider closestDamageable = null;

        foreach (RaycastHit hit in hits)
        {
            if (ShouldIgnoreCollider(hit.collider))
            {
                continue;
            }

            if (FindDamageableComponent(hit.collider) == null)
            {
                continue;
            }

            if (hit.distance >= closestDistance)
            {
                continue;
            }

            closestDistance = hit.distance;
            closestDamageable = hit.collider;
            meleeImpactPoint = hit.point;
        }

        if (closestDamageable != null)
        {
            TryRegisterMeleeContact(closestDamageable);
            return;
        }

        TryOverlapForMeleeContact(to);
    }

    private void TryOverlapForMeleeContact(Vector3 center)
    {
        Collider[] overlaps = Physics.OverlapSphere(
            center,
            tipCastRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider overlap in overlaps)
        {
            if (TryRegisterMeleeContact(overlap))
            {
                meleeImpactPoint = overlap.ClosestPoint(center);
                return;
            }
        }
    }
}
