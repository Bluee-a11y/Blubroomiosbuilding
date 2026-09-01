using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClubhousePC
{
    public sealed class MultiplayerMenu : MonoBehaviour
    {
        private string joinCode = "";
        private string hostCode = "";
        private string status = "Relay mode — no port forwarding needed";
        private bool busy;
        private bool intentionalShutdown;
        private bool recoveringFromLostHost;
        private bool usernameMenuOpen;
        private bool settingsMenuOpen;
        private bool howToPlayOpen;
        private bool shutdownConfirmOpen;
        private string settingsNotice = "";
        private string usernameDraft = "";
        private string profilePlayerId = "Loading…";
        private bool loadingProfileId;
        private bool watchOpen;
        private GUIStyle heading;
        private GUIStyle settingsButton;
        private GUIStyle settingsText;
        private GUIStyle watchTabButton;
        private Font arcadeFont;
        private Lobby activeLobby;
        private Coroutine lobbyHeartbeat;
        private const string LobbyQueueKey = "Queue";
        private const string LobbyRelayCodeKey = "RelayCode";
        private const string LobbyVersionKey = "Version";
        private const string MatchmakingVersion = "alpha-1.1";

        private void Start()
        {
            watchOpen = false;
            usernameMenuOpen = false;
            settingsMenuOpen = false;
            howToPlayOpen = false;
            shutdownConfirmOpen = false;
            var manager = NetworkManager.Singleton;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            SceneManager.sceneLoaded += CloseWatchAfterSceneLoad;

            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "BlubCenter" || sceneName == "Dodgeball")
                StartPublicMatchmaking(sceneName);
        }

        private void CloseWatchAfterSceneLoad(Scene scene, LoadSceneMode mode)
        {
            watchOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            var mobileWatch = MobileControls.Current != null && MobileControls.Current.ConsumeWatch();
            if (!Input.GetKeyDown(KeyCode.Tab) && !mobileWatch) return;
            watchOpen = !watchOpen;
            if (!watchOpen)
            {
                usernameMenuOpen = false;
                settingsMenuOpen = false;
                howToPlayOpen = false;
                shutdownConfirmOpen = false;
            }
            Cursor.lockState = watchOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = watchOpen;
        }

        private void OnDestroy()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            SceneManager.sceneLoaded -= CloseWatchAfterSceneLoad;
            StopLobbyHeartbeat();
        }

        private void OnClientConnected(ulong clientId)
        {
            status = clientId == NetworkManager.Singleton.LocalClientId
                ? "Connected successfully"
                : "Player " + clientId + " joined";

            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                var channel = NetworkManager.Singleton.IsHost ? hostCode : joinCode.Trim().ToUpperInvariant();
                GetComponent<VoiceChatManager>().JoinVoice(channel);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var reason = NetworkManager.Singleton.DisconnectReason;
            status = string.IsNullOrWhiteSpace(reason) ? "A player disconnected" : "Disconnected: " + reason;

            if (clientId == NetworkManager.Singleton.LocalClientId &&
                !intentionalShutdown && !NetworkManager.Singleton.IsServer)
                RecoverFromLostHost();
        }

        private async void RecoverFromLostHost()
        {
            if (recoveringFromLostHost) return;
            recoveringFromLostHost = true;
            busy = true;
            status = "Host left — returning safely to Blubhouse…";

            try
            {
                await LeaveCurrentPublicLobby();
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    NetworkManager.Singleton.Shutdown();
                await System.Threading.Tasks.Task.Delay(150);
                if (SceneManager.GetActiveScene().name != "Main")
                    await LoadingScreen.LoadScene("Main");
                status = "Host left — Blubhouse offline";
            }
            catch (System.Exception exception)
            {
                status = "Disconnected safely — reopen Blubhouse if needed";
                Debug.LogException(exception);
            }
            finally
            {
                busy = false;
                recoveringFromLostHost = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnGUI()
        {
            if (!watchOpen) return;
            arcadeFont ??= Resources.Load<Font>("Fonts/PressStart2P-Regular");
            var previousFont = GUI.skin.font;
            var previousLabelSize = GUI.skin.label.fontSize;
            var previousButtonSize = GUI.skin.button.fontSize;
            var previousTextFieldSize = GUI.skin.textField.fontSize;
            if (arcadeFont != null) GUI.skin.font = arcadeFont;
            GUI.skin.label.fontSize = 9;
            GUI.skin.button.fontSize = 8;
            GUI.skin.textField.fontSize = 9;
            try { DrawWatchGUI(); }
            finally
            {
                GUI.skin.font = previousFont;
                GUI.skin.label.fontSize = previousLabelSize;
                GUI.skin.button.fontSize = previousButtonSize;
                GUI.skin.textField.fontSize = previousTextFieldSize;
            }
        }

        private void DrawWatchGUI()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            heading ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Color.white }
            };

            GUI.Box(new Rect(Screen.width - 370, 18, 352, 410), "");
            GUI.Label(new Rect(Screen.width - 352, 30, 315, 30), "BLUB WATCH", heading);
            GUI.Label(new Rect(Screen.width - 352, 62, 118, 24), "Map: " + SceneManager.GetActiveScene().name);
            watchTabButton ??= new GUIStyle(GUI.skin.button) { fontSize = 7 };
            if (GUI.Button(new Rect(Screen.width - 222, 58, 90, 30), "SETTINGS", watchTabButton))
            {
                settingsMenuOpen = true;
                howToPlayOpen = false;
                shutdownConfirmOpen = false;
                usernameMenuOpen = false;
                settingsNotice = "";
                LoadProfilePlayerId();
            }
            if (GUI.Button(new Rect(Screen.width - 125, 58, 87, 30), "PROFILE", watchTabButton))
            {
                usernameMenuOpen = true;
                settingsMenuOpen = false;
                howToPlayOpen = false;
                shutdownConfirmOpen = false;
                usernameDraft = PlayerPrefs.GetString("BlubRoom.Username", "Player");
                LoadProfilePlayerId();
            }

            if (settingsMenuOpen)
            {
                DrawSettingsMenu();
                return;
            }

            if (usernameMenuOpen)
            {
                DrawUsernameMenu();
                return;
            }

            if (SceneManager.GetActiveScene().name == "MakerWorld")
            {
                GUI.Label(new Rect(Screen.width - 352, 92, 315, 24), "Press F to open the Maker palette");
                if (manager.IsHost)
                {
                    GUI.Label(new Rect(Screen.width - 352, 122, 250, 24), "Maker code: " + hostCode);
                    if (GUI.Button(new Rect(Screen.width - 105, 119, 67, 27), "COPY"))
                        GUIUtility.systemCopyBuffer = hostCode;
                }
                GUI.Label(new Rect(Screen.width - 352, 158, 78, 24), "Join code:");
                joinCode = GUI.TextField(new Rect(Screen.width - 270, 156, 232, 26), joinCode.ToUpperInvariant(), 12);
                GUI.enabled = !busy;
                if (GUI.Button(new Rect(Screen.width - 352, 192, 314, 34), "JOIN MAKERWORLD SERVER"))
                    StartClient();
                if (GUI.Button(new Rect(Screen.width - 352, 240, 314, 40), "RETURN TO BLUBHOUSE"))
                    ReturnToBlubhouse();
                if (GUI.Button(new Rect(Screen.width - 352, 290, 314, 40), "GO TO BLUBCENTER"))
                    EnterBlubCenter();
                GUI.enabled = true;
                GUI.Label(new Rect(Screen.width - 352, 342, 314, 32), status);
                return;
            }

            if (SceneManager.GetActiveScene().name == "Dodgeball")
            {
                GUI.Label(new Rect(Screen.width - 352, 96, 315, 24), "Red team vs blue team");
                var localPlayer = FindLocalNetworkPlayer();
                var roundActive = localPlayer != null && localPlayer.DodgeballRoundActive.Value;
                if (manager.IsHost)
                {
                    GUI.Label(new Rect(Screen.width - 352, 126, 250, 24), "Match code: " + hostCode);
                    if (GUI.Button(new Rect(Screen.width - 105, 123, 67, 27), "COPY"))
                        GUIUtility.systemCopyBuffer = hostCode;
                    GUI.enabled = !busy && localPlayer != null;
                    if (GUI.Button(new Rect(Screen.width - 352, 160, 314, 42),
                            roundActive ? "RESTART DODGEBALL ROUND" : "START DODGEBALL ROUND"))
                        localPlayer.RequestStartDodgeballRound();
                    GUI.enabled = true;
                }
                else if (!manager.IsListening)
                {
                    GUI.Label(new Rect(Screen.width - 352, 126, 314, 28),
                        busy ? "SEARCHING FOR ACTIVE MATCH..." : "NO ACTIVE MATCH CONNECTION");
                    GUI.enabled = !busy;
                    if (GUI.Button(new Rect(Screen.width - 352, 160, 314, 42), "RETRY AUTO MATCH"))
                        StartPublicMatchmaking("Dodgeball");
                    GUI.enabled = true;
                }
                else
                    GUI.Label(new Rect(Screen.width - 352, 142, 314, 32),
                        roundActive ? "ROUND ACTIVE - 3 HITS EACH" : "WAITING FOR HOST TO START");
                if (GUI.Button(new Rect(Screen.width - 352, 244, 314, 40), "RETURN TO BLUBCENTER"))
                    EnterBlubCenter();
                if (GUI.Button(new Rect(Screen.width - 352, 294, 314, 40), "RETURN TO BLUBHOUSE"))
                    ReturnToBlubhouse();
                GUI.enabled = true;
                GUI.Label(new Rect(Screen.width - 352, 350, 314, 32), status);
                return;
            }

            if (SceneManager.GetActiveScene().name == "BlubCenter")
            {
                GUI.Label(new Rect(Screen.width - 352, 96, 315, 24), "Automatic public BlubCenter");
                if (manager.IsListening && !string.IsNullOrEmpty(hostCode))
                {
                    GUI.Label(new Rect(Screen.width - 352, 126, 250, 24), "Server code: " + hostCode);
                    if (GUI.Button(new Rect(Screen.width - 105, 123, 67, 27), "COPY"))
                        GUIUtility.systemCopyBuffer = hostCode;
                }
                else
                    GUI.Label(new Rect(Screen.width - 352, 126, 314, 28),
                        busy ? "SEARCHING FOR PLAYERS..." : "MATCHMAKING DISCONNECTED");
                GUI.Label(new Rect(Screen.width - 352, 162, 314, 28),
                    manager.IsListening ? "MATCHED AUTOMATICALLY" : "AN OPEN SERVER WILL BE CREATED IF NEEDED");
                GUI.enabled = !busy && !manager.IsListening;
                if (GUI.Button(new Rect(Screen.width - 352, 196, 314, 34), "RETRY AUTO MATCH"))
                    StartPublicMatchmaking("BlubCenter");
                GUI.enabled = true;
                if (GUI.Button(new Rect(Screen.width - 352, 238, 314, 32), "GO TO MAKERWORLD"))
                    EnterMakerWorld();
                if (GUI.Button(new Rect(Screen.width - 352, 276, 314, 32), "GO TO DODGEBALL"))
                    EnterDodgeball();
                if (GUI.Button(new Rect(Screen.width - 352, 314, 314, 34), "RETURN TO BLUBHOUSE"))
                    ReturnToBlubhouse();
                GUI.enabled = true;
                GUI.Label(new Rect(Screen.width - 352, 356, 314, 32), status);
                return;
            }

            GUI.Label(new Rect(Screen.width - 352, 92, 315, 24), "Destination: BlubCenter");
            if (GUI.Button(new Rect(Screen.width - 352, 120, 314, 34), "GO TO BLUBCENTER"))
                EnterBlubCenter();
            if (GUI.Button(new Rect(Screen.width - 352, 160, 314, 34), "GO TO MAKERWORLD"))
                EnterMakerWorld();
            GUI.Label(new Rect(Screen.width - 352, 205, 315, 24), "PRIVATE BLUBHOUSE SERVER");

            if (!manager.IsListening)
            {
                GUI.enabled = !busy;
                if (GUI.Button(new Rect(Screen.width - 352, 234, 314, 34), "CREATE PRIVATE SERVER")) StartHost();
                GUI.Label(new Rect(Screen.width - 352, 280, 78, 24), "Join code:");
                joinCode = GUI.TextField(new Rect(Screen.width - 270, 278, 232, 26), joinCode.ToUpperInvariant(), 12);
                if (GUI.Button(new Rect(Screen.width - 352, 314, 314, 34), "JOIN PRIVATE SERVER")) StartClient();
                GUI.enabled = true;
                GUI.Label(new Rect(Screen.width - 352, 356, 314, 24), status);
            }
            else
            {
                var privateInfo = manager.IsHost ? "Private code: " + hostCode : status;
                GUI.Label(new Rect(Screen.width - 352, 204, 250, 24), privateInfo);
                if (manager.IsHost && GUI.Button(new Rect(Screen.width - 105, 201, 67, 27), "COPY"))
                    GUIUtility.systemCopyBuffer = hostCode;
                if (GUI.Button(new Rect(Screen.width - 352, 242, 314, 34), "LEAVE PRIVATE SERVER"))
                {
                    intentionalShutdown = true;
                    manager.Shutdown();
                    status = "Disconnected — restart Play mode for solo controls";
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        private void DrawSettingsMenu()
        {
            settingsButton ??= new GUIStyle(GUI.skin.button) { fontSize = 8 };
            settingsText ??= new GUIStyle(GUI.skin.label) { fontSize = 8 };
            GUI.Label(new Rect(Screen.width - 352, 102, 315, 30),
                howToPlayOpen ? "HOW TO PLAY" : shutdownConfirmOpen ? "CONFIRM SHUTDOWN" : "SETTINGS", heading);

            if (shutdownConfirmOpen)
            {
                GUI.Label(new Rect(Screen.width - 352, 140, 314, 70),
                    "Kick everyone from this room and disable multiplayer on their game for one hour?",
                    settingsText);
                if (GUI.Button(new Rect(Screen.width - 352, 226, 314, 50),
                        "YES - SHUT DOWN", settingsButton))
                {
                    var admin = FindLocalAdminPlayer();
                    if (admin != null)
                        admin.RequestOneHourShutdown();
                    else
                        settingsNotice = "JOIN A MULTIPLAYER ROOM FIRST";
                    shutdownConfirmOpen = false;
                }
                if (GUI.Button(new Rect(Screen.width - 352, 290, 314, 50), "CANCEL", settingsButton))
                    shutdownConfirmOpen = false;
                return;
            }

            if (howToPlayOpen)
            {
                var controls = "WASD  MOVE\nMOUSE  LOOK\nSPACE  JUMP\nCTRL  CROUCH\nRIGHT SHIFT  THIRD PERSON\nTAB  WATCH\nC  CHAT\nV  VOICE\nLEFT CLICK  GRAB\nRIGHT CLICK  THROW";
                GUI.Label(new Rect(Screen.width - 352, 142, 314, 205), controls, settingsText);
                if (GUI.Button(new Rect(Screen.width - 352, 352, 314, 44), "BACK", settingsButton))
                    howToPlayOpen = false;
                return;
            }

            var localAdmin = FindLocalAdminPlayer();
            var isAdminAccount = localAdmin != null || AdminAccess.IsAllowed(profilePlayerId);
            if (GUI.Button(new Rect(Screen.width - 352, 138, 314, 44), "HOW TO PLAY", settingsButton))
                howToPlayOpen = true;
            if (GUI.Button(new Rect(Screen.width - 352, 190, 314, 44), "QUIT GAME", settingsButton))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            if (isAdminAccount && GUI.Button(new Rect(Screen.width - 352, 242, 314, 44),
                    "ADMIN: SHUTDOWN 1 HOUR", settingsButton))
            {
                if (localAdmin != null)
                    shutdownConfirmOpen = true;
                else
                    settingsNotice = "JOIN A MULTIPLAYER ROOM FIRST";
            }
            if (GUI.Button(new Rect(Screen.width - 352, isAdminAccount ? 294 : 250, 314, 44),
                    "BACK", settingsButton))
                settingsMenuOpen = false;
            if (loadingProfileId)
                GUI.Label(new Rect(Screen.width - 352, 346, 314, 36), "CHECKING ADMIN ACCESS...", settingsText);
            else if (!string.IsNullOrEmpty(settingsNotice))
                GUI.Label(new Rect(Screen.width - 352, 346, 314, 36), settingsNotice, settingsText);
        }

        private static NetworkPlayer FindLocalAdminPlayer()
        {
            var player = FindLocalNetworkPlayer();
            return player != null && player.IsAdmin.Value ? player : null;
        }

        private static NetworkPlayer FindLocalNetworkPlayer()
        {
            foreach (var player in FindObjectsOfType<NetworkPlayer>())
                if (player.IsOwner && player.IsSpawned)
                    return player;
            return null;
        }

        private void DrawUsernameMenu()
        {
            GUI.Label(new Rect(Screen.width - 352, 102, 315, 26), "CHANGE USERNAME", heading);
            GUI.Label(new Rect(Screen.width - 352, 142, 315, 24), "This is visible to everyone:");
            usernameDraft = GUI.TextField(new Rect(Screen.width - 352, 174, 314, 32), usernameDraft, 20);
            GUI.Label(new Rect(Screen.width - 352, 214, 315, 24), "Unity Player ID:");
            GUI.Label(new Rect(Screen.width - 352, 238, 315, 24), profilePlayerId);
            var adminStatus = AdminAccess.IsAllowed(profilePlayerId) ? "Admin access: ENABLED" : "Admin access: not allowed";
            GUI.Label(new Rect(Screen.width - 352, 264, 315, 24), adminStatus);
            GUI.enabled = !loadingProfileId && !string.IsNullOrWhiteSpace(profilePlayerId) && profilePlayerId != "Loading…";
            if (GUI.Button(new Rect(Screen.width - 352, 292, 314, 30), "COPY PLAYER ID"))
            {
                GUIUtility.systemCopyBuffer = profilePlayerId;
                status = "Player ID copied";
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(Screen.width - 352, 324, 314, 34), "SAVE USERNAME"))
            {
                usernameDraft = string.IsNullOrWhiteSpace(usernameDraft) ? "Player" : usernameDraft.Trim();
                PlayerPrefs.SetString("BlubRoom.Username", usernameDraft);
                PlayerPrefs.Save();
                foreach (var player in FindObjectsOfType<NetworkPlayer>())
                    if (player.IsOwner) player.SetDisplayName(usernameDraft);
                status = "Username changed to " + usernameDraft;
                usernameMenuOpen = false;
            }
            if (GUI.Button(new Rect(Screen.width - 352, 366, 314, 32), "BACK"))
                usernameMenuOpen = false;
        }

        private async void LoadProfilePlayerId()
        {
            if (loadingProfileId) return;
            loadingProfileId = true;
            profilePlayerId = "Loading…";
            try
            {
                await SignIn();
                profilePlayerId = AuthenticationService.Instance.PlayerId;
            }
            catch (System.Exception exception)
            {
                profilePlayerId = "Could not load ID";
                Debug.LogException(exception);
            }
            finally { loadingProfileId = false; }
        }

        private void PrepareForNetwork()
        {
            foreach (var motor in FindObjectsOfType<PlayerMotor>()) Destroy(motor.gameObject);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public async void StartHost()
        {
            if (AdminShutdownGate.IsActive)
            {
                status = "Multiplayer shut down — " + AdminShutdownGate.RemainingText + " remaining";
                return;
            }
            busy = true;
            status = "Creating Relay game…";
            try
            {
                await SignIn();
                var allocation = await RelayService.Instance.CreateAllocationAsync(7);
                hostCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                var relayData = new RelayServerData(allocation, "dtls");
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayData);
                PrepareForNetwork();
                intentionalShutdown = false;
                if (NetworkManager.Singleton.StartHost())
                {
                    status = "Share code " + hostCode;
                    // Do not depend only on NGO's host connection callback; it can
                    // run during StartHost before other services finish their setup.
                    GetComponent<VoiceChatManager>().JoinVoice(hostCode);
                }
                else
                {
                    status = "Could not start host";
                }
            }
            catch (System.Exception exception)
            {
                status = "Relay failed: " + exception.Message;
                Debug.LogException(exception);
            }
            finally { busy = false; }
        }

        public async void EnterBlubCenter()
        {
            if (busy) return;
            busy = true;
            status = "Loading BlubCenter…";
            try
            {
                await LeaveCurrentPublicLobby();
                if (NetworkManager.Singleton.IsListening)
                {
                    intentionalShutdown = true;
                    NetworkManager.Singleton.Shutdown();
                    await System.Threading.Tasks.Task.Delay(150);
                    intentionalShutdown = false;
                }

                await LoadingScreen.LoadScene("BlubCenter");
            }
            finally { busy = false; }

            StartPublicMatchmaking("BlubCenter");
        }

        public async void EnterMakerWorld()
        {
            if (busy) return;
            busy = true;
            status = "Loading MakerWorld…";
            await LeaveCurrentPublicLobby();
            if (NetworkManager.Singleton.IsListening)
            {
                intentionalShutdown = true;
                NetworkManager.Singleton.Shutdown();
                await System.Threading.Tasks.Task.Delay(150);
            }
            await LoadingScreen.LoadScene("MakerWorld");
            intentionalShutdown = false;
            status = "MakerWorld — press F to build";
            busy = false;
            StartHost();
        }

        public async void EnterDodgeball()
        {
            if (busy) return;
            busy = true;
            status = "Loading Dodgeball…";
            await LeaveCurrentPublicLobby();
            if (NetworkManager.Singleton.IsListening)
            {
                intentionalShutdown = true;
                NetworkManager.Singleton.Shutdown();
                await System.Threading.Tasks.Task.Delay(150);
            }
            await LoadingScreen.LoadScene("Dodgeball");
            intentionalShutdown = false;
            status = "Finding a Dodgeball match…";
            busy = false;
            StartPublicMatchmaking("Dodgeball");
        }

        public async void ReturnToBlubhouse()
        {
            if (busy) return;
            busy = true;
            status = "Returning to Blubhouse…";
            await LeaveCurrentPublicLobby();
            if (NetworkManager.Singleton.IsListening)
            {
                intentionalShutdown = true;
                NetworkManager.Singleton.Shutdown();
                await System.Threading.Tasks.Task.Delay(150);
            }
            await LoadingScreen.LoadScene("Main");
            status = "Blubhouse — offline";
            intentionalShutdown = false;
            busy = false;
        }

        public async void StartClient()
        {
            if (AdminShutdownGate.IsActive)
            {
                status = "Multiplayer shut down — " + AdminShutdownGate.RemainingText + " remaining";
                return;
            }
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                status = "Enter the host's join code";
                return;
            }

            busy = true;
            status = "Joining Relay game…";
            try
            {
                if (NetworkManager.Singleton.IsListening)
                {
                    await LeaveCurrentPublicLobby();
                    intentionalShutdown = true;
                    NetworkManager.Singleton.Shutdown();
                    await System.Threading.Tasks.Task.Delay(150);
                    intentionalShutdown = false;
                }
                await SignIn();
                var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpperInvariant());
                var relayData = new RelayServerData(allocation, "dtls");
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayData);
                PrepareForNetwork();
                intentionalShutdown = false;
                status = NetworkManager.Singleton.StartClient() ? "Connecting…" : "Could not start client";
            }
            catch (System.Exception exception)
            {
                status = "Join failed: " + exception.Message;
                Debug.LogException(exception);
            }
            finally { busy = false; }
        }

        public async void StartPublicMatchmaking(string queue)
        {
            if (busy || (queue != "BlubCenter" && queue != "Dodgeball")) return;
            if (AdminShutdownGate.IsActive)
            {
                status = "Multiplayer shut down — " + AdminShutdownGate.RemainingText + " remaining";
                return;
            }

            busy = true;
            status = "Searching for an active " + queue + " server…";
            try
            {
                await SignIn();
                await LeaveCurrentPublicLobby();
                if (NetworkManager.Singleton.IsListening)
                {
                    intentionalShutdown = true;
                    NetworkManager.Singleton.Shutdown();
                    await System.Threading.Tasks.Task.Delay(150);
                    intentionalShutdown = false;
                }

                var query = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 25,
                    Filters = new List<QueryFilter>
                    {
                        new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                        new(QueryFilter.FieldOptions.S1, queue, QueryFilter.OpOptions.EQ),
                        new(QueryFilter.FieldOptions.S2, MatchmakingVersion, QueryFilter.OpOptions.EQ)
                    }
                });

                foreach (var candidate in query.Results)
                    if (await TryJoinPublicLobby(candidate, queue)) return;

                await CreatePublicLobby(queue);
            }
            catch (System.Exception exception)
            {
                status = "Automatic matchmaking failed: " + exception.Message;
                Debug.LogException(exception);
            }
            finally { busy = false; }
        }

        private async System.Threading.Tasks.Task<bool> TryJoinPublicLobby(Lobby candidate, string queue)
        {
            Lobby joinedLobby = null;
            try
            {
                joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(candidate.Id,
                    new JoinLobbyByIdOptions
                    {
                        Player = new Unity.Services.Lobbies.Models.Player(
                            id: AuthenticationService.Instance.PlayerId)
                    });
                if (joinedLobby.Data == null ||
                    !joinedLobby.Data.TryGetValue(LobbyRelayCodeKey, out var relayCodeData) ||
                    string.IsNullOrWhiteSpace(relayCodeData.Value))
                    throw new System.InvalidOperationException("Matched lobby has no Relay code");

                var relayCode = relayCodeData.Value.Trim().ToUpperInvariant();
                var allocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
                    new RelayServerData(allocation, "dtls"));
                hostCode = relayCode;
                joinCode = relayCode;
                activeLobby = joinedLobby;
                PrepareForNetwork();
                intentionalShutdown = false;
                if (!NetworkManager.Singleton.StartClient())
                    throw new System.InvalidOperationException("Could not start network client");
                status = "Joined an active " + queue + " server automatically";
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.Log("Could not use public lobby " + candidate.Id + ": " + exception.Message);
                if (joinedLobby != null)
                {
                    try
                    {
                        await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id,
                            AuthenticationService.Instance.PlayerId);
                    }
                    catch (System.Exception) { }
                }
                activeLobby = null;
                return false;
            }
        }

        private async System.Threading.Tasks.Task CreatePublicLobby(string queue)
        {
            status = "No active players found — creating a " + queue + " server…";
            var allocation = await RelayService.Instance.CreateAllocationAsync(7);
            var relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = new Unity.Services.Lobbies.Models.Player(
                    id: AuthenticationService.Instance.PlayerId),
                Data = new Dictionary<string, DataObject>
                {
                    [LobbyQueueKey] = new(DataObject.VisibilityOptions.Public, queue,
                        DataObject.IndexOptions.S1),
                    [LobbyVersionKey] = new(DataObject.VisibilityOptions.Public, MatchmakingVersion,
                        DataObject.IndexOptions.S2),
                    [LobbyRelayCodeKey] = new(DataObject.VisibilityOptions.Member, relayCode)
                }
            };

            activeLobby = await LobbyService.Instance.CreateLobbyAsync(
                "BlubRoom " + queue, 8, options);
            hostCode = relayCode;
            joinCode = relayCode;
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
                new RelayServerData(allocation, "dtls"));
            PrepareForNetwork();
            intentionalShutdown = false;
            if (!NetworkManager.Singleton.StartHost())
            {
                await LeaveCurrentPublicLobby();
                throw new System.InvalidOperationException("Could not start public host");
            }

            StartLobbyHeartbeat();
            GetComponent<VoiceChatManager>().JoinVoice(relayCode);
            status = "Created a public " + queue + " server — waiting for players";
        }

        private void StartLobbyHeartbeat()
        {
            StopLobbyHeartbeat();
            if (activeLobby != null)
                lobbyHeartbeat = StartCoroutine(LobbyHeartbeat(activeLobby.Id));
        }

        private IEnumerator LobbyHeartbeat(string lobbyId)
        {
            var delay = new WaitForSecondsRealtime(15f);
            while (activeLobby != null && activeLobby.Id == lobbyId)
            {
                yield return delay;
                SendLobbyHeartbeat(lobbyId);
            }
        }

        private async void SendLobbyHeartbeat(string lobbyId)
        {
            try { await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId); }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Lobby heartbeat failed: " + exception.Message);
            }
        }

        private void StopLobbyHeartbeat()
        {
            if (lobbyHeartbeat == null) return;
            StopCoroutine(lobbyHeartbeat);
            lobbyHeartbeat = null;
        }

        private async System.Threading.Tasks.Task LeaveCurrentPublicLobby()
        {
            var lobby = activeLobby;
            activeLobby = null;
            StopLobbyHeartbeat();
            if (lobby == null || !AuthenticationService.Instance.IsSignedIn) return;

            try
            {
                if (lobby.HostId == AuthenticationService.Instance.PlayerId)
                    await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                else
                    await LobbyService.Instance.RemovePlayerAsync(lobby.Id,
                        AuthenticationService.Instance.PlayerId);
            }
            catch (System.Exception exception)
            {
                Debug.Log("Lobby cleanup finished with: " + exception.Message);
            }
        }

        public async void ApplyAdminShutdown(long untilUtcTicks)
        {
            AdminShutdownGate.ActivateUntil(untilUtcTicks);
            intentionalShutdown = true;
            busy = true;
            watchOpen = false;
            usernameMenuOpen = false;
            settingsMenuOpen = false;
            howToPlayOpen = false;
            shutdownConfirmOpen = false;
            status = "Admin shut down multiplayer for one hour";

            // Leave enough time for the server's RPC packet to reach every client
            // before the host closes the Relay room.
            await System.Threading.Tasks.Task.Delay(600);
            await LeaveCurrentPublicLobby();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            await System.Threading.Tasks.Task.Delay(150);
            if (SceneManager.GetActiveScene().name != "Main")
                await LoadingScreen.LoadScene("Main");

            status = "Multiplayer shut down — " + AdminShutdownGate.RemainingText + " remaining";
            busy = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static async System.Threading.Tasks.Task SignIn()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
}
