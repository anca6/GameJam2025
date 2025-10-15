using FMOD.Studio;
using FMODUnity;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
#endif

public class ButtonsPuzzle : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Direct children of this transform are considered buttons.")]
    public Transform buttonsRoot;

    [Header("Start")]
    [Min(0f)] public float startDelaySeconds = 0f;
    public bool useUnscaledTimeForDelay = false;
    public bool deactivateChildrenOnAwake = true;

    [Header("Gameplay")]
    [Min(1f)] public float timeLimitSeconds = 10f;
    public bool useUnscaledTimeForTimer = false;
    public LayerMask clickMask = ~0;
    public float raycastMaxDistance = 100f;

    [Header("Random Activation")]
    [Tooltip("How many buttons to enable at start.")]
    public int minActiveButtons = 1;
    public int maxActiveButtons = 0;   // 0 = use total count
    public bool ensureAtLeastOne = true;

    [Header("Cursor")]
    public bool manageCursor = true;

    [Header("Raycast Camera")]
    [Tooltip("Drag the real Camera (with CinemachineBrain). Falls back to Camera.main.")]
    public Camera raycastCamera;

    [Header("Events")]
    public UnityEvent OnSolved;
    public UnityEvent OnFailed;

    [Header("Win → Return To Start")]
    [SerializeField] private bool returnToStartOnSolve = true;
    [SerializeField, Min(0f)] private float solveReturnDelay = 3f;
    [SerializeField] private string startSceneName = "StartScreen"; // set this to your title scene

    [Header("Fail - Reload")]
    [SerializeField] private bool reloadOnFail = true;
    [SerializeField, Min(0f)] private float reloadDelay = 1.5f;

    [Header("Debug / HUD")]
    public bool debugLogs = true;
    public bool showTimerHud = true;
    [SerializeField] private TextMeshProUGUI eventName;
    [SerializeField] private TextMeshProUGUI timerText;

    [Tooltip("Shown when solved (assign a GO with your 'Crisis averted' text).")]
    [SerializeField] private GameObject solvedMessageObject;

    [Header("Music (FMOD)")]
    [SerializeField] private EventReference musicEvent;     // drag your music event here
    [SerializeField] private string intensityParam = "Intensity";

    // Step-ups based on **time left** (in seconds) → what Intensity value to set
    [SerializeField] private float step1TimeLeft = 20f;  // when timeLeft <= 20s
    [SerializeField] private float step1Value = 1f;

    [SerializeField] private float step2TimeLeft = 10f;  // when timeLeft <= 10s
    [SerializeField] private float step2Value = 2f;

    [SerializeField] private float step3TimeLeft = 5f;   // when timeLeft <= 5s
    [SerializeField] private float step3Value = 3f;

    [SerializeField] private float startIntensity = 0f;  // at puzzle start

    [SerializeField] private EventReference failSound;

    // --- internals ---
    readonly List<Transform> _buttons = new();      // direct children only
    readonly HashSet<int> _active = new();          // indexes of active buttons
    readonly Dictionary<Transform, int> _indexOf = new();

    float _timeLeft;
    bool _running;
    bool _armed;

    CursorLockMode _prevLock;
    bool _prevVisible;

    EventInstance _music;
    EventInstance failSoundInstance;
    bool _musicCreated;

    void Awake()
    {
        if (!buttonsRoot) buttonsRoot = transform;
        if (!deactivateChildrenOnAwake) return;

        for (int i = 0; i < buttonsRoot.childCount; i++)
        {
            var child = buttonsRoot.GetChild(i)?.gameObject;
            if (child && child.activeSelf) child.SetActive(false);
        }

        SetRunUIVisible(false);
        SetEndMessagesVisible(false, false);

        _music.start();
    }

    void OnDisable()
    {
        _running = false;
        _armed = false;
        _active.Clear();
        _indexOf.Clear();

        // FMOD: make sure music stops if object gets disabled
       // StopAndReleaseMusic();                        // FMOD

        StopMusic();
    }

    // Call from your pressure plate
    public void TriggerStart(float overrideDelay = -1f)
    {
        if (_armed) { if (debugLogs) Debug.Log("[ButtonsPuzzle] Already armed/started."); return; }
        _armed = true;
        float d = (overrideDelay >= 0f) ? overrideDelay : startDelaySeconds;
        StartCoroutine(useUnscaledTimeForDelay ? CoDelayRealtime(d) : CoDelay(d));
    }

    System.Collections.IEnumerator CoDelay(float seconds)
    {
        if (seconds > 0f) yield return new WaitForSeconds(seconds);
        StartPuzzleNow();
    }
    System.Collections.IEnumerator CoDelayRealtime(float seconds)
    {
        if (seconds > 0f) yield return new WaitForSecondsRealtime(seconds);
        StartPuzzleNow();
    }

    void StartPuzzleNow()
    {
        if (!buttonsRoot) buttonsRoot = transform;

        // collect ONLY direct children (one GO per button)
        _buttons.Clear();
        _indexOf.Clear();
        for (int i = 0; i < buttonsRoot.childCount; i++)
        {
            var child = buttonsRoot.GetChild(i);
            _buttons.Add(child);
            _indexOf[child] = i;
        }

        int total = _buttons.Count;
        int max = (maxActiveButtons <= 0) ? total : Mathf.Clamp(maxActiveButtons, 0, total);
        int min = Mathf.Clamp(minActiveButtons, 0, max);
        if (ensureAtLeastOne && min == 0 && max == 0 && total > 0) min = 1;

        int target = (total == 0) ? 0 : Random.Range(min, Mathf.Max(min, max) + 1);

        // shuffle indices (Fisher–Yates)
        var idx = new List<int>(total);
        for (int i = 0; i < total; i++) idx.Add(i);
        for (int i = 0; i < total; i++)
        {
            int j = Random.Range(i, total);
            (idx[i], idx[j]) = (idx[j], idx[i]);
        }

        // choose active subset + apply states
        _active.Clear();
        for (int k = 0; k < total; k++)
        {
            bool on = (k < target);
            int id = idx[k];
            var t = _buttons[id];
            if (t) t.gameObject.SetActive(on);
            if (on) _active.Add(id);
        }

        _timeLeft = Mathf.Max(0.01f, timeLimitSeconds);
        _running = true;

        SetRunUIVisible(true);
        SetEndMessagesVisible(false, false);
        UpdateTimerUI(); // initial value

        if (manageCursor)
        {
            _prevLock = Cursor.lockState;
            _prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (debugLogs)
            Debug.Log($"[ButtonsPuzzle] START → total:{total}  active:{_active.Count}  time:{_timeLeft:0.00}s");

        // start FMOD music
        if (!musicEvent.IsNull && !_musicCreated)
        {
            _music = RuntimeManager.CreateInstance(musicEvent);
            // music can be 2D; attaching is fine too
            RuntimeManager.AttachInstanceToGameObject(_music, transform, GetComponent<Rigidbody>());
            _music.setParameterByName(intensityParam, startIntensity);
            _music.start();
            _musicCreated = true;
        }
    }

    void Update()
    {
        if (!_running) return;

        _timeLeft -= useUnscaledTimeForTimer ? Time.unscaledDeltaTime : Time.deltaTime;
        if (_timeLeft <= 0f) { Fail(); return; }

        UpdateTimerUI();

        UpdateMusicIntensity();

        if (MouseDownThisFrame() && TryGetHitButtonIndex(out int i))
        {
            if (_active.Remove(i))
            {
                var tr = _buttons[i];
                if (tr) tr.gameObject.SetActive(false);
                if (debugLogs) Debug.Log($"[ButtonsPuzzle] Cleared '{tr?.name}'. Remaining: {_active.Count}");
                if (_active.Count == 0) Solve();
            }
        }
    }

    void UpdateMusicIntensity()
    {
        if (!_musicCreated) return;

        float val = startIntensity;

        // NOTE: these are "time left" breakpoints. Smallest time wins.
        if (_timeLeft <= step3TimeLeft) val = step3Value;
        else if (_timeLeft <= step2TimeLeft) val = step2Value;
        else if (_timeLeft <= step1TimeLeft) val = step1Value;

        _music.setParameterByName(intensityParam, val);
    }

    void StopMusic()
    {
        if (!_musicCreated) return;
        _music.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _music.release();
        _musicCreated = false;
    }



    void Solve()
    {
        if (!_running) return;
        _running = false;
        RestoreCursor();

        SetRunUIVisible(false);
        SetEndMessagesVisible(true, false); // show success text

        if (debugLogs) Debug.Log($"[ButtonsPuzzle] SOLVED with {_timeLeft:0.00}s left.");
        OnSolved?.Invoke();

        if (returnToStartOnSolve)
            StartCoroutine(CoReturnToStartAfterDelay(solveReturnDelay));

        StopMusic();
    }


    void Fail()
    {
        if (!_running) return;
        _running = false;
        RestoreCursor();

        SetRunUIVisible(false);
        SetEndMessagesVisible(false, true); //  show fail, not success

        if (debugLogs) Debug.Log("[ButtonsPuzzle] FAILED (timer expired).");
        OnFailed?.Invoke();

        if (reloadOnFail)
            StartCoroutine(CoReloadAfterDelay(reloadDelay));

        StopMusic();

        // start FMOD music
        if (!failSound.IsNull)
        {
            failSoundInstance = RuntimeManager.CreateInstance(failSound);
            RuntimeManager.AttachInstanceToGameObject(failSoundInstance, transform, GetComponent<Rigidbody>());
            failSoundInstance.start();
        }
    }

    System.Collections.IEnumerator CoReloadAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        if (GameManager.Instance) GameManager.Instance.ReloadCurrentScene();
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    System.Collections.IEnumerator CoReturnToStartAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        if (GameManager.Instance && !string.IsNullOrWhiteSpace(startSceneName))
            GameManager.Instance.LoadSceneByName(startSceneName);
        else if (!string.IsNullOrWhiteSpace(startSceneName))
            SceneManager.LoadScene(startSceneName);
    }

    void RestoreCursor()
    {
        if (!manageCursor) return;
        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;
    }

    // --- helpers ---

    bool TryGetHitButtonIndex(out int idx)
    {
        idx = -1;
        var cam = raycastCamera ? raycastCamera : Camera.main;
        if (!cam) return false;

        Ray ray = cam.ScreenPointToRay(GetMousePosition());
        if (!Physics.Raycast(ray, out var hit, raycastMaxDistance, clickMask, QueryTriggerInteraction.Collide))
            return false;

        // climb to direct child under buttonsRoot
        var tr = hit.transform;
        while (tr && tr.parent && tr.parent != buttonsRoot) tr = tr.parent;
        if (!tr || tr.parent != buttonsRoot) return false;

        // fast lookup
        return _indexOf.TryGetValue(tr, out idx);
    }

    bool MouseDownThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    Vector2 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    // ---------- UI helpers ----------
    void SetRunUIVisible(bool visible)
    {
        if (eventName) eventName.gameObject.SetActive(visible);
        if (timerText) timerText.gameObject.SetActive(visible);
    }

    void SetEndMessagesVisible(bool solvedOn, bool failedOn)
    {
        if (solvedMessageObject) solvedMessageObject.SetActive(solvedOn);
        // if (failedMessageObject) failedMessageObject.SetActive(failedOn);
    }

    void UpdateTimerUI()
    {
        if (!timerText) return;

        timerText.text = _timeLeft.ToString("0.0");

    }
}
