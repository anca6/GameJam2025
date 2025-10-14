using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class EventInstance : MonoBehaviour
{
    public EventDefinition definition;
    public Transform puzzleAnchor;

    [Header("UI (optional)")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI timerText;

    [Header("Hooks for SFX/VFX")]
    public UnityEvent OnEventStarted;
    public UnityEvent OnEventSolved;
    public UnityEvent OnEventFailed;

    private float timeLeft;
    private bool running;
    private IEventPuzzle puzzle;

    public System.Action<EventInstance> OnSolvedInternal;
    public System.Action<EventInstance> OnFailedInternal;

    public void Begin(EventDefinition def)
    {
        definition = def;
        timeLeft = def.durationSeconds;
        running = false;                   // <-- don't auto-start
        if (titleText) titleText.text = def.uiTitle;

        var go = Instantiate(def.puzzlePrefab, puzzleAnchor);
        puzzle = go.GetComponent<IEventPuzzle>();
        puzzle.OnSolved += HandleSolved;
        puzzle.InitPuzzle();

        OnEventStarted?.Invoke();
    }

    public void StartCountdown()
    {
        timeLeft = definition != null ? definition.durationSeconds : timeLeft;
        running = true;
    }

    public void StartCountdownDelayed(float delaySeconds)
    {
        StartCoroutine(StartCountdownRoutine(delaySeconds));
    }

    private System.Collections.IEnumerator StartCountdownRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        StartCountdown();
    }

    void Update()
    {
        if (!running) return;
        timeLeft -= Time.deltaTime;

        if (timerText) timerText.text = Mathf.CeilToInt(timeLeft).ToString();

        if (timeLeft <= 0f)
        {
            running = false;
            OnEventFailed?.Invoke();
            OnFailedInternal?.Invoke(this);
        }
    }

    void HandleSolved()
    {
        if (!running) return;
        running = false;
        OnEventSolved?.Invoke();
        OnSolvedInternal?.Invoke(this);
    }
}
