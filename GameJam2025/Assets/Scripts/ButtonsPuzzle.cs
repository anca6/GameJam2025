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

    [Header("Gameplay")]
    [Min(1f)] public float timeLimitSeconds = 10f;
    public bool autoStartOnEnable = true;
    public LayerMask clickMask = ~0;          // optional: set to a "Buttons" layer
    public float raycastMaxDistance = 100f;
    public bool useUnscaledTime = false;      // set true if your game pauses Time.timeScale

    [Header("Cursor")]
    public bool manageCursor = true;

    [Header("Events")]
    public UnityEvent OnSolved;
    public UnityEvent OnFailed;

    [Header("Debug / HUD")]
    public bool showTimerHud = true;          // in-Game view timer
    public bool debugLogs = true;             // master switch
    [Tooltip("Log a timer heartbeat every X seconds (0 = off)")]
    public float timerLogEverySeconds = 1f;

    // internals
    readonly List<GameObject> _buttons = new();
    int _remaining;
    float _timeLeft;
    bool _running;

    CursorLockMode _prevLock;
    bool _prevVisible;
    float _nextTimerLogAt;

    void OnEnable()
    {
        if (autoStartOnEnable) StartPuzzle();
    }

    void OnDisable()
    {
        //RestoreCursor();
        _running = false;
        if (debugLogs) Debug.Log($"[ButtonsPuzzle] Disabled. Running={_running}");
    }

    /// <summary>Call this to (re)start the puzzle.</summary>
    public void StartPuzzle()
    {
        if (!buttonsRoot) buttonsRoot = transform;

        // collect ALL children (even inactive)
        _buttons.Clear();
        foreach (var t in buttonsRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == buttonsRoot) continue;
            _buttons.Add(t.gameObject);
        }

        // activate everything and count
        _remaining = 0;
        for (int i = 0; i < _buttons.Count; i++)
        {
            var go = _buttons[i];
            if (!go) continue;
            go.SetActive(true);
            _remaining++;
        }

        // timer + cursor
        _timeLeft = Mathf.Max(0.01f, timeLimitSeconds);
        _running = true;
        _nextTimerLogAt = Time.time + Mathf.Max(0f, timerLogEverySeconds);

        if (manageCursor)
        {
            _prevLock = Cursor.lockState;
            _prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (debugLogs)
        {
            Debug.Log($"[ButtonsPuzzle] StartPuzzle → found {_buttons.Count} children under '{buttonsRoot.name}', activated {_remaining}, time={_timeLeft:0.00}s");
            for (int i = 0; i < _buttons.Count; i++)
            {
                var name = _buttons[i] ? _buttons[i].name : "(null)";
                Debug.Log($"[ButtonsPuzzle]   [{i}] {name}");
            }
        }
    }

    void Update()
    {
        if (!_running) return;

        // countdown
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _timeLeft -= dt;

        if (timerLogEverySeconds > 0f && Time.time >= _nextTimerLogAt)
        {
            _nextTimerLogAt = Time.time + timerLogEverySeconds;
            if (debugLogs) Debug.Log($"[ButtonsPuzzle] Timer: {_timeLeft:0.00}s left | Remaining: {_remaining}");
        }

        if (_timeLeft <= 0f)
        {
            Fail();
            return;
        }

        // click to clear
        if (MouseDownThisFrame())
        {
            if (TryHitButton(out GameObject hitGo))
            {
                if (hitGo.activeSelf)
                {
                    if (debugLogs) Debug.Log($"[ButtonsPuzzle] Clicked '{hitGo.name}' ✓");
                    hitGo.SetActive(false);
                    _remaining = Mathf.Max(0, _remaining - 1);
                    if (debugLogs) Debug.Log($"[ButtonsPuzzle] Remaining after click: {_remaining}");
                    if (_remaining == 0) Solve();
                }
                else if (debugLogs)
                {
                    Debug.Log($"[ButtonsPuzzle] Clicked '{hitGo.name}' but it was already inactive.");
                }
            }
            else if (debugLogs)
            {
                Debug.Log("[ButtonsPuzzle] Click miss (no valid button under cursor).");
            }
        }
    }

    void Solve()
    {
        if (!_running) return;
        _running = false;
        //RestoreCursor();
        if (debugLogs) Debug.Log($"[ButtonsPuzzle] SOLVED with {_timeLeft:0.00}s left 🎉");
        OnSolved?.Invoke();
    }

    void Fail()
    {
        if (!_running) return;
        _running = false;
        //RestoreCursor();
        if (debugLogs) Debug.Log("[ButtonsPuzzle] FAILED (timer expired)");
        OnFailed?.Invoke();
    }

    bool TryHitButton(out GameObject go)
    {
        go = null;
        var cam = Camera.main;
        if (!cam)
        {
            if (debugLogs) Debug.LogWarning("[ButtonsPuzzle] No Camera.main found for raycast.");
            return false;
        }

        Ray ray = cam.ScreenPointToRay(GetMousePosition());
        if (Physics.Raycast(ray, out var hit, raycastMaxDistance, clickMask, QueryTriggerInteraction.Collide))
        {
            if (debugLogs) Debug.Log($"[ButtonsPuzzle] Raycast hit '{hit.transform.name}' on layer {hit.transform.gameObject.layer}");
            // ensure the hit object is one of our buttons (or a child of one)
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (!b) continue;
                var tr = b.transform;
                if (hit.transform == tr || hit.transform.IsChildOf(tr))
                {
                    go = b;
                    return true;
                }
            }
            if (debugLogs) Debug.Log("[ButtonsPuzzle] Hit something, but it’s not in my buttons list.");
        }
        else if (debugLogs)
        {
            Debug.Log("[ButtonsPuzzle] Raycast missed everything.");
        }
        return false;
    }

    bool MouseDownThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        bool pressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (debugLogs && pressed) Debug.Log("[ButtonsPuzzle] LMB down (New Input System).");
        return pressed;
#else
        bool pressed = Input.GetMouseButtonDown(0);
        if (debugLogs && pressed) Debug.Log("[ButtonsPuzzle] LMB down (Old Input Manager).");
        return pressed;
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

    // optional: call from other scripts to query remaining time
    public float GetTimeLeft() => Mathf.Max(0f, _timeLeft);
    public bool IsRunning() => _running;

    // handy in Play mode: right-click component header S

}
