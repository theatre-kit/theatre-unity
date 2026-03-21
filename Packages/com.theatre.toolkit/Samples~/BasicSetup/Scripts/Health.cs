using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHp = 100f;
    public float currentHp = 100f;
    public bool isInvulnerable;

    public void TakeDamage(float amount)
    {
        if (isInvulnerable) return;
        currentHp = Mathf.Max(0, currentHp - amount);
    }

    public void Heal(float amount)
    {
        currentHp = Mathf.Min(maxHp, currentHp + amount);
    }
}
