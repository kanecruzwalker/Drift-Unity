using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SessionRowUI — one row in the open sessions browse list.
/// Instantiated by MainMenuUI.RefreshSessionList() per available session.
/// Displays session ID, player count, and a Join button.
/// </summary>
public class SessionRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text sessionLabel;
    [SerializeField] private TMP_Text playerCountLabel;
    [SerializeField] private Button joinButton;

    /// <summary>
    /// Populates this row with session data and wires the Join button callback.
    /// Called by MainMenuUI immediately after instantiation.
    /// </summary>
    public void Setup(string sessionId, int current, int max, System.Action onJoin)
    {
        if (sessionLabel != null)
            sessionLabel.text = sessionId[..Mathf.Min(8, sessionId.Length)];

        if (playerCountLabel != null)
            playerCountLabel.text = $"{current}/{max}";

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => onJoin?.Invoke());
        }
    }
}