using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LobbyUI — shown during GamePhase.Lobby.
///
/// Displays:
///   · Join code (copyable, auto-hides after JoinCodeAutoHideDelay)
///   · Connected player list with ready states
///   · Ready toggle button (all players)
///   · Start button (host only — enabled when all players ready)
///   · Leave session button
///
/// Attach to: the same Canvas as MainMenuUI.
/// Shown/hidden by GameManager.OnPhaseChanged.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────────────────────

    public static LobbyUI Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Panel")]
    [SerializeField] private GameObject lobbyPanel;

    [Header("Join Code")]
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private Button copyCodeButton;
    [Tooltip("Seconds before the join code label auto-hides.")]
    [SerializeField] private float codeAutoHideDelay = 15f;

    [Header("Player List")]
    [Tooltip("Parent for player slot labels.")]
    [SerializeField] private Transform playerListParent;
    [Tooltip("Prefab with a TMP_Text for player name + ready state.")]
    [SerializeField] private GameObject playerSlotPrefab;

    [Header("Buttons")]
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startButton;   // Host only
    [SerializeField] private Button leaveButton;

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    private bool _isReady = false;

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
        readyButton?.onClick.AddListener(OnReadyToggle);
        startButton?.onClick.AddListener(OnStart);
        leaveButton?.onClick.AddListener(OnLeave);
        copyCodeButton?.onClick.AddListener(OnCopyCode);

        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged += OnPhaseChanged;

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayersChanged +=  OnPlayersChangedHandler;
            LobbyManager.Instance.OnAllPlayersReady += OnAllReady;
        }

        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged -= OnPhaseChanged;
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayersChanged -= OnPlayersChangedHandler;
            LobbyManager.Instance.OnAllPlayersReady -= OnAllReady;
        }
    }



    // ─────────────────────────────────────────────────────────────────────────
    // PHASE VISIBILITY
    // ─────────────────────────────────────────────────────────────────────────

    private void OnPhaseChanged(GameManager.GamePhase phase)
    {
        bool showLobby = phase == GameManager.GamePhase.Lobby;
        if (lobbyPanel != null) lobbyPanel.SetActive(showLobby);

        if (showLobby) OnLobbyEntered();

        // Hide lobby panel when game starts — world is already running underneath.
        if (phase == GameManager.GamePhase.Playing)
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            Debug.Log("[LobbyUI] Game started — hiding lobby panel.");
        }
    }

    private void OnLobbyEntered()
    {
        // Show join code — auto-hide after delay.
        string code = RelayManager.Instance?.JoinCode ?? "------";
        if (joinCodeText != null)
        {
            joinCodeText.text = $"Code: {code}";
            joinCodeText.gameObject.SetActive(true);
            Invoke(nameof(HideJoinCode), codeAutoHideDelay);
        }


        // Only host can start the session.
        bool isHost = GameManager.Instance?.IsHost ?? false;
        if (startButton != null)
        {
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = false; // Enabled when all ready.
        }

        SetStatus("Waiting for players...");
        RefreshPlayerList();
    }

    private void HideJoinCode()
    {
        if (joinCodeText != null) joinCodeText.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BUTTON HANDLERS
    // ─────────────────────────────────────────────────────────────────────────

    private void OnReadyToggle()
    {
        _isReady = !_isReady;
        NetworkPlayer localPlayer = GetLocalNetworkPlayer();
        localPlayer?.SetReadyServerRpc(_isReady);

        if (readyButton != null)
        {
            TMP_Text label = readyButton.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = _isReady ? "Not Ready" : "Ready";
        }

        SetStatus(_isReady ? "Ready!" : "Waiting for others...");

        // Re-evaluate start button any time local ready state changes.
        EvaluateStartButton();
    }

    /// <summary>
    /// Evaluates whether the Start button should be enabled.
    /// Rules:
    ///   · Only visible to the host.
    ///   · Enabled when ALL connected NetworkPlayers have isReady = true.
    ///   · Solo mode (1 player): host ready = start enabled immediately.
    ///   · Multi mode: all players must be ready.
    /// Called on ready toggle and when any player's ready state changes.
    /// </summary>
    private void EvaluateStartButton()
    {
        if (startButton == null) return;
        if (!(GameManager.Instance?.IsHost ?? false)) return;

        var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);

        // Need at least one player (the host themselves).
        if (players.Length == 0) return;

        bool allReady = true;
        foreach (var p in players)
        {
            if (!p.isReady.Value) { allReady = false; break; }
        }

        startButton.interactable = allReady;

        if (allReady)
            SetStatus("All players ready — start when ready!");
    }

    private void OnAllReady()
    {
        // LobbyManager fires this — use it as a secondary trigger.
        EvaluateStartButton();
    }

    private void OnStart()
    {
        if (LobbyManager.Instance == null) return;
        LobbyManager.Instance.StartGame();
        SetStatus("Starting...");
    }

    private void OnLeave()
    {
        GameManager.Instance?.LeaveCurrentSession();
    }

    private void OnCopyCode()
    {
        string code = RelayManager.Instance?.JoinCode ?? string.Empty;
        if (!string.IsNullOrEmpty(code))
            GUIUtility.systemCopyBuffer = code;
        SetStatus("Code copied!");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYER LIST
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the player list display. Called by LobbyManager.OnPlayersChanged.
    /// Each row shows "Player X — Ready / Waiting".
    /// </summary>
    private void RefreshPlayerList()
    {
        if (playerListParent == null || playerSlotPrefab == null) return;

        foreach (Transform child in playerListParent)
            Destroy(child.gameObject);

        foreach (var player in
            FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            GameObject slot = Instantiate(playerSlotPrefab, playerListParent);
            TMP_Text label = slot.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                string readyStr = player.isReady.Value ? "Ready" : "Waiting";
                label.text = $"Player {player.OwnerClientId} — {readyStr}";
            }

            // Subscribe to this player's ready state changes so the list
            // and Start button update the moment any player toggles ready.
            player.isReady.OnValueChanged += (_, __) =>
            {
                RefreshPlayerList();
                EvaluateStartButton();
            };
        }

        // Re-evaluate start button whenever the player list is rebuilt.
        EvaluateStartButton();
    }


    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────


    private void OnPlayersChangedHandler(List<SessionPlayerInfo> players)
    {
        RefreshPlayerList();
    }
    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[LobbyUI] {message}");
    }

    private NetworkPlayer GetLocalNetworkPlayer()
    {
        foreach (var player in
            FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.IsOwner) return player;
        }
        return null;
    }


}