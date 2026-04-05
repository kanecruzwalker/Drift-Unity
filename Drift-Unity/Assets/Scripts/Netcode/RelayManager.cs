using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// RelayManager — wraps the Multiplayer Services SDK session API.
///
/// The Multiplayer Services 2.x package unifies Relay, Lobby, and join codes
/// under a single ISession abstraction. This replaces the old manual
/// RelayService + UnityTransport configuration pattern entirely.
///
/// The SDK automatically handles:
///   · Relay server allocation
///   · Join code generation  
///   · UnityTransport configuration
///   · Netcode host/client startup
///
/// Usage flow:
///   Host:   await RelayManager.Instance.CreateSession()
///           → returns join code → display in HUD/LobbyUI
///   Client: await RelayManager.Instance.JoinSession(joinCode)
///           → returns bool success → show connected state in LobbyUI
///   Browse: await RelayManager.Instance.QuerySessions()
///           → returns session list → display in open rooms dashboard
///
/// Requires UGS initialized and authenticated — handled in GameManager.
/// Singleton — attach to the same persistent GameObject as NetworkManager.
/// </summary>
public class RelayManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────────────────────

    public static RelayManager Instance { get; private set; }

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
    // STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// The active session. Null when not in a session.
    public ISession CurrentSession { get; private set; }

    /// Join code for the current session. Empty when not hosting.
    public string JoinCode => CurrentSession?.Code ?? string.Empty;

    /// True when a session is active.
    public bool IsInSession => CurrentSession != null;

    // ─────────────────────────────────────────────────────────────────────────
    // HOST — Create Session
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new multiplayer session with Relay networking.
    /// The SDK automatically allocates a Relay server, generates a join code,
    /// and starts Netcode as host — no manual UnityTransport setup needed.
    ///
    /// Returns the join code string on success, null on failure.
    /// </summary>
    public async Task<string> CreateSession(string sessionName = "DriftSession")
    {
        try
        {
            var options = new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = GameConstants.MaxPlayersPerSession,
                IsLocked = false,
                IsPrivate = false
            }.WithRelayNetwork();

            CurrentSession = await MultiplayerService.Instance
                .CreateSessionAsync(options);

            Debug.Log($"[RelayManager] Session created. " +
                      $"Id={CurrentSession.Id} JoinCode={CurrentSession.Code}");
            return CurrentSession.Code;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] CreateSession failed: {e.Message}");
            CurrentSession = null;
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CLIENT — Join by Code
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Joins an existing session using a join code.
    /// The SDK handles Relay connection and Netcode client startup automatically.
    ///
    /// Returns true on success, false on any failure.
    /// </summary>
    public async Task<bool> JoinSessionByCode(string joinCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Debug.LogError("[RelayManager] Join code is empty.");
                return false;
            }

            CurrentSession = await MultiplayerService.Instance
                .JoinSessionByCodeAsync(joinCode.ToUpper().Trim());

            Debug.Log($"[RelayManager] Joined session. " +
                      $"Id={CurrentSession.Id} Code={joinCode}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] JoinSessionByCode failed: {e.Message}");
            CurrentSession = null;
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BROWSE — Open Rooms Dashboard
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries all active public sessions for the open rooms dashboard.
    /// Returns a QuerySessionsResults containing the session list.
    /// LobbyUI reads Name, PlayerCount, MaxPlayers, and Code from each result.
    ///
    /// Returns null on failure — LobbyUI should show an error/retry state.
    /// </summary>
    public async Task<QuerySessionsResults> QueryAvailableSessions()
    {
        try
        {
            var results = await MultiplayerService.Instance
                .QuerySessionsAsync(new QuerySessionsOptions());

            Debug.Log($"[RelayManager] Found {results.Sessions.Count} active sessions.");
            return results;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] QuerySessions failed: {e.Message}");
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CLIENT — Join by Session ID (from browse list)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Joins a specific session by its ID — used by the open rooms dashboard
    /// when a player taps a session from the browse list.
    ///
    /// Returns true on success, false on failure.
    /// </summary>
    public async Task<bool> JoinSessionById(string sessionId)
    {
        try
        {
            CurrentSession = await MultiplayerService.Instance
                .JoinSessionByIdAsync(sessionId);

            Debug.Log($"[RelayManager] Joined session by Id={sessionId}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] JoinSessionById failed: {e.Message}");
            CurrentSession = null;
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CLEANUP
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Leaves and cleans up the current session.
    /// Called by GameManager on session end, disconnect, or app quit.
    /// </summary>
    public async void LeaveSession()
    {
        if (CurrentSession == null) return;

        try
        {
            await CurrentSession.LeaveAsync();
            Debug.Log("[RelayManager] Left session.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RelayManager] LeaveSession failed: {e.Message}");
        }
        finally
        {
            CurrentSession = null;
        }
    }

    private void OnDestroy()
    {
        LeaveSession();
    }
}