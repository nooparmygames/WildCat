using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WildCat.Protocol;
using WildCat.Unity;

namespace WildCat.Samples.Basic
{
    /// <summary>
    /// An IMGUI walk through the SDK: connect and log in with a device id, find a match and relay
    /// cube positions between two clients, submit a score through a server script event and read
    /// the leaderboard, and invite, accept and list friends. Put it next to a WildCat Client
    /// component (WildCatBehaviour) whose settings point at a server; see README.md.
    /// </summary>
    [RequireComponent(typeof(WildCatBehaviour))]
    public sealed class BasicSample : MonoBehaviour
    {
        private static readonly WildCatMsgType PositionType = RelayClient.MessageType(1);

        [Tooltip("The matchmaking game mode both clients look for.")]
        [SerializeField]
        private string gameMode = "sample-duel";

        [Tooltip("The leaderboard the score script writes to (the development host seeds \"l1\").")]
        [SerializeField]
        private string leaderboard = "l1";

        [Tooltip("The server script event that sets the score (see README.md).")]
        [SerializeField]
        private string scoreEvent = "submitScore";

        [Tooltip("Position messages sent per second during a match.")]
        [Min(1)]
        [SerializeField]
        private float sendsPerSecond = 10;

        [Tooltip("How far in the past the other player's cube is shown, in seconds, to smooth its movement.")]
        [Min(0)]
        [SerializeField]
        private float interpolationBackTime = 0.2f;

        private readonly List<string> log = new List<string>();
        private readonly TimestampedBuffer<Vector3> remotePositions =
            new TimestampedBuffer<Vector3>((a, b, t) => Vector3.Lerp(a, b, t));

        private WildCatBehaviour wildCat;
        private WildCatClient client;
        private IDisposable positionHandler;
        private string deviceId;
        private string status = "Not connected";
        private string friendIdInput = "";
        private string scoreInput = "100";
        private bool busy;
        private MatchInfo match;
        private GameObject localCube;
        private GameObject remoteCube;
        private float nextSendTime;
        private IReadOnlyList<LeaderboardEntry> leaderboardRows;
        private FriendList friends;
        private FriendEvent pendingInvitation;
        private Vector2 scroll;

        private void Awake()
        {
            wildCat = GetComponent<WildCatBehaviour>();
            // A random id, so a second editor or a build on the same machine is a second player.
            deviceId = "sample-" + UnityEngine.Random.Range(100000, 999999);
        }

        private void Start()
        {
            client = wildCat.Client;
            client.Reconnecting += e => Log("Connection lost (" + e.Reason + "); reconnect attempt " + e.Attempt + " in " + e.Delay.TotalSeconds.ToString("0.0") + " s");
            client.Reconnected += () =>
            {
                Log("Reconnected; the session and login were resumed");
                // Matches are not rejoined on their own. This sample simply ends the match.
                EndMatch();
            };
            client.Disconnected += e =>
            {
                status = "Disconnected (" + e + ")";
                EndMatch();
            };
            client.Friends.InvitationReceived += e =>
            {
                pendingInvitation = e;
                Log("Friend invitation from " + Describe(e.PlayerName, e.PlayerId));
            };
            client.Friends.InvitationAccepted += e =>
            {
                Log(Describe(e.PlayerName, e.PlayerId) + " accepted your invitation");
                RefreshFriends();
            };
            client.Friends.InvitationDenied += e => Log(Describe(e.PlayerName, e.PlayerId) + " denied your invitation");
            client.Friends.StatusChanged += e =>
            {
                Log(Describe(e.PlayerName, e.PlayerId) + " is " + e.Status);
                RefreshFriends();
            };
            client.Matches.PlayerJoined += e => Log("In the match: " + Describe(e.Player, "a player"));
            client.Matches.PlayerLeft += e => Log("Left the match: " + e.Player);
            client.Matches.Finished += e =>
            {
                if (match != null && e.MatchId == match.Id)
                {
                    Log(e.ByDisconnect ? "The match ended: the other player disconnected" : "The match ended");
                    EndMatch();
                }
            };
            positionHandler = client.Relay.On<PositionMessage>(PositionType, OnRemotePosition);
        }

        private void OnDestroy()
        {
            if (positionHandler != null)
                positionHandler.Dispose();
        }

        private void Update()
        {
            if (match == null || localCube == null)
                return;

            // The local cube moves on its own, so the sample needs no input setup.
            float angle = Time.time * 0.8f;
            localCube.transform.position = new Vector3(Mathf.Cos(angle) * 2f, 0.5f, Mathf.Sin(angle) * 2f);

            if (Time.time >= nextSendTime)
            {
                nextSendTime = Time.time + 1f / sendsPerSecond;
                client.Relay.Send(match.Id, PositionType, new PositionMessage { Position = localCube.transform.position });
            }

            Vector3 remote;
            if (remoteCube != null && remotePositions.TryGetValue(Time.time - interpolationBackTime, out remote))
                remoteCube.transform.position = remote;
        }

        private void OnRemotePosition(PositionMessage message, Guid matchId)
        {
            if (match == null || matchId != match.Id)
                return;
            if (remoteCube == null)
            {
                remoteCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                remoteCube.name = "Other player";
                remoteCube.GetComponent<Renderer>().material.color = Color.blue;
            }
            // Relay handlers run on the main thread, so Unity objects are safe to touch here.
            remotePositions.Add(Time.time, message.Position);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20));
            GUILayout.Label("WildCat Basic sample");
            GUILayout.Label("Status: " + status);
            if (client != null && client.IsConnected)
                GUILayout.Label("Round trip: " + client.Connection.SmoothedRoundTripTime.TotalMilliseconds.ToString("0") + " ms");

            GUI.enabled = !busy;
            if (client == null || !client.IsLoggedIn)
                DrawLogin();
            else
                DrawMainMenu();
            GUI.enabled = true;

            GUILayout.Space(10);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(160));
            for (int i = log.Count - 1; i >= 0; --i)
                GUILayout.Label(log[i]);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLogin()
        {
            GUILayout.Label("Device id");
            deviceId = GUILayout.TextField(deviceId);
            if (GUILayout.Button("Connect and log in"))
                Run("Logging in", ConnectAndLoginAsync);
        }

        private void DrawMainMenu()
        {
            GUILayout.Label("Your player id: " + client.Auth.PlayerId);
            if (GUILayout.Button("Copy player id"))
                GUIUtility.systemCopyBuffer = client.Auth.PlayerId;

            GUILayout.Space(6);
            if (match == null)
            {
                if (GUILayout.Button("Find match (" + gameMode + ")"))
                    Run("Looking for a match", FindMatchAsync);
            }
            else if (GUILayout.Button("Leave match"))
            {
                Run("Leaving the match", LeaveMatchAsync);
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Score", GUILayout.Width(50));
            scoreInput = GUILayout.TextField(scoreInput, GUILayout.Width(80));
            if (GUILayout.Button("Submit through " + scoreEvent))
                Run("Submitting the score", SubmitScoreAsync);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Show leaderboard " + leaderboard))
                Run("Reading the leaderboard", ReadLeaderboardAsync);
            if (leaderboardRows != null)
            {
                if (leaderboardRows.Count == 0)
                    GUILayout.Label("  (no scores yet)");
                for (int i = 0; i < leaderboardRows.Count; ++i)
                    GUILayout.Label("  " + (i + 1) + ". " + Describe(leaderboardRows[i].PlayerName, leaderboardRows[i].PlayerId) + "  " + leaderboardRows[i].Score);
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            friendIdInput = GUILayout.TextField(friendIdInput, GUILayout.Width(250));
            if (GUILayout.Button("Invite by player id"))
                Run("Inviting", InviteAsync);
            GUILayout.EndHorizontal();
            if (pendingInvitation != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Invitation from " + Describe(pendingInvitation.PlayerName, pendingInvitation.PlayerId));
                if (GUILayout.Button("Accept"))
                    Run("Accepting", () => AnswerInvitationAsync(true));
                if (GUILayout.Button("Deny"))
                    Run("Denying", () => AnswerInvitationAsync(false));
                GUILayout.EndHorizontal();
            }
            if (friends != null)
            {
                GUILayout.Label("Friends: " + friends.Friends.Count);
                foreach (Friend friend in friends.Friends)
                    GUILayout.Label("  " + Describe(friend.Name, friend.PlayerId) + " (" + friend.Status + ")");
            }
        }

        private async Task ConnectAndLoginAsync()
        {
            await wildCat.ConnectAndInitializeAsync();
            LoginResult login = await client.Auth.LoginWithDeviceAsync(deviceId);
            status = "Logged in" + (login.IsNewAccount ? " (new account)" : "");
            Log("Logged in as " + login.PlayerId);
            await RefreshFriendsAsync();
        }

        private async Task FindMatchAsync()
        {
            MatchInfo found = await client.Matchmaking.FindMatchAsync(gameMode, 0, TimeSpan.FromSeconds(15));
            if (found == null)
            {
                Log("Nobody else was looking for a match. Start a second client and try again.");
                return;
            }
            match = found;
            remotePositions.Clear();
            localCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            localCube.name = "You";
            Log("Match " + match.Id + " with " + match.PlayerIds.Count + " players");
        }

        private async Task LeaveMatchAsync()
        {
            MatchInfo leaving = match;
            EndMatch();
            if (leaving != null)
                await client.Matches.LeaveAsync(leaving.Id);
        }

        private void EndMatch()
        {
            match = null;
            remotePositions.Clear();
            if (localCube != null)
                Destroy(localCube);
            if (remoteCube != null)
                Destroy(remoteCube);
            localCube = null;
            remoteCube = null;
        }

        private async Task SubmitScoreAsync()
        {
            int score;
            if (!int.TryParse(scoreInput, out score))
            {
                Log("The score must be a whole number.");
                return;
            }
            // Clients cannot set scores; the server script decides what to store.
            int rank = await client.Scripts.ExecuteAsync<int>(scoreEvent, score);
            Log("Score submitted; your rank is " + rank);
            await ReadLeaderboardAsync();
        }

        private async Task ReadLeaderboardAsync()
        {
            LeaderboardPage page = await client.Leaderboards.GetTopAsync(leaderboard, 0, 10);
            leaderboardRows = page.Entries;
        }

        private async Task InviteAsync()
        {
            string id = friendIdInput.Trim();
            if (id.Length == 0)
            {
                Log("Paste the other player's id first.");
                return;
            }
            await client.Friends.InviteByPlayerIdAsync(id);
            Log("Invitation sent");
        }

        private async Task AnswerInvitationAsync(bool accept)
        {
            FriendEvent invitation = pendingInvitation;
            pendingInvitation = null;
            if (invitation == null)
                return;
            if (accept)
                await client.Friends.AcceptAsync(invitation.PlayerId);
            else
                await client.Friends.DenyAsync(invitation.PlayerId);
            await RefreshFriendsAsync();
        }

        private void RefreshFriends()
        {
            Run(null, RefreshFriendsAsync);
        }

        private async Task RefreshFriendsAsync()
        {
            friends = await client.Friends.GetAsync();
        }

        /// <summary>Runs one call at a time from a button and logs its failure instead of throwing.</summary>
        private async void Run(string what, Func<Task> action)
        {
            if (what != null)
            {
                if (busy)
                    return;
                busy = true;
                status = what + "...";
            }
            try
            {
                await action();
            }
            catch (WildCatException ex)
            {
                Log("Server error " + ex.ErrorCode + ": " + ex.Message);
            }
            catch (Exception ex)
            {
                Log(ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (what != null)
                {
                    busy = false;
                    if (status == what + "...")
                        status = client != null && client.IsLoggedIn ? "Logged in" : "Not logged in";
                }
            }
        }

        private void Log(string message)
        {
            log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            if (log.Count > 50)
                log.RemoveAt(0);
        }

        private static string Describe(string name, string fallback)
        {
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
    }
}
