using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// While enabled (during a round) spawns targets at random places in front of the player.
/// Targets appear faster as the round goes on.
/// </summary>
public class TargetSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] Target targetPrefab;
    [SerializeField] Target movingTargetPrefab;
    [SerializeField, Range(0f, 1f)] float movingTargetChance = 0.3f;

    [Header("Timing")]
    [SerializeField] float firstSpawnDelay = 1.2f;   // seconds between targets at the start of a round
    [SerializeField] float lastSpawnDelay = 0.5f;    // ... and at the end of a round
    [SerializeField] int maxTargets = 5;

    [Header("Spawn area (in front of this object)")]
    [SerializeField] float minDistance = 5f;
    [SerializeField] float maxDistance = 12f;
    [SerializeField] float maxAngle = 40f;           // degrees left/right of straight ahead
    [SerializeField] float minHeight = 1f;
    [SerializeField] float maxHeight = 3.5f;

    [SerializeField] Transform player;               // the headset camera; targets turn to face it

    readonly List<Target> spawnedTargets = new List<Target>();
    float timer;

    void OnEnable()
    {
        timer = 0.5f;
    }

    void Update()
    {
        // Forget targets that were hit or timed out (destroyed objects compare equal to null)
        spawnedTargets.RemoveAll(target => target == null);

        timer -= Time.deltaTime;
        if (timer > 0f || spawnedTargets.Count >= maxTargets)
            return;

        SpawnTarget();
        timer = Mathf.Lerp(firstSpawnDelay, lastSpawnDelay, GameManager.Instance.RoundProgress);
    }

    void SpawnTarget()
    {
        // Random direction inside a cone in front of the spawner
        float angle = Random.Range(-maxAngle, maxAngle);
        Vector3 direction = Quaternion.Euler(0f, angle, 0f) * transform.forward;

        Vector3 position = transform.position
                           + direction * Random.Range(minDistance, maxDistance)
                           + Vector3.up * Random.Range(minHeight, maxHeight);

        // Turn the target so its face (blue Z axis) points at the player's head
        Quaternion rotation = Quaternion.LookRotation(player.position - position);

        Target prefab = Random.value < movingTargetChance ? movingTargetPrefab : targetPrefab;
        spawnedTargets.Add(Instantiate(prefab, position, rotation));
    }

    public void ClearTargets()
    {
        foreach (Target target in spawnedTargets)
        {
            if (target != null)
                Destroy(target.gameObject);
        }
        spawnedTargets.Clear();
    }
}
