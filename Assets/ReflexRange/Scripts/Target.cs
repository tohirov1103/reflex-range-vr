using UnityEngine;

/// <summary>
/// A round target. It pops up, waits a few seconds and disappears if nobody hits it.
/// Hits closer to the centre score more: outer ring x1, middle ring x2, bullseye x3.
/// </summary>
public class Target : MonoBehaviour, IShootable
{
    [SerializeField] int points = 10;
    [SerializeField] float radius = 0.35f;     // radius of the target face in metres
    [SerializeField] float lifetime = 3.5f;    // seconds before it disappears on its own
    [SerializeField] float popUpSpeed = 6f;

    float spawnTime;

    void Start()
    {
        spawnTime = Time.time;                 // used to measure the player's reaction time
        transform.localScale = Vector3.zero;   // grows to full size in Update
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        // Pop-up animation from size 0 to size 1
        transform.localScale = Vector3.MoveTowards(transform.localScale, Vector3.one, popUpSpeed * Time.deltaTime);
    }

    public void OnShot(Vector3 hitPoint)
    {
        float distanceFromCentre = Vector3.Distance(hitPoint, transform.position);

        int multiplier = 1;
        if (distanceFromCentre < radius / 3f)
            multiplier = 3;
        else if (distanceFromCentre < radius * 2f / 3f)
            multiplier = 2;

        float reactionTime = Time.time - spawnTime;
        GameManager.Instance.AddScore(points, multiplier, hitPoint, reactionTime);
        Destroy(gameObject);
    }
}
