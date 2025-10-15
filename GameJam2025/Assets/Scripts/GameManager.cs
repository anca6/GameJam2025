using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[AddComponentMenu("Game/Game Manager")]
public class GameManager : MonoBehaviour
{
    // -------- Singleton ----------
    public static GameManager Instance { get; private set; }

    [Header("Fade (Optional)")]
    [Tooltip("CanvasGroup used for fade in/out. Leave null to disable fading.")]
    public CanvasGroup fadeCanvasGroup;
    [Min(0f)] public float fadeDuration = 0.6f;
    public bool fadeOnLoad = true;

    [Header("Loading UI (Optional)")]
    [Tooltip("Shown while a scene is loading.")]
    public GameObject loadingScreen;
    [Tooltip("Optional slider to reflect loading progress (0-1).")]
    public Slider progressSlider;
    [Tooltip("Minimum time the loading UI should stay visible (seconds).")]
    [Min(0f)] public float minLoadingShowTime = 0.35f;

    [Header("Hotkeys (Optional)")]
    public bool enableHotkeys = false;
    public KeyCode reloadKey = KeyCode.F5;
    public KeyCode nextSceneKey = KeyCode.F6;

    [Header("Events")]
    public UnityEvent onLoadStarted;
    public UnityEvent onLoadFinished;

    [System.Serializable] public class FloatEvent : UnityEvent<float> { }
    [Tooltip("Invoked repeatedly with loading progress (0..1).")]
    public FloatEvent onLoadProgress;

    bool _isLoading;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Make sure fade starts hidden
        if (fadeCanvasGroup)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false;
        }

        if (loadingScreen) loadingScreen.SetActive(false);
    }

    void Update()
    {
        if (!enableHotkeys) return;
        if (Input.GetKeyDown(reloadKey)) ReloadCurrentScene();
        if (Input.GetKeyDown(nextSceneKey)) LoadNextSceneInBuild();
    }

    // ---------- Public API ----------

    public void LoadSceneByName(string sceneName)
    {
        if (!CanStartLoad()) return;
        StartCoroutine(LoadSceneRoutine(sceneName, LoadSceneMode.Single));
    }

    public void LoadSceneByIndex(int buildIndex)
    {
        if (!CanStartLoad()) return;
        var name = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        StartCoroutine(LoadSceneRoutine(buildIndex, LoadSceneMode.Single));
    }

    public void ReloadCurrentScene()
    {
        if (!CanStartLoad()) return;
        int idx = SceneManager.GetActiveScene().buildIndex;
        StartCoroutine(LoadSceneRoutine(idx, LoadSceneMode.Single));
    }

    public void LoadNextSceneInBuild()
    {
        if (!CanStartLoad()) return;
        int current = SceneManager.GetActiveScene().buildIndex;
        int count = SceneManager.sceneCountInBuildSettings;
        int next = (current + 1) % count;
        StartCoroutine(LoadSceneRoutine(next, LoadSceneMode.Single));
    }

    public void LoadAdditive(string sceneName)
    {
        if (!CanStartLoad()) return;
        StartCoroutine(LoadSceneRoutine(sceneName, LoadSceneMode.Additive));
    }

    public void UnloadAdditive(string sceneName)
    {
        if (_isLoading) return;
        StartCoroutine(UnloadRoutine(sceneName));
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- Internals ----------

    bool CanStartLoad()
    {
        if (_isLoading) return false;
        // (Optional) Validate scene exists in build here if you want.
        return true;
    }

    IEnumerator LoadSceneRoutine(string sceneNameOrIndex, LoadSceneMode mode)
    {
        _isLoading = true;
        onLoadStarted?.Invoke();
        ShowLoadingUI(true);

        if (fadeOnLoad) yield return Fade(1f);

        float visibleTimer = 0f;

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneNameOrIndex, mode);
        if (op == null)
        {
            Debug.LogError($"[GameManager] Load failed: {sceneNameOrIndex}");
            FinishLoading();
            yield break;
        }

        op.allowSceneActivation = true; // set false if you want to hold at 0.9f

        while (!op.isDone)
        {
            float p = Mathf.Clamp01(op.progress / 0.9f); // normalize to 0..1
            onLoadProgress?.Invoke(p);
            if (progressSlider) progressSlider.value = p;

            visibleTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        // Ensure the loading UI is visible for at least minLoadingShowTime
        while (visibleTimer < minLoadingShowTime)
        {
            visibleTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        if (fadeOnLoad) yield return Fade(0f);

        FinishLoading();
    }

    IEnumerator LoadSceneRoutine(int buildIndex, LoadSceneMode mode)
    {
        _isLoading = true;
        onLoadStarted?.Invoke();
        ShowLoadingUI(true);

        if (fadeOnLoad) yield return Fade(1f);

        float visibleTimer = 0f;

        AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, mode);
        if (op == null)
        {
            Debug.LogError($"[GameManager] Load failed: index {buildIndex}");
            FinishLoading();
            yield break;
        }

        op.allowSceneActivation = true;

        while (!op.isDone)
        {
            float p = Mathf.Clamp01(op.progress / 0.9f);
            onLoadProgress?.Invoke(p);
            if (progressSlider) progressSlider.value = p;

            visibleTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        while (visibleTimer < minLoadingShowTime)
        {
            visibleTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        if (fadeOnLoad) yield return Fade(0f);

        FinishLoading();
    }

    IEnumerator UnloadRoutine(string sceneName)
    {
        _isLoading = true;
        ShowLoadingUI(true);
        if (fadeOnLoad) yield return Fade(1f);

        AsyncOperation op = SceneManager.UnloadSceneAsync(sceneName);
        if (op != null)
        {
            while (!op.isDone)
            {
                float p = Mathf.Clamp01(op.progress / 0.9f);
                onLoadProgress?.Invoke(p);
                if (progressSlider) progressSlider.value = p;
                yield return null;
            }
        }
        else
        {
            Debug.LogWarning($"[GameManager] Unload failed or scene not loaded: {sceneName}");
        }

        if (fadeOnLoad) yield return Fade(0f);
        FinishLoading();
    }

    void FinishLoading()
    {
        ShowLoadingUI(false);
        _isLoading = false;
        onLoadProgress?.Invoke(1f);
        onLoadFinished?.Invoke();
    }

    void ShowLoadingUI(bool on)
    {
        if (loadingScreen) loadingScreen.SetActive(on);
        if (progressSlider && on) progressSlider.value = 0f;
        if (fadeCanvasGroup) fadeCanvasGroup.blocksRaycasts = on; // block clicks during fade/loading
    }

    IEnumerator Fade(float targetAlpha)
    {
        if (!fadeCanvasGroup) yield break;

        fadeCanvasGroup.blocksRaycasts = true;

        float start = fadeCanvasGroup.alpha;
        float t = 0f;

        // Use unscaled time so fade isn't affected by Time.timeScale
        while (!Mathf.Approximately(fadeCanvasGroup.alpha, targetAlpha))
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, fadeDuration);
            fadeCanvasGroup.alpha = Mathf.Lerp(start, targetAlpha, t);
            yield return null;
        }

        if (Mathf.Approximately(targetAlpha, 0f))
            fadeCanvasGroup.blocksRaycasts = false;
    }
}
