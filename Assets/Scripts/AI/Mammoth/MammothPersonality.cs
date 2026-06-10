using UnityEngine;

public class MammothPersonality : MonoBehaviour
{
    [Header("Generated Personality")]
    [Range(0f, 1f)] public float bravery;
    [Range(0f, 1f)] public float aggression;
    [Range(0f, 1f)] public float curiosity;
    [Range(0f, 1f)] public float fearfulness;

    [Header("Behaviour Thresholds")]
    [Range(0f, 1f)] public float panicHealthThreshold = 0.3f;
    [Range(0f, 1f)] public float enragedHealthThreshold = 0.45f;

    [Header("Runtime Emotion")]
    [Range(0f, 1f)] public float anger;
    [Range(0f, 1f)] public float fear;

    [Header("Generation")]
    [SerializeField] private bool randomizeOnAwake = true;

    private void Awake()
    {
        if (randomizeOnAwake)
        {
            RandomizePersonality();
        }
    }

    public void RandomizePersonality()
    {
        bravery = Random.Range(0.15f, 1f);
        aggression = Random.Range(0.15f, 1f);
        curiosity = Random.Range(0.1f, 0.8f);

        fearfulness = 1f - bravery;

        panicHealthThreshold = Mathf.Lerp(0.2f, 0.55f, fearfulness);
        enragedHealthThreshold = Mathf.Lerp(0.65f, 0.3f, bravery);

        anger = aggression * 0.25f;
        fear = fearfulness * 0.25f;

        Debug.Log(
            $"Mammoth personality generated | Bravery: {bravery:0.00}, " +
            $"Aggression: {aggression:0.00}, Fearfulness: {fearfulness:0.00}"
        );
    }

    public void AddAnger(float amount)
    {
        anger = Mathf.Clamp01(anger + amount);
    }

    public void AddFear(float amount)
    {
        fear = Mathf.Clamp01(fear + amount);
    }

    public float GetFightDrive()
    {
        return Mathf.Clamp01((bravery * 0.45f) + (aggression * 0.35f) + (anger * 0.2f) - (fear * 0.25f));
    }

    public float GetFlightDrive()
    {
        return Mathf.Clamp01((fearfulness * 0.45f) + (fear * 0.35f) - (bravery * 0.25f) - (anger * 0.1f));
    }
}