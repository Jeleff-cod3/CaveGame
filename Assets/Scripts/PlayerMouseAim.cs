using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMouseAim : MonoBehaviour
{
    [Header("Aim Targeting")]
    [SerializeField] private LayerMask aimSurfaceLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private float aimRayDistance = 1000f;
    [SerializeField] private bool useGroundPlaneFallback = true;

    public bool IsAimModifierPressed
    {
        get
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }

    public bool TryGetAimDirection(out Vector3 aimDirection, bool requireFrontHemisphere)
    {
        aimDirection = Vector3.zero;

        if (!TryGetMouseWorldPoint(out Vector3 targetPoint))
        {
            return false;
        }

        Vector3 flatDirection = targetPoint - transform.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude < 0.001f)
        {
            return false;
        }

        aimDirection = flatDirection.normalized;

        if (requireFrontHemisphere && Vector3.Dot(transform.forward, aimDirection) < 0f)
        {
            aimDirection = Vector3.zero;
            return false;
        }

        return true;
    }

    public bool TryGetMouseWorldPoint(out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;

        Camera aimCamera = Camera.main;
        Mouse mouse = Mouse.current;

        if (aimCamera == null || mouse == null)
        {
            return false;
        }

        Ray aimRay = aimCamera.ScreenPointToRay(mouse.position.ReadValue());
        int surfaceMask = aimSurfaceLayers.value == 0
            ? Physics.DefaultRaycastLayers
            : aimSurfaceLayers.value;

        RaycastHit[] hits = Physics.RaycastAll(
            aimRay,
            aimRayDistance,
            surfaceMask,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance >= closestDistance)
            {
                continue;
            }

            closestDistance = hit.distance;
            worldPoint = hit.point;
        }

        if (closestDistance < float.MaxValue)
        {
            return true;
        }

        if (!useGroundPlaneFallback)
        {
            return false;
        }

        Plane fallbackPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

        if (!fallbackPlane.Raycast(aimRay, out float enterDistance))
        {
            return false;
        }

        worldPoint = aimRay.GetPoint(enterDistance);
        return true;
    }
}
