using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// The pistol.
/// Grip button  = select   = pick it up (done by XRGrabInteractable).
/// Trigger      = activate = shoot (done here).
/// The shot is a raycast from the muzzle, so it hits instantly ("hitscan").
/// </summary>
[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
public class Pistol : MonoBehaviour
{
    [SerializeField] Transform muzzle;          // tip of the barrel; shots go along its blue (Z) axis
    [SerializeField] LineRenderer laser;        // short flash that shows where the shot went
    [SerializeField] GameObject muzzleFlash;
    [SerializeField] AudioSource audioSource;
    [SerializeField] float range = 100f;
    [SerializeField] float timeBetweenShots = 0.15f;
    [SerializeField] float returnDelay = 2f;    // seconds before a dropped pistol goes back to the table

    XRGrabInteractable grabInteractable;
    Rigidbody body;
    Vector3 tablePosition;
    Quaternion tableRotation;
    float nextShotTime;
    Coroutine returnRoutine;
    GameObject hiddenControllerModel;

    void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();

        // Remember where the pistol starts, so we can put it back if it is dropped
        tablePosition = transform.position;
        tableRotation = transform.rotation;

        laser.enabled = false;
        muzzleFlash.SetActive(false);
    }

    void OnEnable()
    {
        grabInteractable.activated.AddListener(OnTriggerPulled);
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);
    }

    void OnDisable()
    {
        grabInteractable.activated.RemoveListener(OnTriggerPulled);
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }

    void OnTriggerPulled(ActivateEventArgs args)
    {
        if (Time.time < nextShotTime)
            return;
        nextShotTime = Time.time + timeBetweenShots;

        Shoot();

        // Short vibration in the controller that holds the pistol
        if (args.interactorObject is XRBaseInputInteractor controller)
            controller.SendHapticImpulse(0.5f, 0.08f);
    }

    void Shoot()
    {
        GameManager.Instance.RegisterShot();

        Vector3 endPoint = muzzle.position + muzzle.forward * range;

        // Invisible ray from the muzzle. Trigger colliders are ignored.
        if (Physics.Raycast(muzzle.position, muzzle.forward, out RaycastHit hit, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;

            IShootable shootable = hit.collider.GetComponentInParent<IShootable>();
            if (shootable != null)
                shootable.OnShot(hit.point);
            else
                GameManager.Instance.RegisterMiss();
        }
        else
        {
            GameManager.Instance.RegisterMiss();
        }

        audioSource.PlayOneShot(SoundMaker.Shot);
        StartCoroutine(ShowShotEffects(endPoint));
    }

    IEnumerator ShowShotEffects(Vector3 endPoint)
    {
        laser.SetPosition(0, muzzle.position);
        laser.SetPosition(1, endPoint);
        laser.enabled = true;
        muzzleFlash.SetActive(true);

        yield return new WaitForSeconds(0.05f);

        laser.enabled = false;
        muzzleFlash.SetActive(false);
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        if (returnRoutine != null)
            StopCoroutine(returnRoutine);

        // Hide the controller model while it holds the pistol, so the two do not overlap
        hiddenControllerModel = FindControllerModel(args.interactorObject.transform);
        if (hiddenControllerModel != null)
            hiddenControllerModel.SetActive(false);
    }

    void OnReleased(SelectExitEventArgs args)
    {
        if (hiddenControllerModel != null)
            hiddenControllerModel.SetActive(true);
        hiddenControllerModel = null;

        returnRoutine = StartCoroutine(ReturnToTable());
    }

    // Walks up from the hand's interactor to find the "... Controller Visual" object of that controller
    static GameObject FindControllerModel(Transform interactor)
    {
        for (Transform parent = interactor; parent != null; parent = parent.parent)
        {
            foreach (Transform child in parent)
            {
                if (child.name.EndsWith("Controller Visual"))
                    return child.gameObject;
            }
        }
        return null;
    }

    IEnumerator ReturnToTable()
    {
        yield return new WaitForSeconds(returnDelay);

        if (grabInteractable.isSelected)
            yield break;

        // Stop the physics movement and put the pistol back on the table
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(tablePosition, tableRotation);
    }
}
