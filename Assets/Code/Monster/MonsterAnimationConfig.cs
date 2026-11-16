using System;
using UnityEngine;

/// <summary>
/// Per-monster animation and combat reaction configuration.
/// Create via: Assets > Create > MMO > Monster Animation Config
/// </summary>
[CreateAssetMenu(
    fileName = "MonsterAnimationConfig",
    menuName = "MMO/Monster Animation Config",
    order = 100)]
public sealed class MonsterAnimationConfig : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display or archetype name of this monster (e.g., Ancient Warrior, Goblin, Orc Boss).")]
    public string monsterName = "Enemy";

    [Tooltip("Boss or Elite monsters usually disable flinching (canFlinch = false) so normal hits do not interrupt their attacks.")]
    public bool isBossOrElite = false;

    [Header("Locomotion Thresholds (Matches AC_Monster_Base 1D Blend Tree)")]
    [Tooltip("World-space speed (m/s) that maps to the Walk threshold (1.5) in AC_Monster_Base.")]
    [Min(0.1f)]
    public float walkSpeed = 1.5f;

    [Tooltip("World-space speed (m/s) that maps to the Run threshold (4.5) in AC_Monster_Base.")]
    [Min(0.2f)]
    public float runSpeed = 4.5f;

    [Tooltip("Smoothing time (seconds) applied to the Animator Speed parameter.")]
    [Range(0.01f, 0.5f)]
    public float speedDampTime = 0.1f;

    [Header("Basic Attacks")]
    [Tooltip("Number of basic attack animations available for this monster (1 to 3). Model Enemy has 2 (Attack_01, Attack_02).")]
    [Range(1, 3)]
    public int attackCount = 2;

    [Tooltip("Relative random weight for Attack_01, Attack_02, Attack_03. Automatically normalized at runtime.")]
    public float[] attackWeights = new float[] { 0.6f, 0.4f, 0f };

    [Header("Special Skills")]
    [Tooltip("Number of special skill animations available (0 to 2).")]
    [Range(0, 2)]
    public int skillCount = 0;

    [Header("Hit React & Poise (Super Armor)")]
    [Tooltip("If false (recommended for Bosses), TriggerHit() will not play Hit_React or interrupt attacks.")]
    public bool canFlinch = true;

    [Tooltip("If false, the monster will not flinch while actively swinging an attack or casting a skill.")]
    public bool canFlinchDuringAttack = false;

    [Tooltip("Minimum cooldown (seconds) between consecutive Hit_React animations to prevent stunlocking.")]
    [Min(0f)]
    public float flinchCooldown = 0.8f;

    [Header("UpperBody Layer (Move + Cast/Attack)")]
    [Tooltip("Enable if this monster can walk/run with its lower body while playing Attack/Skill on the UpperBody layer.")]
    public bool useUpperBodyLayerWhenMoving = false;

    [Tooltip("Speed at which UpperBody layer weight blends between 0 and 1.")]
    [Min(1f)]
    public float upperBodyWeightBlendSpeed = 10f;

    /// <summary>
    /// Converts actual world movement speed (from NavMeshAgent or network velocity)
    /// into the standardized AC_Monster_Base Blend Tree value (0.0 = Idle, 1.5 = Walk, 4.5 = Run).
    /// </summary>
    public float EvaluateBlendTreeSpeed(float worldSpeed)
    {
        if (worldSpeed <= 0.05f)
        {
            return 0f;
        }

        if (worldSpeed <= walkSpeed)
        {
            float t = Mathf.InverseLerp(0f, Mathf.Max(0.1f, walkSpeed), worldSpeed);
            return Mathf.Lerp(0f, 1.5f, t);
        }
        else
        {
            float t = Mathf.InverseLerp(walkSpeed, Mathf.Max(walkSpeed + 0.1f, runSpeed), worldSpeed);
            return Mathf.Lerp(1.5f, 4.5f, t);
        }
    }

    /// <summary>
    /// Picks a valid AttackIndex (0..attackCount-1) using weighted random selection.
    /// </summary>
    public int PickRandomAttackIndex()
    {
        int count = Mathf.Clamp(attackCount, 1, 3);
        if (count == 1)
        {
            return 0;
        }

        float totalWeight = 0f;
        for (int i = 0; i < count; i++)
        {
            float w = (attackWeights != null && i < attackWeights.Length) ? Mathf.Max(0f, attackWeights[i]) : 1f;
            totalWeight += w;
        }

        if (totalWeight <= 0.0001f)
        {
            return UnityEngine.Random.Range(0, count);
        }

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < count; i++)
        {
            float w = (attackWeights != null && i < attackWeights.Length) ? Mathf.Max(0f, attackWeights[i]) : 1f;
            cumulative += w;
            if (roll <= cumulative)
            {
                return i;
            }
        }

        return count - 1;
    }

    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0.1f, walkSpeed);
        runSpeed = Mathf.Max(walkSpeed + 0.1f, runSpeed);
        attackCount = Mathf.Clamp(attackCount, 1, 3);
        skillCount = Mathf.Clamp(skillCount, 0, 2);

        if (isBossOrElite)
        {
            canFlinch = false;
        }

        if (attackWeights == null || attackWeights.Length != 3)
        {
            float[] resized = new float[3];
            if (attackWeights != null)
            {
                Array.Copy(attackWeights, resized, Mathf.Min(attackWeights.Length, 3));
            }
            else
            {
                resized[0] = 0.6f;
                resized[1] = 0.4f;
                resized[2] = 0f;
            }
            attackWeights = resized;
        }
    }
}
