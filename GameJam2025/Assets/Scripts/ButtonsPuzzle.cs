using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
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

    [Header("Debug / HUD")]
    public bool debugLogs = true;
    public bool showTimerHud = true;

    // --- internals ---
    readonly List<Transform> _buttons = new();      // direct children only
    readonly HashSet<int> _active = new();          // indexes of active buttons
    readonly Dictionary<Transform, int> _indexOf = new();

    float _timeLeft;
    bool _running;
    bool _armed;

    CursorLockMode _prevLock;
    bool _prevVisible;

    void Awake()
    {
        if (!buttonsRoot) buttonsRoot = transform;
        if (!deactivateChildrenOnAwake) return;

        for (int i = 0; i < buttonsRoot.childCount; i++)
        {
            var child = buttonsRoot.GetChild(i)?.gameObject;
            if (child && child.activeSelf) child.SetActive(false);
        }
    }

    void OnDisable()
    {
        _running = false;
        _armed = false;
        _active.Clear();
        _indexOf.Clear();
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

        if (manageCursor)
        {
            _prevLock = Cursor.lockState;
            _prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (debugLogs)
            Debug.Log($"[ButtonsPuzzle] START → total:{total}  active:{_active.Count}  time:{_timeLeft:0.00}s");
    }

    void Update()
    {
        if (!_running) return;

        _timeLeft -= useUnscaledTimeForTimer ? Time.unscaledDeltaTime : Time.deltaTime;
        if (_timeLeft <= 0f) { Fail(); return; }

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

    void Solve()
    {
        if (!_running) return;
        _running = false;
        RestoreCursor();
        if (debugLogs) Debug.Log($"[ButtonsPuzzle] SOLVED with {_timeLeft:0.00}s left.");
        OnSolved?.Invoke();
    }

    void Fail()
    {
        if (!_running) return;
        _running = false;
        RestoreCursor();
        if (debugLogs) Debug.Log("[ButtonsPuzzle] FAILED (timer expired).");
        OnFailed?.Invoke();
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

    void OnGUI()
    {
        if (!showTimerHud || !_running) return;
        var label = $"Active: {_active.Count}  |  Time: {_timeLeft:0.0}s";
        var size = GUI.skin.label.CalcSize(new GUIContent(label));
        var rect = new Rect(10, 10, size.x + 12, size.y + 8);

        var prev = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = prev;

        rect.x += 6; rect.y += 4;
        GUI.Label(rect, label);
    }
}
