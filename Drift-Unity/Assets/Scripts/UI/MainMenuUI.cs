using System.Threading.Tasks;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MainMenuUI — drives the main menu and session browse screens.
///
/// Panels managed:
///   MainPanel  — visible during GamePhase.MainMenu
///   BrowsePanel — open rooms list, shown when player taps Browse
///
/// Flow:
///   Host Public  → GameManager.HostSession(Passive, public:true)  → Lobby
///   Host Private → GameManager.HostSession(Passive, public:false) → Lobby
///   Browse       → show BrowsePanel, QuerySessions every refresh interval
///   Join by Code → GameManager.JoinSessionByCode(code)            → Lobby
///
/// Attach to: a Canvas GameObject in SampleScene.
/// Subscribe to GameManager.OnPhaseChanged to show/hide automatically.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────────────────────

    public static MainMenuUI Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Panels")]
    [Tooltip("Root panel shown during MainMenu phase.")]
    [SerializeField] private GameObject mainPanel;

    [Tooltip("Open rooms browse panel.")]
    [SerializeField] private GameObject browsePanel;

    [Header("Main Panel Buttons")]
    [SerializeField] private Button hostPublicButton;
    [SerializeField] private Button hostPrivateButton;
    [SerializeField] private Button browseButton;
    [SerializeField] private Button joinByCodeButton;

    [Header("Join by Code")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_Text joinCodeFeedback;

    [Header("Browse Panel")]
    [Tooltip("Parent transform — SessionRowUI prefabs are instantiated here.")]
    [SerializeField] private Transform sessionListParent;

    [Tooltip("Prefab with SessionRowUI component for each listed session.")]
    [SerializeField] private GameObject sessionRowPrefab;

    [SerializeField] private Button browsePanelCloseButton;
    [SerializeField] private Button refreshButton;

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Wire buttons.
        hostPublicButton?.onClick.AddListener(() => _ = OnHostPublic());
        hostPrivateButton?.onClick.AddListener(() => _ = OnHostPrivate());
        browseButton?.onClick.AddListener(OnBrowse);
        joinByCodeButton?.onClick.AddListener(() => _ = OnJoinByCode());
        browsePanelCloseButton?.onClick.AddListener(OnBrowseClose);
        refreshButton?.onClick.AddListener(() => _ = RefreshSessionList());

        // Subscribe to phase changes.
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged += OnPhaseChanged;

        // Start hidden — OnPhaseChanged will show when ready.
        SetPanelsVisible(false, false);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged -= OnPhaseChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHASE VISIBILITY
    // ─────────────────────────────────────────────────────────────────────────

    private void OnPhaseChanged(GameManager.GamePhase phase)
    {
        bool showMain = phase == GameManager.GamePhase.MainMenu;
        SetPanelsVisible(showMain, false);

        // Clear stale status whenever returning to main menu.
        if (showMain) SetStatus(string.Empty);
    }

    private void SetPanelsVisible(bool main, bool browse)
    {
        if (mainPanel != null) mainPanel.SetActive(main);
        if (browsePanel != null) browsePanel.SetActive(browse);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BUTTON HANDLERS
    // ─────────────────────────────────────────────────────────────────────────

    private async Task OnHostPublic()
    {
        SetStatus("Creating public session...");
        SetButtonsInteractable(false);

        string code = await GameManager.Instance.HostSession(
            GameMode.Passive, isPublic: true);

        if (code == null)
        {
            SetStatus("Failed to create session. Check your connection.");
            SetButtonsInteractable(true);
        }
        // On success GameManager transitions phase → Lobby, LobbyUI takes over.
    }

    private async Task OnHostPrivate()
    {
        SetStatus("Creating private session...");
        SetButtonsInteractable(false);

        string code = await GameManager.Instance.HostSession(
            GameMode.Passive, isPublic: false);

        if (code == null)
        {
            SetStatus("Failed to create session. Check your connection.");
            SetButtonsInteractable(true);
        }
    }

    private void OnBrowse()
    {
        SetPanelsVisible(false, true);
        _ = RefreshSessionList();
    }

    private void OnBrowseClose()
    {
        SetPanelsVisible(true, false);
    }

    private async Task OnJoinByCode()
    {
        string code = joinCodeInput != null
            ? joinCodeInput.text.Trim().ToUpper()
            : string.Empty;

        if (code.Length != GameConstants.JoinCodeLength)
        {
            SetStatus($"Enter a {GameConstants.JoinCodeLength}-character join code.");
            return;
        }

        SetStatus($"Joining session {code}...");
        SetButtonsInteractable(false);

        bool success = await GameManager.Instance.JoinSessionByCode(code);
        if (!success)
        {
            SetStatus("Could not join — check the code and try again.");
            SetButtonsInteractable(true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SESSION BROWSE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries available public sessions and populates the browse list.
    /// Clears previous rows before rebuilding. Each row shows session ID,
    /// player count, and a Join button.
    /// </summary>
    private async Task RefreshSessionList()
    {
        if (sessionListParent == null) return;

        // Clear old rows.
        foreach (Transform child in sessionListParent)
            Destroy(child.gameObject);

        SetStatus("Refreshing...");

        var results = await RelayManager.Instance?.QueryAvailableSessions();

        if (results == null || results.Sessions.Count == 0)
        {
            SetStatus("No open sessions found.");
            return;
        }

        SetStatus($"{results.Sessions.Count} session(s) available.");

        foreach (var session in results.Sessions)
        {
            if (sessionRowPrefab == null) break;

            string capturedId = session.Id;

            GameObject row = Instantiate(sessionRowPrefab, sessionListParent);
            SessionRowUI rowUI = row.GetComponent<SessionRowUI>();

            // ISessionInfo exposes AvailableSlots and MaxPlayers — derive current
            // player count as MaxPlayers minus available slots.
            int current = session.MaxPlayers - session.AvailableSlots;
            rowUI?.Setup(
                capturedId,
                current,
                session.MaxPlayers,
                () => _ = JoinFromBrowse(capturedId));
        }
    }

    private async Task JoinFromBrowse(string sessionId)
    {
        SetStatus($"Joining session {sessionId}...");
        SetButtonsInteractable(false);

        bool success = await GameManager.Instance.JoinSessionById(sessionId);
        if (!success)
        {
            SetStatus("Could not join session.");
            SetButtonsInteractable(true);
            _ = RefreshSessionList();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[MainMenuUI] {message}");
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (hostPublicButton != null) hostPublicButton.interactable = interactable;
        if (hostPrivateButton != null) hostPrivateButton.interactable = interactable;
        if (browseButton != null) browseButton.interactable = interactable;
        if (joinByCodeButton != null) joinByCodeButton.interactable = interactable;
    }
}