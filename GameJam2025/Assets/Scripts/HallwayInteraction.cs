using UnityEngine;

public class HallwayInteraction : MonoBehaviour
{
    [SerializeField] private GameObject hallwayDoor;

    private void Awake()
    {
        hallwayDoor.SetActive(false); // Ensure the door is initially inactive
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log("Player entered the hallway trigger.");
            //inverse camera - use cinemachine camera? 
            //trigger event 
            gameObject.SetActive(false); // Disable the trigger after activation
            hallwayDoor.SetActive(true); // Activate the door
        }

    }
}

