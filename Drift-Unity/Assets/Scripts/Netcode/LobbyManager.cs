using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// LobbyManager — manages session discovery, ready states, and session lifecycle.
///
/// Works alongside RelayManager: RelayManager owns the ISession connection,
/// LobbyManager owns the UI-facing state machine around it.
///
/// Responsibilities:
///   · Poll available sessions for the open rooms dashboard
///   · Track connected player ready states
///   · Notify GameManager when all players are ready (host can start)
///   · Handle player join/leave events during the lobby phase
///   · Provide session metadata for LobbyUI display
///
/// GameManager calls LobbyManager after RelayManager creates/joins a session.
/// LobbyUI subscribes to LobbyManager events to update its display.
///
/// Singleton — attach to the same persistent GameObject as RelayManager.
/// There should be exactly one LobbyManager in the scene.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────────────────────

    public static LobbyManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EVENTS — LobbyUI subscribes to these
    // ─────────────────────────────────────────────────────────────────────────

    /// Fired when the session player list changes (join or leave).
    public event Action<List<SessionPlayerInfo>> OnPlayersChanged;

    /// Fired when all players in the session are ready.
    public event Action OnAllPlayersReady;

    /// Fired when the host starts the game — all clients transition to Playing.
    public event Action OnGameStarted;

    /// Fired when available sessions list is refreshed for the dashboard.
    public event Action<List<ISessionInfo>> OnSessionListRefreshed;

    // ─────────────────────────────────────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Current player info list for the lobby display.
    public List<SessionPlayerInfo> ConnectedPlayers { get; private set; }
        = new List<SessionPlayerInfo>();

    /// Most recent session list from dashboard query.
    public List<ISessionInfo> AvailableSessions { get; private set; }
        = new List<ISessionInfo>();

    /// Whether the local player has marked themselves ready.
    public bool IsLocalPlayerReady { get; private set; }

    /// Whether all connected players are ready (host reads this to enable Start).
    public bool AreAllPlayersReady { get; private set; }

    /// Seconds between automatic session list refreshes on the dashboard.
    private const float SessionRefreshInterval = 5f;

    /// Coroutine handle for the auto-refresh ticker.
    private Coroutine _refreshCoroutine;

    // ─────────────────────────────────────────────────────────────────────────
    // SESSION SETUP — called by GameManager after session is created/joined
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameManager once a session is active.
    /// Subscribes to session events and initializes lobby state.
    /// </summary>
    public void OnSessionJoined(ISession session)
    {
        if (session == null)
        {
            Debug.LogError("[LobbyManager] OnSessionJoined called with null session.");
            return;
        }

        session.PlayerJoined += OnPlayerJoined;
        session.PlayerLeaving += OnPlayerLeft;

        // Changed event takes no parameters in MPS SDK 2.x.
        session.Changed += OnSessionChanged;

        Debug.Log($"[LobbyManager] Subscribed to session events. " +
                  $"Players={session.Players.Count}/{session.MaxPlayers}");

        RefreshPlayerList(session);
    }

    /// <summary>
    /// Called by GameManager when the session ends or the player leaves.
    /// Unsubscribes from session events and resets lobby state.
    /// </summary>
    public void OnSessionLeft(ISession session)
    {
        if (session == null) return;

        session.PlayerJoined -= OnPlayerJoined;
        session.PlayerLeaving -= OnPlayerLeft;
        session.Changed -= OnSessionChanged;

        ConnectedPlayers.Clear();
        IsLocalPlayerReady = false;
        AreAllPlayersReady = false;

        StopRefreshCoroutine();
        Debug.Log("[LobbyManager] Session left — lobby state reset.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYER READY STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the local player's ready state.
    /// Called by LobbyUI when the player taps the ready button.
    /// Writes to NetworkPlayer.isReady via ServerRpc.
    /// </summary>
    public void ToggleReady()
    {
        IsLocalPlayerReady = !IsLocalPlayerReady;

        var localNetworkPlayer = GetLocalNetworkPlayer();
        if (localNetworkPlayer != null)
            localNetworkPlayer.SetReadyServerRpc(IsLocalPlayerReady);

        Debug.Log($"[LobbyManager] Local player ready={IsLocalPlayerReady}");
        CheckAllPlayersReady();
    }

    /// <summary>
    /// Checks if all connected players have marked themselves ready.
    /// Fires OnAllPlayersReady when the condition is first met.
    /// </summary>
    private void CheckAllPlayersReady()
    {
        if (ConnectedPlayers.Count == 0) return;

        // FUTURE: read isReady from each NetworkPlayer's NetworkVariable
        // Full NetworkVariable read wired in feature/player-controller.
        bool allReady = IsLocalPlayerReady;

        if (allReady && !AreAllPlayersReady)
        {
            AreAllPlayersReady = true;
            OnAllPlayersReady?.Invoke();
            Debug.Log("[LobbyManager] All players ready — host can start.");
        }
        else if (!allReady)
        {
            AreAllPlayersReady = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HOST — Start Game
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by LobbyUI when the host taps Start.
    /// Fires OnGameStarted which GameManager handles to transition to Playing.
    /// Only the host should call this.
    /// </summary>
    public void StartGame()
    {
        if (RelayManager.Instance == null || !RelayManager.Instance.IsInSession)
        {
            Debug.LogError("[LobbyManager] StartGame called with no active session.");
            return;
        }

        Debug.Log("[LobbyManager] Host starting game.");
        OnGameStarted?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SESSION DASHBOARD — Browse available sessions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the current list of available public sessions.
    /// Called on demand by LobbyUI and on the auto-refresh interval.
    /// Fires OnSessionListRefreshed when results arrive.
    /// </summary>
    public async Task RefreshSessionList()
    {
        var results = await RelayManager.Instance.QueryAvailableSessions();
        if (results == null) return;

        AvailableSessions.Clear();
        foreach (var s in results.Sessions)
            AvailableSessions.Add(s);

        OnSessionListRefreshed?.Invoke(AvailableSessions);
        Debug.Log($"[LobbyManager] Session list refreshed. Count={AvailableSessions.Count}");
    }

    /// <summary>
    /// Starts the automatic session list refresh coroutine.
    /// Called by LobbyUI when the browse panel opens.
    /// </summary>
    public void StartAutoRefresh()
    {
        StopRefreshCoroutine();
        _refreshCoroutine = StartCoroutine(AutoRefreshCoroutine());
    }

    /// <summary>Stops the automatic refresh. Called when browse panel closes.</summary>
    public void StopAutoRefresh() => StopRefreshCoroutine();

    private System.Collections.IEnumerator AutoRefreshCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(SessionRefreshInterval);
            _ = RefreshSessionList();
        }
    }

    private void StopRefreshCoroutine()
    {
        if (_refreshCoroutine != null)
        {
            StopCoroutine(_refreshCoroutine);
            _refreshCoroutine = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SESSION EVENT HANDLERS
    // ─────────────────────────────────────────────────────────────────────────

    private void OnPlayerJoined(string playerId)
    {
        Debug.Log($"[LobbyManager] Player joined: {playerId}");
        var session = RelayManager.Instance?.CurrentSession;
        if (session != null) RefreshPlayerList(session);
    }

    private void OnPlayerLeft(string playerId)
    {
        Debug.Log($"[LobbyManager] Player left: {playerId}");
        var session = RelayManager.Instance?.CurrentSession;
        if (session != null) RefreshPlayerList(session);
    }

    /// <summary>
    /// Parameterless — MPS SDK 2.x Changed event fires with no arguments.
    /// </summary>
    private void OnSessionChanged()
    {
        Debug.Log("[LobbyManager] Session state changed.");
        var session = RelayManager.Instance?.CurrentSession;
        if (session != null) RefreshPlayerList(session);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds ConnectedPlayers from the current session and fires
    /// OnPlayersChanged so LobbyUI can update its display.
    /// </summary>
    private void RefreshPlayerList(ISession session)
    {
        ConnectedPlayers.Clear();
        foreach (var player in session.Players)
        {
            ConnectedPlayers.Add(new SessionPlayerInfo
            {
                PlayerId = player.Id,
                // FUTURE: wire host detection via session.Host when confirmed
                IsHost = false
            });
        }
        OnPlayersChanged?.Invoke(ConnectedPlayers);
    }

    /// <summary>
    /// Finds the local player's NetworkPlayer component.
    /// Returns null before PlayerController exists.
    /// FUTURE: replace with NetworkPlayer.LocalInstance in feature/player-controller.
    /// </summary>
    private NetworkPlayer GetLocalNetworkPlayer()
    {
        var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (var p in players)
            if (p.IsOwner) return p;
        return null;
    }
}

/// <summary>
/// Lightweight player info struct for LobbyUI display.
/// Populated from ISession.Players — no NetworkVariable dependency.
/// </summary>
[Serializable]
public struct SessionPlayerInfo
{
    public string PlayerId;
    public bool IsHost;
}