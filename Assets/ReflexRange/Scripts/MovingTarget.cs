using UnityEngine;

/// <summary>
/// Slides a target left and right so it is harder to hit.
/// Added to the "Moving Target" prefab next to the Target script.
/// </summary>
public class MovingTarget : MonoBehaviour
{
    [SerializeField] float speed = 1.5f;      // metres per second
    [SerializeField] float distance = 1.5f;   // how far it moves away from where it spawned

    Vector3 startPosition;
    float offset;
    int direction = 1;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        // Multiplying by Time.deltaTime makes the speed the same at 72, 90 or 120 frames per second
        offset += direction * speed * Time.deltaTime;

        if (Mathf.Abs(offset) >= distance)
        {
            offset = Mathf.Clamp(offset, -distance, distance);
            direction = -direction;
        }

        transform.position = startPosition + transform.right * offset;
    }
}
