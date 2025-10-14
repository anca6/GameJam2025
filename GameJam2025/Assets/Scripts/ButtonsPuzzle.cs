using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ButtonsPuzzle : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Leave empty to use this object as the root. All children are treated as buttons.")]
    public Transform buttonsRoot;

    [Header("Start")]
    [Tooltip("Delay between trigger call and the puzzle actually starting.")]
    [Min(0f)] public float startDelaySeconds = 0f;
    public bool useUnscaledTimeForDelay = false;

    [Header("Gameplay")]
    [Min(1f)] public float timeLimitSeconds = 10f;
    public LayerMask clickMask = ~0;
    public float raycastMaxDistance = 100f;
    public bool useUnscaledTimeForTimer = false;

    [Header("Cursor")]
    public bool manageCursor = true;

    [Header("Events")]
    public UnityEvent OnSolved;
    public UnityEvent OnFailed;

    [Header("Debug / HUD")]
    public bool showTimerHud = true;
    public bool debugLogs = true;

    // internals
    readonly List<GameObject> _buttons = new();
    int _remaining;
    float _timeLeft;
    bool _running;
    bool _armedOrStarted;
    CursorLockMode _prevLock;
    bool _prevVisible;

    void OnDisable()
    {
        // don’t auto-restore cursor if you prefer global controller to handle it.
        _running = false;
        _armedOrStarted = false;
    }

    // ----------------- PUBLIC API -----------------

    /// <summary>Called by your pressure plate. Uses the serialized delay unless overrideDelay >= 0 is provided.</summary>
    public void TriggerStart(float overrideDelay = -1f)
    {
        if (_armedOrStarted) { if (debugLogs) Debug.Log("[ButtonsPuzzle] Already armed/started."); return; }
        _armedOrStarted = true;
        float delay = (overrideDelay >= 0f) ? overrideDelay : startDelaySeconds;
        StartCoroutine(Co_StartAfterDelay(delay));
        if (debugLogs) Debug.Log($"[ButtonsPuzzle] Triggered. Will start in {delay:0.00}s.");
    }

    // ----------------- CORE -----------------

    System.Collections.IEnumerator Co_StartAfterDelay(float delay)
    {
        if (delay > 0f)
        {
            if (useUnscaledTimeForDelay)
            {
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < delay) yield return null;
            }
            else
            {
                yield return new WaitForSeconds(delay);
            }
        }
        StartPuzzleNow();
    }

    void StartPuzzleNow()
    {
        if (!buttonsRoot) buttonsRoot = transform;

        // collect all children (even inactive)
        _buttons.Clear();
        foreach (var t in buttonsRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == buttonsRoot) continue;
            _buttons.Add(t.gameObject);
        }

        _remaining = 0;
        for (int i = 0; i < _buttons.Count; i++)
        {
            var go = _buttons[i];
            if (!go) continue;
            go.SetActive(true);
            _remaining++;
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
        {
            Debug.Log($"[ButtonsPuzzle] START → buttons:{_remaining}  time:{_timeLeft:0.00}s");
            for (int i = 0; i < _buttons.Count; i++)
                Debug.Log($"[ButtonsPuzzle]   [{i}] {_buttons[i]?.name}");
        }
    }

    void Update()
    {
        if (!_running) return;

        // timer
        float dt = useUnscaledTimeForTimer ? Time.unscaledDeltaTime : Time.deltaTime;
        _timeLeft -= dt;
        if (_timeLeft <= 0f) { Fail(); return; }

        // click to clear
        if (MouseDownThisFrame() && TryHitButton(out GameObject hitGo))
        {
            if (hitGo.activeSelf)
            {
                hitGo.SetActive(false);
                _remaining = Mathf.Max(0, _remaining - 1);
                if (debugLogs) Debug.Log($"[ButtonsPuzzle] Clicked '{hitGo.name}'. Remaining: {_remaining}");
                if (_remaining == 0) Solve();
            }
        }
    }

    void Solve()
    {
        if (!_running) return;
        _running = false;
        if (manageCursor) { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; }
        if (debugLogs) Debug.Log($"[ButtonsPuzzle] SOLVED with {_timeLeft:0.00}s left.");
        OnSolved?.Invoke();
    }

    void Fail()
    {
        if (!_running) return;
        _running = false;
        if (manageCursor) { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; }
        if (debugLogs) Debug.Log("[ButtonsPuzzle] FAILED (timer expired).");
        OnFailed?.Invoke();
    }

    // ----------------- helpers -----------------

    bool TryHitButton(out GameObject go)
    {
        go = null;
        var cam = Camera.main;
        if (!cam) { if (debugLogs) Debug.LogWarning("[ButtonsPuzzle] No Camera.main."); return false; }

        Ray ray = cam.ScreenPointToRay(GetMousePosition());
        if (Physics.Raycast(ray, out var hit, raycastMaxDistance, clickMask, QueryTriggerInteraction.Collide))
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (!b) continue;
                var tr = b.transform;
                if (hit.transform == tr || hit.transform.IsChildOf(tr)) { go = b; return true; }
            }
        }
        return false;
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

    public float GetTimeLeft() => Mathf.Max(0f, _timeLeft);
    public bool IsRunning() => _running;

    void OnGUI()
    {
        if (!showTimerHud || !_running) return;
        var label = $"Buttons: {_remaining}  |  Time: {_timeLeft:0.0}s";
        var size = GUI.skin.label.CalcSize(new GUIContent(label));
        var rect = new Rect(10, 10, size.x + 12, size.y + 8);
        var prev = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = prev; rect.x += 6; rect.y += 4;
        GUI.Label(rect, label);
    }
}
