using UnityEngine;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ButtonsPuzzle : MonoBehaviour, IEventPuzzle
{
    [Header("Hierarchy")]
    [Tooltip("Leave empty to use this GameObject as the root.")]
    public Transform buttonsRoot;

    [Header("Popcorn spawn")]
    [Tooltip("All buttons will activate within this window, in random order.")]
    public float spawnWindowSeconds = 1.25f;
    public float perButtonMinDelay = 0.05f;
    public float perButtonMaxDelay = 0.25f;
    [Tooltip("If true, spawn timing ignores Time.timeScale.")]
    public bool useUnscaledTime = true;

    [Header("Click detection")]
    public LayerMask clickMask = ~0;
    public float raycastMaxDistance = 100f;

    [Header("Cursor")]
    public bool manageCursor = true;

    public event System.Action OnSolved;

    struct Btn { public GameObject go; public bool on; }
    List<Btn> _btns = new();
    int _remaining;
    bool _running;

    CursorLockMode _prevLock;
    bool _prevVisible;

    public void InitPuzzle()
    {
        if (!buttonsRoot) buttonsRoot = transform;

        // Collect ALL child objects (even inactive) — no collider requirement
        _btns.Clear();
        var all = buttonsRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t == buttonsRoot) continue;
            _btns.Add(new Btn { go = t.gameObject, on = false });
        }

        // Hide all & reset
        for (int i = 0; i < _btns.Count; i++)
            if (_btns[i].go) _btns[i].go.SetActive(false);

        _remaining = 0;
        _running = true;

        if (manageCursor)
        {
            _prevLock = Cursor.lockState;
            _prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        Debug.Log($"[ButtonsPuzzle] Init. Found {_btns.Count} button objects under '{buttonsRoot.name}'.");
        StartCoroutine(PopcornActivate());
    }

    System.Collections.IEnumerator PopcornActivate()
    {
        if (_btns.Count == 0)
        {
            Debug.LogWarning("[ButtonsPuzzle] No child buttons found to activate.");
            yield break;
        }

        // Shuffle
        var order = new List<int>(_btns.Count);
        for (int i = 0; i < _btns.Count; i++) order.Add(i);
        for (int i = 0; i < order.Count; i++)
        {
            int j = Random.Range(i, order.Count);
            (order[i], order[j]) = (order[j], order[i]);
        }

        float start = useUnscaledTime ? Time.unscaledTime : Time.time;
        for (int k = 0; k < order.Count; k++)
        {
            float tNorm = (order.Count == 1) ? 0f : (k / (float)(order.Count - 1));
            float baseDelay = tNorm * Mathf.Max(0f, spawnWindowSeconds);
            float jitter = Random.Range(perButtonMinDelay, perButtonMaxDelay);
            float targetDelay = Mathf.Max(0f, baseDelay + jitter);

            // wait using scaled or unscaled time
            while (true)
            {
                float now = useUnscaledTime ? Time.unscaledTime : Time.time;
                float elapsed = now - start;
                if (elapsed >= targetDelay) break;
                yield return null;
            }

            if (!_running) yield break;

            int idx = order[k];
            var b = _btns[idx];
            if (b.go && !b.on)
            {
                b.on = true;
                b.go.SetActive(true);
                _btns[idx] = b;
                _remaining++;
            }
        }

        Debug.Log($"[ButtonsPuzzle] Activated {_remaining} buttons.");
    }

    void Update()
    {
        if (!_running) return;

        if (MouseDownThisFrame() && TryHitActiveButton(out int idx))
        {
            var b = _btns[idx];
            if (b.on)
            {
                b.on = false;
                if (b.go) b.go.SetActive(false);
                _btns[idx] = b;
                _remaining--;

                if (_remaining <= 0)
                    Complete();
            }
        }
    }

    void Complete()
    {
        if (!_running) return;
        _running = false;
        RestoreCursor();
        OnSolved?.Invoke();
    }

    void OnDisable() => RestoreCursor();

    void RestoreCursor()
    {
        if (!manageCursor) return;
        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;
    }

    bool MouseDownThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    bool TryHitActiveButton(out int idx)
    {
        idx = -1;
        var cam = Camera.main;
        if (!cam) return false;

        Ray ray = cam.ScreenPointToRay(GetMousePosition());
        if (Physics.Raycast(ray, out var hit, raycastMaxDistance, clickMask, QueryTriggerInteraction.Collide))
        {
            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i];
                if (!b.on || !b.go) continue;
                var tr = b.go.transform;
                if (hit.transform == tr || hit.transform.IsChildOf(tr))
                {
                    idx = i;
                    return true;
                }
            }
        }
        return false;
    }

    Vector2 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    // 🧪 Right-click the component header → "Force Activate All" to sanity check
    [ContextMenu("Force Activate All")]
    void ForceActivateAll()
    {
        if (_btns.Count == 0)
        {
            InitPuzzle();
        }
        foreach (var b in _btns)
            if (b.go) b.go.SetActive(true);
        Debug.Log("[ButtonsPuzzle] Force-activated all children.");
    }
}
