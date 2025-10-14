using UnityEngine;
using UnityEngine.SceneManagement;

public class GameController : MonoBehaviour
{
    [Header("UI (optional)")]
    public Canvas gameOverCanvas; // enable this on fail

    [Header("Scene (optional)")]
    public string gameOverSceneName; // leave empty to just show canvas

    public void Show()
    {
        if (!string.IsNullOrEmpty(gameOverSceneName))
        {
            SceneManager.LoadScene(gameOverSceneName);
            return;
        }
        if (gameOverCanvas) gameOverCanvas.enabled = true;
        Time.timeScale = 0f; // freeze game if you want
    }
}
