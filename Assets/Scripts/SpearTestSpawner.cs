using UnityEngine;

public class SpearTestSpawner : MonoBehaviour
{
    [SerializeField] private PickupableWeapon spearPrefab;
    [SerializeField] private int spearCount = 5;
    [SerializeField] private float spacing = 1.4f;
    [SerializeField] private Vector3 startOffset = new Vector3(-3f, 1f, 2f);

    private void Start()
    {
        if (spearPrefab == null)
        {
            Debug.LogWarning("Spear prefab missing from SpearTestSpawner.");
            return;
        }

        for (int i = 0; i < spearCount; i++)
        {
            Vector3 spawnPosition = transform.position + startOffset + Vector3.right * spacing * i;
            PickupableWeapon spear = Instantiate(spearPrefab, spawnPosition, Quaternion.identity);
            spear.name = $"Testing Spear {i + 1}";
        }
    }
}