using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// GameManager — singleton authority for game phase state and UGS initialization.
///
/// Responsibilities:
///   · Initialize Unity Gaming Services on startup
///   · Authenticate the local player anonymously
///   · Drive the game phase state machine: MainMenu → Lobby → Playing
///   · Coordinate RelayManager and LobbyManager at phase transitions
///   · Provide solo mode (host plays while waiting for others)
///   · Handle application quit and session cleanup
///
/// Phase transitions:
///   MainMenu → Lobby   : player hosts or joins a session
///   Lobby    → Playing : host calls StartGame, LobbyManager fires OnGameStarted
///   Playing  → MainMenu: session ends or player disconnects
///
/// All other systems read CurrentPhase to gate their behavior.
/// Subscribe to OnPhaseChanged to react to transitions.
///
/// Attach to a persistent GameObject in SampleScene.
/// There should be exactly one GameManager in the scene.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────────────────────

    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GAME PHASE
    // ─────────────────────────────────────────────────────────────────────────

    /// All possible game phases. Systems gate behavior on this value.
    public enum GamePhase
    {
        Initializing,   // UGS init and auth in progress — block all input
        MainMenu,       // Player is on the main menu — can host or browse
        Lobby,          // Player is in a session lobby — waiting for others
        Playing,        // Active gameplay — orbs spawning, input live
        // FUTURE: GameOver, Paused
    }

    /// Current game phase. Read by all systems to gate behavior.
    public GamePhase CurrentPhase { get; private set; } = GamePhase.Initializing;

    /// Fired on every phase transition. Subscribe to react to state changes.
    public event Action<GamePhase> OnPhaseChanged;

    /// Current game mode — set by host in lobby before starting.
    public GameMode CurrentGameMode { get; private set; } = GameMode.Passive;

    // ─────────────────────────────────────────────────────────────────────────
    // UGS STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// True once UGS is initialized and the player is authenticated.
    public bool IsUGSReady { get; private set; }

    /// The authenticated player's UGS ID. Empty until authentication completes.
    public string LocalPlayerId { get; private set; } = string.Empty;

    /// True if the local player is the session host.
    public bool IsHost => RelayManager.Instance?.IsHost ?? false;
    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private async void Start()
    {
        await InitializeUGS();


        // Session starts via MainMenuUI Host/Join buttons — see feature/hud-and-polish.
        // StartSoloSession() was here as a temp during feature/player-controller and
        // feature/world-layer testing. Removed in feature/game-loop (ADR-016 session notes).

        // TEMP: auto-start solo session for player movement testing
        // Remove before feature/game-loop wires the real UI flow
        // await StartSoloSession();

    }

    private void OnEnable()
    {
        // Subscribe to LobbyManager events once it's available.
        // LobbyManager may not exist yet at OnEnable — checked at runtime.
    }

    private void OnApplicationQuit()
    {
        // Clean up session on quit so Relay slot is released.
        RelayManager.Instance?.LeaveSession();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UGS INITIALIZATION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes Unity Gaming Services and authenticates the local player.
    /// Called once at startup. Transitions to MainMenu on success.
    ///
    /// Anonymous auth is used — no account required. Each device gets a
    /// stable anonymous ID that persists across sessions on the same device.
    ///
    /// All RelayManager and LobbyManager calls require this to complete first.
    /// Systems should check IsUGSReady or wait for GamePhase.MainMenu before
    /// making any UGS API calls.
    /// </summary>
    public async Task InitializeUGS()
    {
        try
        {
            Debug.Log("[GameManager] Initializing Unity Gaming Services...");

            await UnityServices.InitializeAsync();
            Debug.Log("[GameManager] UGS initialized.");

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("[GameManager] Signed in anonymously.");
            }

            LocalPlayerId = AuthenticationService.Instance.PlayerId;
            IsUGSReady = true;

            Debug.Log($"[GameManager] UGS ready. PlayerId={LocalPlayerId}");

            // Wire LobbyManager events now that UGS is ready.
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnGameStarted += HandleGameStarted;
                LobbyManager.Instance.OnAllPlayersReady += HandleAllPlayersReady;
            }

            TransitionToPhase(GamePhase.MainMenu);
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameManager] UGS initialization failed: {e.Message}");
            // Stay in Initializing phase — MainMenuUI will show an error state.
            // FUTURE: retry logic with exponential backoff.
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HOST FLOW
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by MainMenuUI when the player taps Host.
    /// Creates a new session and transitions to Lobby phase.
    /// Returns the join code on success, null on failure.
    /// </summary>
    public async Task<string> HostSession(GameMode mode = GameMode.Passive, bool isPublic = true)
    {
        if (!IsUGSReady)
        {
            Debug.LogError("[GameManager] HostSession called before UGS is ready.");
            return null;
        }

        CurrentGameMode = mode;
        string joinCode = await RelayManager.Instance.CreateSession();

        if (joinCode == null)
        {
            Debug.LogError("[GameManager] HostSession failed — no join code returned.");
            return null;
        }

        LobbyManager.Instance?.OnSessionJoined(RelayManager.Instance.CurrentSession);
        TransitionToPhase(GamePhase.Lobby);

        Debug.Log($"[GameManager] Hosting session. JoinCode={joinCode} Mode={mode}");
        return joinCode;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CLIENT FLOW — Join by Code
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by LobbyUI when the player submits a join code.
    /// Joins the session and transitions to Lobby phase on success.
    /// Returns true on success, false on failure.
    /// </summary>
    public async Task<bool> JoinSessionByCode(string joinCode)
    {
        if (!IsUGSReady)
        {
            Debug.LogError("[GameManager] JoinSessionByCode called before UGS is ready.");
            return false;
        }

        bool success = await RelayManager.Instance.JoinSessionByCode(joinCode);
        if (!success) return false;

        LobbyManager.Instance?.OnSessionJoined(RelayManager.Instance.CurrentSession);
        TransitionToPhase(GamePhase.Lobby);

        Debug.Log($"[GameManager] Joined session by code={joinCode}");
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CLIENT FLOW — Join by ID (from browse list)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by LobbyUI when the player taps a session in the browse list.
    /// Joins the session by its ID and transitions to Lobby.
    /// Returns true on success, false on failure.
    /// </summary>
    public async Task<bool> JoinSessionById(string sessionId)
    {
        if (!IsUGSReady)
        {
            Debug.LogError("[GameManager] JoinSessionById called before UGS is ready.");
            return false;
        }

        bool success = await RelayManager.Instance.JoinSessionById(sessionId);
        if (!success) return false;

        LobbyManager.Instance?.OnSessionJoined(RelayManager.Instance.CurrentSession);
        TransitionToPhase(GamePhase.Lobby);

        Debug.Log($"[GameManager] Joined session by id={sessionId}");
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SOLO MODE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by MainMenuUI when the player taps Play Solo.
    /// Creates a session but transitions directly to Playing without waiting
    /// for other players — host plays alone, others can join mid-session.
    /// </summary>
    public async Task StartSoloSession()
    {
        string joinCode = await HostSession(GameMode.Passive);
        if (joinCode == null) return;

        // Skip lobby — go straight to playing.
        Debug.Log($"[GameManager] Solo mode started. JoinCode={joinCode} " +
                  $"(others can still join using this code)");
        TransitionToPhase(GamePhase.Playing);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LEAVE SESSION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by HUD or GameManager internally when the player leaves.
    /// Cleans up session state and returns to MainMenu.
    /// </summary>
    public void LeaveCurrentSession()
    {
        if (RelayManager.Instance?.CurrentSession != null)
            LobbyManager.Instance?.OnSessionLeft(RelayManager.Instance.CurrentSession);

        RelayManager.Instance?.LeaveSession();
        TransitionToPhase(GamePhase.MainMenu);

        Debug.Log("[GameManager] Left session — returned to MainMenu.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LOBBY EVENT HANDLERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when LobbyManager fires OnAllPlayersReady.
    /// Enables the Start button on the host's LobbyUI.
    /// FUTURE: auto-start after a countdown if desired.
    /// </summary>
    private void HandleAllPlayersReady()
    {
        Debug.Log("[GameManager] All players ready — host may start.");
        // FUTURE: MainMenuUI.Instance.EnableStartButton();
    }

    /// <summary>
    /// Called when LobbyManager fires OnGameStarted (host tapped Start).
    /// Transitions all clients to Playing phase.
    /// </summary>
    private void HandleGameStarted()
    {
        Debug.Log("[GameManager] Game started — transitioning to Playing.");
        TransitionToPhase(GamePhase.Playing);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHASE MACHINE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transitions to a new game phase and fires OnPhaseChanged.
    /// All phase-gated systems subscribe to OnPhaseChanged to react.
    /// </summary>
    private void TransitionToPhase(GamePhase newPhase)
    {
        if (CurrentPhase == newPhase) return;

        GamePhase previous = CurrentPhase;
        CurrentPhase = newPhase;

        Debug.Log($"[GameManager] Phase: {previous} → {newPhase}");
        OnPhaseChanged?.Invoke(newPhase);
    }
}

/// <summary>
/// Game mode selected in lobby — drives WorldManager spawn behavior
/// and DomainZone hazard activation. Set by host before session starts.
/// </summary>
public enum GameMode
{
    Passive,    // collection and expansion only, no damage
    PvE         // hazard zones active, enemy orbs spawn
    // FUTURE: PvP — player damage enabled, see ADR-012
}



/// <summary>
/// Zone state drives spawn rates, hazards, shop availability, and fog of war.
/// Single source of truth — WorldManager writes, all systems read (ADR-014).
/// </summary>
public enum ZoneState
{
    Undiscovered,   // fog active, high orb spawn, high enemy spawn
    Discovered,     // fog cleared, high orb spawn, medium enemies
    Contested,      // station partially filled, medium orbs, enemies present
    Safe            // station full, low orbs, no enemies, shop available
    // FUTURE: Corrupted — boss reverts Safe → Contested
}