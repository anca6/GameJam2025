using UnityEngine;

public enum ShipEventType { Oxygen, Comms, Security, Electrical }

[CreateAssetMenu(fileName = "EventDefinition", menuName = "GameJam/Event Definition")]
public class EventDefinition : ScriptableObject
{
    public ShipEventType type;
    [Min(1f)] public float durationSeconds = 12f;
    [Tooltip("Spawned under EventInstance's puzzleAnchor")]
    public GameObject puzzlePrefab;

    [Header("Optional theme/UX")]
    public string uiTitle = "OXYGEN";
    public string subtitle = "Stabilize flow";
}
