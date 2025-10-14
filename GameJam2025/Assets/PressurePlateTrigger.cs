using FMODUnity;
using Unity.Cinemachine;
using UnityEngine;
using BlendStyle = Unity.Cinemachine.CinemachineBlendDefinition.Styles;

[RequireComponent(typeof(Collider))]
public class PressurePlateTrigger : MonoBehaviour
{
    [Header("Refs")]
    public FirstPersonController playerController;
    public CinemachineCamera gameplayCam;
    public CinemachineCamera camA;
    public CinemachineCamera camB;
    [Tooltip("Main Camera’s CinemachineBrain")]
    public CinemachineBrain brain;

    [Header("Events")]
    public EventManagerGame eventManager;
    public bool startEventsAfterLock = true;

    [Header("Behaviour")]
    public KeyCode toggleKey = KeyCode.E;
    public bool startWithCamA = true;
    public int activePriority = 20;
    public int inactivePriority = 10;
    public bool zeroOutVelocity = true;

    [Header("Jerky switch animation (A <-> B)")]
    public bool enableJerky = true;
    [Tooltip("Total duration of the wobble around the cut.")]
    public float jerkyDuration = 0.085f;
    [Tooltip("Max Z-tilt (Dutch) in degrees at the peak of the wobble.")]
    public float jerkyMaxAngle = 1.75f;
    [Tooltip("What fraction happens BEFORE the cut (rest happens after).")]
    [Range(0.0f, 0.8f)] public float preCutPortion = 0.25f;

    // internals
    Rigidbody _rb;
    bool _playerInside;
    bool _locked;
    CinemachineCamera _active;
    int _origPriGameplay, _origPriA, _origPriB;
    CinemachineBlendDefinition _origBlend;
    bool _hasOrigBlend;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void Awake()
    {
        if (!playerController) playerController = GetComponentInParent<FirstPersonController>();
        if (playerController) _rb = playerController.GetComponent<Rigidbody>();

        if (!brain)
        {
            var mainCam = Camera.main;
            if (mainCam) brain = mainCam.GetComponent<CinemachineBrain>();
        }

        if (gameplayCam) _origPriGameplay = gameplayCam.Priority;
        if (camA) _origPriA = camA.Priority;
        if (camB) _origPriB = camB.Priority;

        if (brain)
        {
            _origBlend = brain.DefaultBlend;
            _hasOrigBlend = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInside = true;
        LockAndShowInitial();
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInside = false;
        // stays locked until Unlock()
    }

    void Update()
    {
        if (!_locked || !_playerInside) return;
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    bool IsPlayer(Collider c)
    {
        return playerController &&
               c.attachedRigidbody &&
               c.attachedRigidbody.transform.root == playerController.transform;
        // or: return c.CompareTag("Player");
    }

    void LockAndShowInitial()
    {
        if (_locked || playerController == null || camA == null || camB == null || gameplayCam == null) return;

        _locked = true;
        playerController.playerCanMove = false;
        playerController.cameraCanMove = false;

        if (zeroOutVelocity && _rb)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        gameplayCam.Priority = inactivePriority;
        _active = startWithCamA ? camA : camB;
        ApplyPriorities(_active);

        // Start events now (each will arm and countdown with its own 3–5s delay)
        if (startEventsAfterLock && eventManager)
            eventManager.TriggerRandomEvents();
    }

    void Toggle()
    {
        if (_active == null) return;
        var next = (_active == camA) ? camB : camA;

        if (enableJerky)
            StartCoroutine(ToggleWithJerky(_active, next));
        else
            StartCoroutine(CutOnceThenRestore(() => { _active = next; ApplyPriorities(_active); }));
    }

    void ApplyPriorities(CinemachineCamera active)
    {
        camA.Priority = (active == camA) ? activePriority : inactivePriority;
        camB.Priority = (active == camB) ? activePriority : inactivePriority;
    }

    public void Unlock()
    {
        if (!_locked) return;
        _locked = false;

        if (brain && _hasOrigBlend)
            brain.DefaultBlend = _origBlend;

        if (gameplayCam) gameplayCam.Priority = _origPriGameplay;
        if (camA) camA.Priority = _origPriA;
        if (camB) camB.Priority = _origPriB;

        if (playerController)
        {
            playerController.playerCanMove = true;
            playerController.cameraCanMove = true;
        }
    }

    // --- helpers ---

    // The "jerky animation": small pre-cut wobble on the current cam, instant cut, quick settle on new cam.
    System.Collections.IEnumerator ToggleWithJerky(CinemachineCamera fromCam, CinemachineCamera toCam)
    {
        // Randomize wobble direction for variety
        float dir = (Random.value < 0.5f) ? -1f : 1f;

        // durations
        float preT = Mathf.Max(0f, jerkyDuration * preCutPortion);
        float postT = Mathf.Max(0f, jerkyDuration - preT);

        // transforms + starting rotations
        var fromTf = fromCam.transform;
        var toTf = toCam.transform;
        Quaternion fromStart = fromTf.localRotation;
        Quaternion toStart = toTf.localRotation;

        // PRE-CUT wobble on the current (from) cam
        float t = 0f;
        while (t < preT)
        {
            t += Time.deltaTime;
            float p = (preT <= 0f) ? 1f : Mathf.Clamp01(t / preT);
            // Fast spike then partial decay
            float spike = 1f - (p * 0.7f); // leaves a bit of wobble going into the cut
            float angle = jerkyMaxAngle * dir * spike;
            fromTf.localRotation = fromStart * Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
        // ensure we end from-cam in a slightly offset pose at the instant of the cut
        fromTf.localRotation = fromStart * Quaternion.Euler(0f, 0f, jerkyMaxAngle * dir * 0.3f);

        // INSTANT CUT (one frame) while Brain is set to CUT
        yield return CutOnceThenRestore(() =>
        {
            _active = toCam;
            ApplyPriorities(_active);
        });

       // POST-CUT settle wobble on the new (to) cam
        t = 0f;
        while (t < postT)
        {
            t += Time.deltaTime;
            float p = (postT <= 0f) ? 1f : Mathf.Clamp01(t / postT);
            // Decay to zero (crunchy)
            float decay = 1f - (p * 0.85f);
            float angle = jerkyMaxAngle * dir * decay;
            toTf.localRotation = toStart * Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
        toTf.localRotation = toStart; // clean reset
        fromTf.localRotation = fromStart; // clean reset
    }

    System.Collections.IEnumerator CutOnceThenRestore(System.Action swapAction)
    {
        if (brain && _hasOrigBlend)
        {
            var saved = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(BlendStyle.Cut, 0f);

            swapAction?.Invoke(); // priorities while CUT is active

            // Make sure the Brain processes the cut this frame
            yield return new WaitForEndOfFrame();

            brain.DefaultBlend = saved;
        }
        else
        {
            swapAction?.Invoke();
            yield break;
        }
    }
}
