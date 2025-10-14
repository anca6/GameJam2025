using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using Random = UnityEngine.Random;

public class EventManagerGame : MonoBehaviour
{
    [Header("Cinemachine")]
    public CinemachineBrain brain;    // MainCamera's brain
    public CinemachineCamera cameraA; // The target gameplay cam

    [Header("Events Pool")]
    public List<EventDefinition> possibleEvents = new();

    [Tooltip("Event slots in the scene (spawn puzzles into these)")]
    public List<EventInstance> eventSlots = new();

    [Header("Concurrent event count weights")]
    [Tooltip("Chance weight for spawning exactly 1 event")]
    public float weight1 = 40f;
    public float weight2 = 35f;
    public float weight3 = 20f;
    public float weight4 = 5f;

    [Header("Round Control")]
    public bool startOnCameraASettled = true;
    public float settleExtraDelay = 1.0f; // seconds after blend stops
    public bool allowSameTypeMultipleTimes = false;

    [Header("Staggered countdown")]
    public float minStartDelay = 3f;
    public float maxStartDelay = 5f;


    [Header("Global Hooks")]
    public UnityEngine.Events.UnityEvent OnAnyEventFailed;
    public UnityEngine.Events.UnityEvent OnAllEventsCleared;

    private readonly HashSet<EventInstance> active = new();

    void Start()
    {
        if (startOnCameraASettled) StartCoroutine(StartAfterCameraSettled());
        else TriggerRandomEvents();
    }

    System.Collections.IEnumerator StartAfterCameraSettled()
    {
        // Wait until the brain is not blending and Camera A is active
        // (Cinemachine 3 still exposes IsBlending)
        while (brain.IsBlending) yield return null;

        // Ensure Camera A is active (optional, if you switch by priority)
        yield return new WaitForSeconds(settleExtraDelay);
        TriggerRandomEvents();
    }
    public void TriggerRandomEvents()
    {
        int targetCount = WeightedPickCount();
        var defs = PickEventDefinitions(targetCount);

        int slotIndex = 0;
        foreach (var def in defs)
        {
            while (slotIndex < eventSlots.Count && eventSlots[slotIndex] == null) slotIndex++;
            if (slotIndex >= eventSlots.Count) break;

            var slot = eventSlots[slotIndex++];
            slot.gameObject.SetActive(true);
            slot.OnSolvedInternal = HandleSolved;
            slot.OnFailedInternal = HandleFailed;

            slot.Begin(def); // begin without running timer
            active.Add(slot);

            // Start each countdown with its own random delay
            float delay = Random.Range(minStartDelay, maxStartDelay);
            slot.StartCountdownDelayed(delay);
        }
    }

    int WeightedPickCount()
    {
        float total = weight1 + weight2 + weight3 + weight4;
        float r = Random.value * total;

        if ((r -= weight1) <= 0) return 1;
        if ((r -= weight2) <= 0) return 2;
        if ((r -= weight3) <= 0) return 3;
        return 4;
    }

    List<EventDefinition> PickEventDefinitions(int count)
    {
        var result = new List<EventDefinition>(count);
        var pool = new List<EventDefinition>(possibleEvents);

        for (int i = 0; i < count; i++)
        {
            if (pool.Count == 0) break;
            var pick = pool[Random.Range(0, pool.Count)];
            result.Add(pick);

            if (!allowSameTypeMultipleTimes)
            {
                // remove same type to avoid duplicates
                pool.RemoveAll(d => d.type == pick.type);
            }
            else
            {
                // keep pool; optionally remove only the picked instance
                pool.Remove(pick);
            }
        }
        return result;
    }

    void HandleSolved(EventInstance inst)
    {
        if (!active.Contains(inst)) return;
        active.Remove(inst);
        inst.gameObject.SetActive(false); // or keep visible

        if (active.Count == 0)
            OnAllEventsCleared?.Invoke();
    }

    void HandleFailed(EventInstance inst)
    {
        if (!active.Contains(inst)) return;
        active.Remove(inst);
        OnAnyEventFailed?.Invoke();

        // Optional: immediately stop all others
        foreach (var other in active)
        {
            other.gameObject.SetActive(false);
        }
        active.Clear();
    }
}
