using UnityEngine;

/// <summary>
/// The big green target. Shooting it starts a new round, so the player never needs a menu.
/// </summary>
public class StartTarget : MonoBehaviour, IShootable
{
    public void OnShot(Vector3 hitPoint)
    {
        GameManager.Instance.StartRound();
    }
}
