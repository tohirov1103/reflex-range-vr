using UnityEngine;

/// <summary>
/// Anything the pistol can hit implements this interface (normal targets, the START target).
/// The pistol does not need to know what it hit - it just calls OnShot.
/// </summary>
public interface IShootable
{
    void OnShot(Vector3 hitPoint);
}
