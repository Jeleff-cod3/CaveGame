using UnityEngine;
using System;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 100;

    private int currentHealth;
    private bool hasDied;
    private MammothState mammothState;
    private MammothPersonality mammothPersonality;

    public event Action<int, int> HealthChanged;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public float HealthPercent => maxHealth <= 0 ? 0f : Mathf.Clamp01((float)currentHealth / maxHealth);
    public bool IsDead => currentHealth <= 0;

    private void Awake()
    {
        currentHealth = maxHealth;
        mammothState = GetComponent<MammothState>();
        mammothPersonality = GetComponent<MammothPersonality>();
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void OnEnable()
    {
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(int damage)
    {
        if (IsDead)
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        HealthChanged?.Invoke(currentHealth, maxHealth);
        MultiplayerPrototype.NotifyEnemyDamaged(this, damage);

        if (mammothState != null)
        {
            mammothState.MarkDamaged();
        }

        if (mammothPersonality != null)
        {
            mammothPersonality.AddAnger(0.18f);
            mammothPersonality.AddFear(0.08f);
        }

        Debug.Log($"{gameObject.name} took {damage} damage. HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            if (!MultiplayerPrototype.ShouldDeferEnemyDeath(this))
            {
                Die();
            }
        }
    }

    public void ApplyNetworkHealth(int newCurrentHealth, int newMaxHealth)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        int clampedHealth = Mathf.Clamp(newCurrentHealth, 0, maxHealth);

        if (currentHealth == clampedHealth && hasDied == (clampedHealth <= 0))
        {
            return;
        }

        currentHealth = clampedHealth;
        HealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            hasDied = false;
        }
    }

    private void Die()
    {
        if (hasDied)
        {
            return;
        }

        hasDied = true;
        Debug.Log($"{gameObject.name} died.");
        Destroy(gameObject);
    }
}
