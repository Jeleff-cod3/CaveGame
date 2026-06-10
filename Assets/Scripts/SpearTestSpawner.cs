using System.Collections;
using UnityEngine;

public class SpearTestSpawner : MonoBehaviour
{
    [SerializeField] private PickupableWeapon spearPrefab;
    [SerializeField] private int spearCount = 5;
    [SerializeField] private float spacing = 1.5f;
    [SerializeField] private Vector3 startOffset = new Vector3(-3f, 0f, 2f);
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float raycastHeight = 300f;
    [SerializeField] private float spearHeightAboveGround = 0.75f;
    [SerializeField] private int spawnDelayFrames = 3;

    private IEnumerator Start()
    {
        for (int i = 0; i < spawnDelayFrames; i++)
        {
            yield return null;
        }

        if (spearPrefab == null)
        {
            Debug.LogWarning("Spear prefab is missing.");
            yield break;
        }

        for (int i = 0; i < spearCount; i++)
        {
            Vector3 basePosition = transform.position + startOffset + Vector3.right * spacing * i;

            if (!TryGetGroundedSpawnPosition(basePosition, out Vector3 spawnPosition))
            {
                Debug.LogWarning($"No procedural ground found under spear {i + 1}. Skipping spawn.");
                continue;
            }

            PickupableWeapon spear = Instantiate(
                spearPrefab,
                spawnPosition,
                Quaternion.Euler(0f, 90f, 0f)
            );

            spear.name = $"Testing Spear {i + 1}";
        }
    }

    private bool TryGetGroundedSpawnPosition(Vector3 basePosition, out Vector3 spawnPosition)
    {
        Vector3 rayStart = basePosition + Vector3.up * raycastHeight;

        if (Physics.Raycast(
            rayStart,
            Vector3.down,
            out RaycastHit hit,
            raycastHeight * 2f,
            groundLayer,
            QueryTriggerInteraction.Ignore))
        {
            spawnPosition = hit.point + Vector3.up * spearHeightAboveGround;
            return true;
        }

        spawnPosition = Vector3.zero;
        return false;
    }
}