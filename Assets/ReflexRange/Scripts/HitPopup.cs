using TMPro;
using UnityEngine;

/// <summary>
/// Floating "+30" text that appears where a target was hit, rises and fades out.
/// </summary>
[RequireComponent(typeof(TextMeshPro))]
public class HitPopup : MonoBehaviour
{
    [SerializeField] float lifetime = 0.7f;
    [SerializeField] float riseSpeed = 1.2f;   // metres per second

    TextMeshPro text;
    float age;

    public void Show(string message, Color color)
    {
        text = GetComponent<TextMeshPro>();
        text.text = message;
        text.color = color;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;
        text.alpha = 1f - age / lifetime;
    }
}
