using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using WildCat.Protocol;
using EmptyMessage = WildCat.Protocol.EmptyMessage;
using System.IO;

namespace WildCat
{

    /// <summary>
    /// This class is the main entry point for the whole API.
    /// There is a singleton available which gives you access to all public methods but you can create
    /// multiple instances of the class for testing purposes inside one application.
    /// </summary>
    public class BackendPlatform : MonoBehaviour
    {
        /// <summary>
        /// Current major version of the WildCat SDK
        /// </summary>
        public static int majorVersion = 2;

        /// <summary>
        /// Current minor version of the WildCat SDK
        /// </summary>
        public static int minorVersion = 0;

        /// <summary>
        /// Current hot fix version of the WildCat SDK
        /// </summary>
        public static int hotFixVersion = 0;

        /// <summary>
        /// Release date of latest WildCat version
        /// </summary>
        public static DateTime releaseDate = new DateTime(2019, 8, 15);

        private Client client;

        private Dictionary<uint, Result<object>> scriptEventExecutedResults = new Dictionary<uint, Result<object>>();

        private Dictionary<uint, Result<Guid>> sessionInitializedResults = new Dictionary<uint, Result<Guid>>();

        private Dictionary<uint, Result> nameSetResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result<ChallengeRequestsMessage>> getChallengesResults = new Dictionary<uint, Result<ChallengeRequestsMessage>>();
        private Dictionary<uint, Result<string>> nameReceivedResults = new Dictionary<uint, Result<string>>();
        private Dictionary<uint, Result<GetInventoryResponseMessage>> inventoryReceivedResults = new Dictionary<uint, Result<GetInventoryResponseMessage>>();
        private Dictionary<uint, Result<BuyItemInventoryRequestMessage>> inventoryAddedResults = new Dictionary<uint, Result<BuyItemInventoryRequestMessage>>();
        private Dictionary<uint, Result<RemoveItemInventoryRequestMessage>> inventoryRemovedResults = new Dictionary<uint, Result<RemoveItemInventoryRequestMessage>>();
        private Dictionary<uint, Result<string>> playerNameReceivedResults = new Dictionary<uint, Result<string>>();
        private Dictionary<uint, Result<DateTime>> timeReceivedResults = new Dictionary<uint, Result<DateTime>>();
        private Dictionary<uint, Result> cloudDataSavedResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> cloudPublicDataSavedResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result<string>> cloudDataReceivedResults = new Dictionary<uint, Result<string>>();
        private Dictionary<uint, Result<GetPublicCloudDataResponse>> cloudPublicDataReceivedResults = new Dictionary<uint, Result<GetPublicCloudDataResponse>>();
        private Dictionary<uint, Result<GetPublicCloudDataResponse>> cloudPlayerPublicDataReceivedResults = new Dictionary<uint, Result<GetPublicCloudDataResponse>>();
        private Dictionary<uint, Result<string>> publisherDataReceivedResults = new Dictionary<uint, Result<string>>();
        private Dictionary<uint, Result> scoreSetInLeaderBoardResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result<int>> playerRankReceivedResults = new Dictionary<uint, Result<int>>();
        private Dictionary<uint, Result<int, List<LeaderBoardEntry>>> topLeaderBoardEntriesReceivedResults = new Dictionary<uint, Result<int, List<LeaderBoardEntry>>>();
        private Dictionary<uint, Result<int, List<LeaderBoardEntry>>> aroundMeLeaderBoardEntriesReceivedResults = new Dictionary<uint, Result<int, List<LeaderBoardEntry>>>();
        private Dictionary<uint, Result> challengeSentResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result<FriendlistCollection>> getFriendsResults = new Dictionary<uint, Result<FriendlistCollection>>();
        private Dictionary<uint, Result> inviteFriendsResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> acceptFriendsResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> denyFriendsResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> acceptChallengeSentResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> denyChallengeSentResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> finishGameSentResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> setMatchPropertyResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> matchRejoinResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result<PendingMatchesCollectionData>> getMatchesResults = new Dictionary<uint, Result<PendingMatchesCollectionData>>();
        private Dictionary<uint, Result<FindMatchResponse>> gameFoundResults = new Dictionary<uint, Result<FindMatchResponse>>();

        private Dictionary<uint, Action> failedToFIndGameCallbacks = new Dictionary<uint, Action>();
        private Dictionary<uint, Result<AuthenticationResponse>> authResults = new Dictionary<uint, Result<AuthenticationResponse>>();
        private Dictionary<uint, Result<string>> createAccountResults = new Dictionary<uint, Result<string>>();
        private Dictionary<uint, Result> addAuthToAccountResults = new Dictionary<uint, Result>();
        private Dictionary<uint, Result> emailActivationResults = new Dictionary<uint, Result>();

        /// <summary>
        /// This singleton gives you access to the rest of the API.
        /// </summary>
        public static BackendPlatform Instance
        {
            get
            {
                return _instance;
            }
        }
        private static BackendPlatform _instance;


        public class FriendData { public string playerId; public string playerName; public byte[] data; }

        /// <summary>
        /// Received the list of friends and invitations
        /// </summary>
        public event Action<FriendlistCollection> OnFriendListReceived;

        /// <summary>
        /// An invitation for friendship is received
        /// </summary>
        public event Action<FriendData> OnFriendshipInvitation;

        /// <summary>
        /// Your friendship request accepted
        /// </summary>
        public event Action<FriendData> OnFriendshipAccepted;

        /// <summary>
        /// Your friendship request denied
        /// </summary>
        public event Action<FriendData> OnFriendshipDenied;

        /// <summary>
        /// Online status of one of your friends changed
        /// </summary>
        public event Action<string, OnlineStatus> OnFriendStatusChanged;

        /// <summary>
        /// Fired when a game is finished with GUId of the game
        /// </summary>
        public event Action<Guid> OnGameFinished;

        /// <summary>
        /// Fired if game is fnished due to opponent disconnect
        /// </summary>
        public event Action<Guid> OnGameFinishedDueToDisconnect;

        /// <summary>
        /// Fired when a challenge request from another player is received
        /// </summary>
        public event Action<string, string, uint, byte[]> OnchallengeRequestReceived;

        /// <summary>
        /// Fired when a challenge gets started on both ends (sender and accepter)
        /// </summary>
        public event Action<string, string, Guid, byte[]> OnchallengeStarted;

        public event Action<Guid, string, string, byte[]> OnPlayerInMatchPropertyChanged;
        public event Action<Guid, string, byte[]> OnMatchPropertyChanged;
        public Action<MatchPlayerEventData> OnMatchPlayerRemoved;
        public Action<MatchPlayerEventData> OnMatchPlayerRejoined;
        public Action<MatchSyncEventData> OnMatchRejoinSyncDataReceived;

        /// <summary>
        /// Fired when another user denies this user's challenge request
        /// </summary>
        public event Action<string, string, byte[]> OnchallengeDenied;

        /// <summary>
        /// This event is called when a connection to the server is successfully completed.
        /// </summary>
        /// <remarks>
        /// You can call other methods only after this event is fired.
        /// </remarks>
        public event Action<int> OnConnected;

        /// <summary>
        /// This event is called if the player is disconnected from the server.
        /// </summary>
        public Action<int> OnDisconnected;

        /// <summary>
        /// This event fires-up when Connection to server failed
        /// </summary>
        public event Action OnConnectionFailed;



        /// <summary>
        /// This property contains the player's name after you login.
        /// </summary>
        public string userName { get; private set; }

        /// <summary>
        /// Ticket/token of the loggedIn user to be used in other calls
        /// </summary>
        private string ticket { get; set; }

        /// <summary>
        /// This property contains the player's user ID after you login.
        /// </summary>
        public string userID { get; private set; }

        /// <summary>
        /// Are you connected to the server or not.
        /// </summary>
        public bool isConnected { get; private set; }

        /// <summary>
        /// Tells us of the session is initialized
        /// </summary>
        private bool isSessionInitialized { get { return sessionKey != Guid.Empty; } }

        /// <summary>
        /// Are you loggedIn to the server or not.
        /// </summary>
        /// <remarks>
        /// When this is true, you can also see your User ID and User Name using the respective properties as well.
        /// </remarks>
        public bool isLoggedIn { get { return !string.IsNullOrEmpty(userID); } }

        /// <summary>
        /// The developer key to be used
        /// </summary>
        public Guid developerKey { get; private set; }

        /// <summary>
        /// The game key to be used
        /// </summary>
        public Guid gameKey { get; private set; }

        private Guid sessionKey = Guid.Empty;

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else
            {
                Debug.LogError("You have to use only one GamePlatform object but there is more in the scene.");
                return;
            }

            DontDestroyOnLoad(this.gameObject);
            client = this.gameObject.AddComponent<Client>();
            Application.runInBackground = true;
            RegisterMessageHandlers();
        }

        private void Update()
        {
            if (client != null && !string.IsNullOrEmpty(ticket))
                client.SendHeartBeatIfNeeded(ticket);
        }

        private void RegisterMessageHandlers()
        {
            client.OnDisconnected += x =>
            {
                isConnected = false;
                userID = "";
                userName = "";
                ticket = "";
                sessionKey = Guid.Empty;

                foreach (var res in sessionInitializedResults)
                    res.Value.Fail(0, "Disconnected");
                sessionInitializedResults.Clear();

                foreach (var res in scriptEventExecutedResults)
                    res.Value.Fail(0, "Disconnected");
                scriptEventExecutedResults.Clear();


                foreach (var res in nameSetResults)
                    res.Value.Fail(0, "Disconnected");
                nameSetResults.Clear();

                foreach (var res in getChallengesResults)
                    res.Value.Fail(0, "Disconnected");
                getChallengesResults.Clear();

                foreach (var res in nameReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                nameReceivedResults.Clear();

                foreach (var res in inventoryReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                inventoryReceivedResults.Clear();

                foreach (var res in inventoryAddedResults)
                    res.Value.Fail(0, "Disconnected");
                inventoryAddedResults.Clear();

                foreach (var res in inventoryRemovedResults)
                    res.Value.Fail(0, "Disconnected");
                inventoryRemovedResults.Clear();

                foreach (var res in playerNameReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                playerNameReceivedResults.Clear();

                foreach (var res in timeReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                timeReceivedResults.Clear();

                foreach (var res in cloudDataSavedResults)
                    res.Value.Fail(0, "Disconnected");
                cloudDataSavedResults.Clear();

                foreach (var res in cloudPublicDataSavedResults)
                    res.Value.Fail(0, "Disconnected");
                cloudPublicDataSavedResults.Clear();

                foreach (var res in cloudDataReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                cloudDataReceivedResults.Clear();

                foreach (var res in cloudPublicDataReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                cloudPublicDataReceivedResults.Clear();

                foreach (var res in cloudPlayerPublicDataReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                cloudPlayerPublicDataReceivedResults.Clear();

                foreach (var res in publisherDataReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                publisherDataReceivedResults.Clear();

                foreach (var res in scoreSetInLeaderBoardResults)
                    res.Value.Fail(0, "Disconnected");
                scoreSetInLeaderBoardResults.Clear();

                foreach (var res in playerRankReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                playerRankReceivedResults.Clear();

                foreach (var res in topLeaderBoardEntriesReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                topLeaderBoardEntriesReceivedResults.Clear();

                foreach (var res in aroundMeLeaderBoardEntriesReceivedResults)
                    res.Value.Fail(0, "Disconnected");
                aroundMeLeaderBoardEntriesReceivedResults.Clear();

                foreach (var res in getFriendsResults)
                    res.Value.Fail(0, "Disconnected");
                getFriendsResults.Clear();

                foreach (var res in inviteFriendsResults)
                    res.Value.Fail(0, "Disconnected");
                inviteFriendsResults.Clear();

                foreach (var res in acceptFriendsResults)
                    res.Value.Fail(0, "Disconnected");
                acceptFriendsResults.Clear();

                foreach (var res in denyFriendsResults)
                    res.Value.Fail(0, "Disconnected");
                denyFriendsResults.Clear();

                foreach (var res in challengeSentResults)
                    res.Value.Fail(0, "Disconnected");
                challengeSentResults.Clear();

                foreach (var res in acceptChallengeSentResults)
                    res.Value.Fail(0, "Disconnected");
                acceptChallengeSentResults.Clear();

                foreach (var res in denyChallengeSentResults)
                    res.Value.Fail(0, "Disconnected");
                denyChallengeSentResults.Clear();

                foreach (var res in finishGameSentResults)
                    res.Value.Fail(0, "Disconnected");
                finishGameSentResults.Clear();

                foreach (var res in setMatchPropertyResults)
                    res.Value.Fail(0, "Disconnected");
                setMatchPropertyResults.Clear();

                foreach (var res in matchRejoinResults)
                    res.Value.Fail(0, "Disconnected");
                matchRejoinResults.Clear();

                foreach (var res in getMatchesResults)
                    res.Value.Fail(0, "Disconnected");
                getMatchesResults.Clear();

                foreach (var res in gameFoundResults)
                    res.Value.Fail(0, "Disconnected");
                gameFoundResults.Clear();


                failedToFIndGameCallbacks.Clear();

                foreach (var res in authResults)
                    res.Value.Fail(0, "Disconnected");
                authResults.Clear();

                foreach (var res in createAccountResults)
                    res.Value.Fail(0, "Disconnected");
                createAccountResults.Clear();

                foreach (var res in addAuthToAccountResults)
                    res.Value.Fail(0, "Disconnected");
                addAuthToAccountResults.Clear();

                foreach (var res in emailActivationResults)
                    res.Value.Fail(0, "Disconnected");
                emailActivationResults.Clear();

                if (OnDisconnected != null)
                    OnDisconnected(x);
            };

            client.OnConnected += x =>
            {
                print("Connected");
                isConnected = true;
                if (OnConnected != null)
                    OnConnected(x);
            };

            client.OnConnectionFailed += (err) =>
            {
                Debug.LogError("Connection failed " + err);
                if (OnConnectionFailed != null)
                    OnConnectionFailed();
            };

            //Handlers of WildCat internal messages

            client.RegisterHandler(WildCatMsgType.LeaveGameResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    finishGameSentResults[x.requestID].Succeed();
                }
                else
                {
                    Action<int, string> OnError;
                    finishGameSentResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                finishGameSentResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.SetMatchPropertyResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    setMatchPropertyResults[x.requestID].Succeed();
                }
                else
                {
                    Action<int, string> OnError;
                    setMatchPropertyResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                setMatchPropertyResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.RejoinMatchResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {

                    matchRejoinResults[x.requestID].Succeed();
                }
                else
                {
                    Action<int, string> OnError;
                    matchRejoinResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                matchRejoinResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetPlayerMatchesResponse, x =>
            {
                var data = x.GetData<OptionalResponse<PendingMatchesCollectionData>>();
                if (data.isSuccesful)
                {

                    getMatchesResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    Action<int, string> OnError;
                    getMatchesResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                getMatchesResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.SetNameResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    nameSetResults[x.requestID].Succeed();
                }
                else
                {
                    nameSetResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                nameSetResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetPlayerChallengesResponse, x =>
            {
                var data = x.GetData<OptionalResponse<ChallengeRequestsMessage>>();
                if (data.isSuccesful)
                {
                    getChallengesResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    getChallengesResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                getChallengesResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetServerTimeResponse, x =>
            {
                var data = x.GetData<OptionalResponse<StringMessage>>();
                if (data.isSuccesful)
                {
                    var dt = DateTime.Parse(data.msg.value);
                    timeReceivedResults[x.requestID].Succeed(dt);
                }
                else
                {
                    timeReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                timeReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetNameResponse, x =>
            {
                var data = x.GetData<OptionalResponse<StringMessage>>();
                if (data.isSuccesful)
                {
                    nameReceivedResults[x.requestID].Succeed(data.msg.value);
                }
                else
                {
                    nameReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                nameReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetInventoryResponse, x =>
            {
                var data = x.GetData<OptionalResponse<GetInventoryResponseMessage>>();
                if (data.isSuccesful)
                {
                    inventoryReceivedResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    inventoryReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                inventoryReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.AddItemToInventoryResponse, x =>
            {
                var data = x.GetData<OptionalResponse<BuyItemInventoryRequestMessage>>();
                if (data.isSuccesful)
                {
                    inventoryAddedResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    inventoryAddedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                inventoryAddedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.RemoveInventoryItemResponse, x =>
            {
                var data = x.GetData<OptionalResponse<RemoveItemInventoryRequestMessage>>();
                if (data.isSuccesful)
                {
                    inventoryRemovedResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    inventoryRemovedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                inventoryRemovedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetPlayerNameResponse, x =>
            {
                var data = x.GetData<OptionalResponse<StringMessage>>();
                if (data.isSuccesful)
                {
                    playerNameReceivedResults[x.requestID].Succeed(data.msg.value);
                }
                else
                {
                    playerNameReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                playerNameReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.SetCloudDataResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    cloudDataSavedResults[x.requestID].Succeed();
                }
                else
                {
                    cloudDataSavedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                cloudDataSavedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.SetPublicCloudDataResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    cloudPublicDataSavedResults[x.requestID].Succeed();
                }
                else
                {
                    cloudPublicDataSavedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                cloudPublicDataSavedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetCloudDataResponse, x =>
            {
                var data = x.GetData<OptionalResponse<StringMessage>>();
                if (data.isSuccesful)
                {
                    cloudDataReceivedResults[x.requestID].Succeed(data.msg.value);
                }
                else
                {
                    cloudDataReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                cloudDataReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetPublicCloudDataResponse, x =>
            {
                var data = x.GetData<OptionalResponse<GetPublicCloudDataResponse>>();
                if (data.isSuccesful)
                {
                    cloudPublicDataReceivedResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    cloudPublicDataReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                cloudPublicDataReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetPlayerPublicCloudDataResponse, x =>
            {
                var data = x.GetData<OptionalResponse<GetPublicCloudDataResponse>>();
                if (data.isSuccesful)
                {
                    cloudPlayerPublicDataReceivedResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    cloudPlayerPublicDataReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                cloudPlayerPublicDataReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.ExecuteScriptEventResponse, x =>
            {
                var data = x.GetData<OptionalResponse<ScriptEventExecutionResponseMessage>>();
                if (data.isSuccesful)
                {
                    scriptEventExecutedResults[x.requestID].Succeed(data.msg.returnValue);
                }
                else
                {
                    scriptEventExecutedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                scriptEventExecutedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetPublisherDataResponse, x =>
            {
                var data = x.GetData<OptionalResponse<StringMessage>>();
                if (data.isSuccesful)
                {
                    print("Publisher data received " + data.msg.value);
                    publisherDataReceivedResults[x.requestID].Succeed(data.msg.value);
                }
                else
                {
                    publisherDataReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                publisherDataReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetLeaderboardResponse, x =>
            {
                var data = x.GetData<OptionalResponse<GetLeaderBoardResponse>>();
                if (data.isSuccesful)
                {
                    if (data.msg.IsPlayerRank)
                    {
                        playerRankReceivedResults[x.requestID].Succeed(data.msg.playerRank);
                    }
                    if (data.msg.IsAroundMeResult)
                    {
                        aroundMeLeaderBoardEntriesReceivedResults[x.requestID].Succeed(data.msg.playerRank, data.msg.entries);
                    }
                    if (data.msg.IsTopResult)
                    {
                        topLeaderBoardEntriesReceivedResults[x.requestID].Succeed(data.msg.count, data.msg.entries);
                    }
                }
                else
                {
                    if (playerRankReceivedResults.ContainsKey(x.requestID))
                        playerRankReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                    else if (topLeaderBoardEntriesReceivedResults.ContainsKey(x.requestID))
                        topLeaderBoardEntriesReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                    else if (aroundMeLeaderBoardEntriesReceivedResults.ContainsKey(x.requestID))
                        aroundMeLeaderBoardEntriesReceivedResults[x.requestID].Fail(data.errorCode, data.errorMsg);

                }
                playerRankReceivedResults.Remove(x.requestID);
                aroundMeLeaderBoardEntriesReceivedResults.Remove(x.requestID);
                topLeaderBoardEntriesReceivedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.SetLeaderboardResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    scoreSetInLeaderBoardResults[x.requestID].Succeed();
                }
                else
                {
                    scoreSetInLeaderBoardResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                scoreSetInLeaderBoardResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.GetFriendsResponse, x =>
            {
                var data = x.GetData<OptionalResponse<FriendlistCollection>>();
                if (data.isSuccesful)
                {
                    getFriendsResults[x.requestID].Succeed(data.msg);
                    if (OnFriendListReceived != null)
                        OnFriendListReceived(data.msg);
                }
                else
                {
                    getFriendsResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                getFriendsResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.InviteFriendResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    inviteFriendsResults[x.requestID].Succeed();
                }
                else
                {
                    inviteFriendsResults[x.requestID].Fail(data.errorCode, data.errorMsg);

                }
                inviteFriendsResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.AcceptFriendResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    acceptFriendsResults[x.requestID].Succeed();
                }
                else
                {
                    acceptFriendsResults[x.requestID].Fail(data.errorCode, data.errorMsg);

                }
                acceptFriendsResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.DenyFriendResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    denyFriendsResults[x.requestID].Succeed();
                }
                else
                {
                    denyFriendsResults[x.requestID].Fail(data.errorCode, data.errorMsg);

                }
                denyFriendsResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.StartChallengeResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    challengeSentResults[x.requestID].Succeed();
                }
                else
                {
                    challengeSentResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                challengeSentResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.AcceptChallengeResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    acceptChallengeSentResults[x.requestID].Succeed();
                }
                else
                {
                    acceptChallengeSentResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                acceptChallengeSentResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.DenyChallengeRequest, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                if (data.isSuccesful)
                {
                    denyChallengeSentResults[x.requestID].Succeed();
                }
                else
                {
                    denyChallengeSentResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                denyChallengeSentResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.FriendshipInvitationEvent, x =>
            {
                var data = x.GetData<OptionalResponse<FriendlistMessage>>();
                print("received friendship invitation from " + data.msg.friend + data.msg.friendName);
                if (OnFriendshipInvitation != null)
                    OnFriendshipInvitation(new FriendData { playerId = data.msg.friend, playerName = data.msg.friendName, data = data.msg.data });
            });

            client.RegisterHandler(WildCatMsgType.FriendshipAcceptedEvent, x =>
            {
                var data = x.GetData<OptionalResponse<FriendlistMessage>>();
                print("Friendship accepted from " + data.msg.friend + data.msg.friendName);
                if (OnFriendshipAccepted != null)
                    OnFriendshipAccepted(new FriendData { playerId = data.msg.friend, playerName = data.msg.friendName, data = data.msg.data });
            });

            client.RegisterHandler(WildCatMsgType.FriendshipDeniedEvent, x =>
            {
                var data = x.GetData<OptionalResponse<FriendlistMessage>>();
                print("Friendship denied from " + data.msg.friend + data.msg.friendName);
                if (OnFriendshipDenied != null)
                    OnFriendshipDenied(new FriendData { playerId = data.msg.friend, playerName = data.msg.friendName, data = data.msg.data });
            });

            client.RegisterHandler(WildCatMsgType.FriendStatusChangedEvent, x =>
            {
                var data = x.GetData<OptionalResponse<FriendlistMessage>>();
                print("Friend status changed " + data.msg.friend + data.msg.status);
                if (OnFriendStatusChanged != null)
                    OnFriendStatusChanged(data.msg.friend, data.msg.status);
            });

            client.RegisterHandler(WildCatMsgType.StartChallengeRequestedEvent, x =>
            {
                var data = x.GetData<OptionalResponse<ChallengeMessage>>();
                print("received challenge request from " + data.isSuccesful + data.msg.Player);
                if (OnchallengeRequestReceived != null)
                    OnchallengeRequestReceived(data.msg.Player, data.msg.PlayerName, data.msg.TimeOut, data.msg.data);
            });

            client.RegisterHandler(WildCatMsgType.ChallengeDeniedEvent, x =>
            {
                var data = x.GetData<OptionalResponse<ChallengeMessage>>().msg;
                if (OnchallengeDenied != null)
                    OnchallengeDenied(data.Player, data.PlayerName, data.data);
            });

            client.RegisterHandler(WildCatMsgType.ChallengeAcceptedEvent, x =>
            {
                var data = x.GetData<OptionalResponse<ChallengeMessage>>().msg;
                if (OnchallengeStarted != null)
                    OnchallengeStarted(data.Player, data.PlayerName, data.GameKey, data.data);
            });

            client.RegisterHandler(WildCatMsgType.MatchPropertyChangedEvent, x =>
            {
                var data = x.GetData<MatchPropertyChangeData>();
                print("match property changed IsGameProp" + data.isGameProperty);
                if (data.isGameProperty)
                {
                    if (OnMatchPropertyChanged != null)
                        OnMatchPropertyChanged(data.Game, data.key, data.value);
                }
                else
                {
                    if (OnPlayerInMatchPropertyChanged != null)
                        OnPlayerInMatchPropertyChanged(data.Game, data.Player, data.key, data.value);
                }
            });

            client.RegisterHandler(WildCatMsgType.GamePlayerRejoinedEvent, x =>
            {
                var data = x.GetData<MatchPlayerEventData>();
                if (OnMatchPlayerRejoined != null)
                    OnMatchPlayerRejoined(data);
            });

            client.RegisterHandler(WildCatMsgType.GamePlayerRemovedEvent, x =>
            {
                var data = x.GetData<MatchPlayerEventData>();
                if (OnMatchPlayerRemoved != null)
                    OnMatchPlayerRemoved(data);
            });

            client.RegisterHandler(WildCatMsgType.MatchRejoinSyncEvent, x =>
            {
                var data = x.GetData<MatchSyncEventData>();
                if (OnMatchRejoinSyncDataReceived != null)
                    OnMatchRejoinSyncDataReceived(data);
            });

            client.RegisterHandler(WildCatMsgType.GameFinishedEvent, x =>
            {
                print("Received GameFinish Event");
                var data = x.GetData<GameFinishedEvent>();
                if (!data.FinishedWithDisconnect)
                {
                    if (OnGameFinished != null)
                        OnGameFinished(data.Game);
                }
                else
                {
                    if (OnGameFinishedDueToDisconnect != null)
                        OnGameFinishedDueToDisconnect(data.Game);
                }
            });

            client.RegisterHandler(WildCatMsgType.InitializeSessionResponse, x =>
            {
                var data = x.GetData<OptionalResponse<GuidMessage>>();
                if (data.isSuccesful)
                {
                    sessionKey = data.msg.value;
                    sessionInitializedResults[x.requestID].Succeed(sessionKey);
                }
                else
                {
                    sessionInitializedResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                sessionInitializedResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.FindMatchResponse, x =>
            {
                var data = x.GetData<OptionalResponse<FindMatchResponse>>();
                print("Received FindMatchResponse success: " + data.isSuccesful);
                if (data.isSuccesful)
                {
                    gameFoundResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    Debug.LogError(data.errorCode + " " + data.errorMsg);
                    if (data.errorCode == 1)
                    {
                        gameFoundResults[x.requestID].Fail(1, "Failed to find game and timedout");
                        Action OnFailedToFindGame;
                        if (failedToFIndGameCallbacks.TryGetValue(x.requestID, out OnFailedToFindGame))
                            OnFailedToFindGame();
                    }
                    else
                    {
                        gameFoundResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                    }
                }
                failedToFIndGameCallbacks.Remove(x.requestID);
                gameFoundResults.Remove(x.requestID);
            });



            client.RegisterHandler(WildCatMsgType.AuthenticateResponse, x =>
            {
                var data = x.GetData<OptionalResponse<AuthenticationResponse>>();
                if (data.isSuccesful)
                {
                    userID = data.msg.userId;
                    userName = data.msg.name;
                    ticket = data.msg.ticket;
                    authResults[x.requestID].Succeed(data.msg);
                }
                else
                {
                    authResults[x.requestID].Fail(data.errorCode, data.errorMsg);

                }
                authResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.CreateNewAccountResponse, x =>
            {
                var data = x.GetData<OptionalResponse<StringMessage>>();
                if (data.isSuccesful)
                {
                    createAccountResults[x.requestID].Succeed(data.msg.value);
                }
                else
                {
                    createAccountResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                createAccountResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.AddAuthMechanismToAccountResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                print("Received add authentication response " + data.isSuccesful);
                if (data.isSuccesful)
                {
                    addAuthToAccountResults[x.requestID].Succeed();
                }
                else
                {
                    addAuthToAccountResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                addAuthToAccountResults.Remove(x.requestID);
            });

            client.RegisterHandler(WildCatMsgType.EmailActivationResponse, x =>
            {
                var data = x.GetData<OptionalResponse<EmptyMessage>>();
                print("Activation response received " + data.isSuccesful);
                if (data.isSuccesful)
                {
                    emailActivationResults[x.requestID].Succeed();
                }
                else
                {
                    emailActivationResults[x.requestID].Fail(data.errorCode, data.errorMsg);
                }
                emailActivationResults.Remove(x.requestID);
            });
        }


        /// <summary>
        /// You should call this to connect to the server
        /// </summary>
        /// <remarks>The result will be asyncronous and OnConnected or OnConnectionFailed will be called based on the result</remarks>
        public void Connect(string url, int port)
        {
            if (!isConnected)
                client.Connect(url, port);
        }

        /// <summary>
        /// This method connects to a local WildCat server and should only be used by WildCat developers or people
        /// with custom WildCat servers on their machines
        /// </summary>
        public void ConnectLocally()
        {
            if (!isConnected)
                client.Connect("ws://localhost:5000/ws", 5000);
        }

        /// <summary>
        /// Causes this client to disconnect from the server.
        /// </summary>
        /// <remarks>Server will log the user of the player off and will disconnect it from any running games as a result of this.</remarks>
        public void Disconnect()
        {
            AssertIsConnected();
            client.Disconnect();
        }

        private void AssertIsConnected()
        {
            if (!isConnected)
                throw new InvalidOperationException("Not connected");
        }

        private void AssertIsLoggedIn()
        {
            if (!isLoggedIn)
                throw new InvalidOperationException("You should be logged in to call this operation");
        }


        /// <summary>
        /// This method should only be used when testing to use a custom device nam
        /// Don't use this in production code
        /// There is no guarantee many production operations work on these accounts
        /// Authenticates a user with his/her device
        /// </summary>
        /// <param name="device">The device ID of the user</param>
        /// <param name="autoCreate">Automatically creates the account if it doesn't exist</param>
        /// <param name="OnSuccess">Will be called if the operation is successful</param>
        /// <param name="OnFailure">Will be called if the operation failed</param>
        /// <returns>Result of the operation which can be yielded on and contains account id in a successful case</returns>
        /// <remarks>Use this only for testing and use AuthenticateWithDevice for production code and actual user login</remarks>
        public Result<AuthenticationResponse> AuthenticateWithTestDevice(string device, bool autoCreate)
        {
            return Authenticate(LoginType.Device, device, "", autoCreate, new Dictionary<string, string>());
        }

        /// <summary>
        /// Authenticates a user with his/her device
        /// </summary>
        /// <param name="autoCreate">Should we automatically create an account if the device has no accounts</param>
        /// <param name="OnSuccess">Will be called if the operation is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>Result of the operation which can be yielded on and contains the user Id</returns>
        public Result<AuthenticationResponse> AuthenticateWithDevice(bool autoCreate)
        {
            return Authenticate(LoginType.Device, SystemInfo.deviceUniqueIdentifier, "", autoCreate, new Dictionary<string, string>());
        }

        /// <summary>
        /// Authenticates a user with a user Name and password pair
        /// </summary>
        /// <param name="email">Id of the user who wants to login</param>
        /// <param name="password">The user's password</param>
        /// <param name="autoCreate">Should we autoCreate the user if it doesn't exist</param>
        /// <param name="OnSuccess">Will be called if the operation is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>Result of the operation which can be yielded on and contains the user's Id</returns>
        /// <remarks>
        /// The password is plain text and is sent to the server over the connection; use wss:// in production.
        /// The server stores only a salted hash of it.
        /// </remarks>
        public Result<AuthenticationResponse> AuthenticateWithUserAndPassword(string userName, string password, bool autoCreate)
        {
            return Authenticate(LoginType.UserWithPassword, userName, password, autoCreate, new Dictionary<string, string>());
        }

        /// <summary>
        /// Authenticates a user with a email and password pair
        /// </summary>
        /// <param name="email">Email of the user</param>
        /// <param name="password">The user's password</param>
        /// <param name="OnSuccess">Will be called if the operation is successful</param>
        /// <param name="OnFailure">Will be called if the operation is failed</param>
        /// <returns>The result of the operation which can be yielded on and contains the user's Id</returns>
        /// <remarks>
        /// The password is plain text and is sent to the server over the connection; use wss:// in production.
        /// The server stores only a salted hash of it.
        /// </remarks>
        public Result<AuthenticationResponse> AuthenticateWithEmail(string email, string password)
        {
            return Authenticate(LoginType.Email, email, password, false, new Dictionary<string, string>());
        }

        private Result<AuthenticationResponse> Authenticate(LoginType loginType, string userIdentifier, string password, bool autoCreate, Dictionary<string, string> data)
        {
            if (!isSessionInitialized)
                throw new InvalidOperationException("Session is not initialized and you can not login");
            uint id = client.SendAuthenticateRequest(loginType, userIdentifier, password, data, autoCreate, sessionKey);
            var result = new Result<AuthenticationResponse>(this);
            authResults[id] = result;
            return result;
        }

        /// <summary>
        /// Adds a user name and password pair for authentication to the loggedin account
        /// </summary>
        /// <param name="userName">The user name you want to choose</param>
        /// <param name="password">Password of the user in plain text; it is sent to the server over the connection, so use wss:// in production</param>
        /// <param name="OnSuccess">Will be called if the operation succeeds</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A yieldable result which shows the success/failure of the operation</returns>
        public Result AddUserNameAndPasswordToAccount(string userName, string password)
        {
            return AddAuthenticationMechanismToAccount(LoginType.UserWithPassword, userName, password, new Dictionary<string, string>());
        }

        /// <summary>
        /// Adds an email and password combination as a new authentication mechanism for the loggedin
        /// account. The email should be activated using the ActivateEmail call and the code
        /// sent to the user's email before you can use it to login
        /// </summary>
        /// <param name="email">The email address you want to add to the account</param>
        /// <param name="password">The password which you want to use for the account, in plain text; it is sent to the server over the connection, so use wss:// in production</param>
        /// <param name="OnSuccess">Will be called if the operation succeeds</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A yieldable result of the operation which shows success/failure</returns>
        public Result AddEmailAndPasswordToAccount(string email, string password)
        {
            return AddAuthenticationMechanismToAccount(LoginType.Email, email, password, new Dictionary<string, string>());
        }

        private Result AddAuthenticationMechanismToAccount(LoginType loginType, string userId, string password, Dictionary<string, string> data)
        {
            AssertIsLoggedIn();
            uint id = client.SendAddAuthToAccountRequest(loginType, userId, password, data, ticket);
            var result = new Result(this);
            addAuthToAccountResults[id] = result;

            return result;
        }

        /// <summary>
        /// Creates a new account using the device's unique identifier
        /// </summary>
        /// <param name="OnSuccess">Will be called if the operation succeeds</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A yieldable result showing success of the operation and contins the user Id in the successful state</returns>
        public Result<string> CreateAccountWithDevice(Action<string> OnSuccess = null, Action<int, string> OnFailure = null)
        {
            return CreateAccount(LoginType.Device, SystemInfo.deviceUniqueIdentifier, "", new Dictionary<string, string>(), OnSuccess, OnFailure);
        }

        /// <summary>
        /// Creates a new account using a user name and password pair
        /// </summary>
        /// <param name="userId">The user name which you want to choose</param>
        /// <param name="password">The password you want to choose, in plain text; it is sent to the server over the connection, so use wss:// in production</param>
        /// <param name="OnSuccess">Will be called if the operation succeeds</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A yieldable result which shows the success of the operation and contains the account's userId (not the name you use but the ID of the account)</returns>
        public Result<string> CreateAccountWithUserAndPassword(string userId, string password, Action<string> OnSuccess = null, Action<int, string> OnFailure = null)
        {
            return CreateAccount(LoginType.UserWithPassword, userName, password, new Dictionary<string, string>(), OnSuccess, OnFailure);
        }

        /// <summary>
        /// Creates a new user account using an email and password.
        /// The email should be activated using ActivateEmail account and the code sent to the user's 
        /// email address before the account can login
        /// </summary>
        /// <param name="email"></param>
        /// <param name="password">The password in plain text; it is sent to the server over the connection, so use wss:// in production</param>
        /// <param name="OnSuccess"></param>
        /// <param name="OnFailure"></param>
        /// <returns></returns>
        public Result<string> CreateAccountWithEmail(string email, string password, Action<string> OnSuccess = null, Action<int, string> OnFailure = null)
        {
            return CreateAccount(LoginType.Email, email, password, new Dictionary<string, string>(), OnSuccess, OnFailure);
        }

        private Result<string> CreateAccount(LoginType loginType, string userId, string password, Dictionary<string, string> data, Action<string> OnSuccess = null, Action<int, string> OnFailure = null)
        {
            if (!isSessionInitialized)
                throw new InvalidOperationException("Session is not initialized and you can not login");
            uint id = client.SendCreateAccountRequest(loginType, userId, password, data, sessionKey);
            var result = new Result<string>(this);
            createAccountResults[id] = result;
            return result;
        }

        /// <summary>
        /// Activates an email address which tried to create an account or add itself to an account using
        /// the code sent to the email
        /// </summary>
        /// <param name="email">The email address which tried to register</param>
        /// <param name="code">The code sent to the email</param>
        /// <param name="OnSuccess">Will be called if the operation is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A yieldable result which shows the success of the operation</returns>
        public Result ActivateEmail(string email, string code)
        {
            if (!isSessionInitialized)
                throw new InvalidOperationException("Session is not initialized and you can not login");

            uint id = client.SendActivationRequest(email, code, sessionKey);
            var result = new Result(this);
            emailActivationResults[id] = result;

            return result;
        }

        /// <summary>
        /// Initializes a session, Should be called before any other function
        /// using a pair of valid keys
        /// </summary>
        /// <param name="developerKey">Your developer key</param>
        /// <param name="gameKey">The title specific game key</param>
        /// <param name="OnSuccess">Called if the operation is successful and then you can start calling other methods of the platform</param>
        /// <param name="OnFailure">Called if the operation is failed due to wrong keys or some other reason</param>
        public Result<Guid> InitializeSession(Guid developerKey, Guid gameKey)
        {
            if (isSessionInitialized)
                throw new InvalidOperationException("Session is already initialized");
            this.developerKey = developerKey;
            this.gameKey = gameKey;
            uint id = client.SendInitializeSessionRequest(developerKey, gameKey);
            var result = new Result<Guid>(this);
            sessionInitializedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Sets the player's name
        /// </summary>
        /// <param name="name">The name to be set</param>
        /// <param name="OnSuccess">This will be called if the operation is successful</param>
        /// <param name="OnFailure">This will be called if the operation fails</param>
        public Result SetName(string name)
        {
            AssertIsLoggedIn();
            uint id = client.SendSetNameRequest(name, ticket);
            var result = new Result(this);
            nameSetResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the name of the user from the server
        /// </summary>
        /// <param name="OnSuccess">Will be called with the user name as parameter if successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A result object containing the promise for receiving the name which you can yield on</returns>
        /// <remarks>If the name is not set, null string will be returned</remarks>
        public Result<string> GetName()
        {
            AssertIsLoggedIn();
            var id = client.SendGetNameRequest(ticket);
            var result = new Result<string>(this);
            nameReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the name of a player which you specify his/her user ID from the server
        /// </summary>
        /// <param name="playerId">User ID of the player to get his/her name</param>
        /// <param name="OnSuccess">Will be called if the operation is successful with the player's name as parameter</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A result object containing the promise for receiving the name which you can yield on</returns>
        /// <remarks>The name will be null if not set</remarks>
        public Result<string> GetPlayerName(string playerId)
        {
            AssertIsLoggedIn();
            var id = client.SendGetPlayerNameRequest(playerId, ticket);
            var result = new Result<string>(this);
            playerNameReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the current UTC time from the server
        /// </summary>
        /// <param name="OnSuccess">Will be called if successful with the current time as parameter</param>
        /// <param name="OnFailure">Will be called if the operation is failed</param>
        public Result<DateTime> GetServerTime()
        {
            AssertIsConnected();
            var id = client.SendGetServerTimeRequest(ticket);
            var result = new Result<DateTime>(this);
            timeReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Saves a string key value pair on the server
        /// </summary>
        /// <param name="key">The key of the pair</param>
        /// <param name="value">The value which should be stored for the key</param>
        /// <param name="OnSuccess">Will be called if the save is successful</param>
        /// <param name="OnFailure">Will be called if a save fails, if the error code is 1 then the failure is due to miss-match between your data and the latest one in the database</param>
        /// <returns>A Result object as a promise which tells you about the success of the operation and you can yield on it</returns>
        /// <remarks>Key has a 100 character size limit</remarks>
        public Result SaveCloudData(string key, string value)
        {
            AssertIsLoggedIn();
            AssertCorrectKeySize(key);
            var id = client.SendSetCloudDataRequest(key, value, ticket);
            var result = new Result(this);
            cloudDataSavedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Saves a string key value pair on the server's public data portion which can be
        /// read by other player clients.
        /// This is good for storing data which you want friends and other players be able to access
        /// </summary>
        /// <param name="key">Key name of the data to be saved</param>
        /// <param name="value">The value to be saved for the key</param>
        /// <param name="OnSuccess">Will be called if the operation is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A Result object as a promise which tells you about the success of the operation and you can yield on it</returns>
        /// <remarks>The key should be at most 100 characters</remarks>
        public Result SavePublicCloudData(string key, string value)
        {
            AssertIsLoggedIn();
            AssertCorrectKeySize(key);
            var id = client.SendSetPublicCloudDataRequest(key, value, ticket);
            var result = new Result(this);
            cloudPublicDataSavedResults[id] = result;
            return result;
        }

        /// <summary>
        /// /Asks the server to run a script event
        /// </summary>
        /// <param name="eventName">Event name on the server</param>
        /// <param name="OnSuccess">The callback for the time that the call succeeds, Based on your return value on the server, you should cast the object result to the appropreate type</param>
        /// <param name="OnFailure">Will be fired if the execution of the script fails due to a compile error, runtime exception/timeout or any other failure reason</param>
        /// <param name="values">The arguments which should be sent to the script</param>
        /// <remarks>
        /// The arguments should be in the same order and with the same type of the arguments defined for the event 
        /// in the developer dashboard and inside the code.
        /// If any of these differ , the code might not run or run with incorrectly serialized data
        /// Also the return value should be casted based on the value you return from the
        /// function on the server
        /// </remarks>
        public Result<object> ExecuteScriptEvent(string eventName, params object[] values)
        {
            AssertIsLoggedIn();
            var id = client.SendExecuteScriptRequest(eventName, ticket, values);
            var result = new Result<object>(this);
            scriptEventExecutedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the string value of a user key stored on the server
        /// </summary>
        /// <param name="key">Name of the key that we want to get its value</param>
        /// <param name="OnSuccess">Will be called if the operation is successful and the argument will be the content of the data</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A result object as a proimse of the return value which can be yielded</returns>
        /// <remarks>if the key doesn't exist the default value will be returned
        /// The key should be less than 100 characters
        /// </remarks>
        public Result<string> GetCloudData(string key)
        {
            AssertIsLoggedIn();
            AssertCorrectKeySize(key);
            uint id = client.SendGetCloudDataRequest(key, ticket);
            var result = new Result<string>(this);
            cloudDataReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the value of a key in your public data
        /// </summary>
        /// <param name="key">The key to return its value</param>
        /// <param name="OnSuccess">Will be called if successful</param>
        /// <param name="OnFailure">Will be called if the operaiton fails</param>
        /// <returns>A result object as a proimse of the return value which can be yielded</returns>
        /// <remarks>
        /// The returning object will contain a hasValue bool which shows if there
        /// was a value for the key on the server and if it is true, then value will contain the actual value.
        /// The key should be at most 100 keys
        /// </remarks>
        public Result<GetPublicCloudDataResponse> GetPublicCloudData(string key)
        {
            AssertIsLoggedIn();
            AssertCorrectKeySize(key);
            uint id = client.SendGetPublicCloudDataRequest(key, ticket);
            var result = new Result<GetPublicCloudDataResponse>(this);
            cloudPublicDataReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the value of a key in a specified player's public data
        /// </summary>
        /// <param name="player">The player to read his/her public data</param>
        /// <param name="key">The key to return its value</param>
        /// <param name="OnSuccess">Will be called if successful</param>
        /// <param name="OnFailure">Will be called if the operaiton fails</param>
        /// <returns>A result object as a proimse of the return value which can be yielded</returns>
        /// <remarks>
        /// The returning object will contain a hasValue bool which shows if there
        /// was a value for the key on the server and if it is true, then value will contain the actual value.
        /// The key should be at most 100 keys
        /// </remarks>
        public Result<GetPublicCloudDataResponse> GetPlayerPublicCloudData(string player, string key)
        {
            AssertIsLoggedIn();
            AssertCorrectKeySize(key);
            uint id = client.SendGetPlayerPublicCloudDataRequest(player, key, ticket);
            var result = new Result<GetPublicCloudDataResponse>(this);
            cloudPlayerPublicDataReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the publisher data stored by the developer APIs
        /// </summary>
        /// <param name="key">Key of the data to be received</param>
        /// <param name="OnSuccess">This will be called with the retrieved value if the call is successful</param>
        /// <param name="OnFailure">This will be called if the call fails</param>
        public Result<string> GetPublisherData(string key)
        {
            AssertIsLoggedIn();
            AssertCorrectKeySize(key);
            uint id = client.SendGetPublisherDataRequest(key, ticket);
            var result = new Result<string>(this);
            publisherDataReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Sets the score of the user in the leaderboard
        /// </summary>
        /// <param name="leaderBoardName">Name of the leaderboard to set the score in</param>
        /// <param name="score">The score to be used</param>
        /// <param name="OnSuccess">Will be called when the operation is successful and score is saved</param>
        /// <param name="OnFailure">Will be called if the operation failed</param>
        /// <remarks>
        /// The server rejects this call from clients (error code 1) because players could post any score.
        /// Set scores from a server script event (WildCat.SetScoreInLeaderBoard) and run it with ExecuteScriptEvent.
        /// </remarks>
        [Obsolete("Not allowed from clients. Grant items and set scores from a server script event.")]
        public Result SetScoreInLeaderBoard(string leaderBoardName, int score)
        {
            AssertIsLoggedIn();
            uint id = client.SendSetLeaderBoardRequest(leaderBoardName, score, ticket);
            var result = new Result(this);
            scoreSetInLeaderBoardResults[id] = result;

            return result;
        }

        /// <summary>
        /// Gets the rank of the user in the specified leaderboard from the server.
        /// </summary>
        /// <param name="leaderBoardName">Name of the leaderboard that you want to get your rank in it</param>
        /// <param name="OnSuccess">Will be called if the operation is successful and the argument will be rank of the user</param>
        /// <param name="OnFailure">Will be called if the operation is not successful</param>
        public Result<int> GetRankInLeaderBoard(string leaderBoardName)
        {
            AssertIsLoggedIn();
            uint id = client.SendGetLeaderBoardRequest(leaderBoardName, GetLeaderBoardRequest.RequestType.Me, 0, 0, ticket);
            var result = new Result<int>(this);
            playerRankReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the elements in the leaderboard aroundthe player
        /// </summary>
        /// <param name="leaderBoardName">Name of the leaderboard to get entries from</param>
        /// <param name="count">Number of elements to retrieve</param>
        /// <param name="OnSuccess">Will be called if the operation is successful, the int argument will be the user's rank and the list is the leaderboard eleements of the returned set of elements</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        public Result<int, List<LeaderBoardEntry>> GetAroundPlayersInLeaderBoard(string leaderBoardName, int count)
        {
            AssertIsLoggedIn();
            uint id = client.SendGetLeaderBoardRequest(leaderBoardName, GetLeaderBoardRequest.RequestType.Around, 0, count, ticket);
            var result = new Result<int, List<LeaderBoardEntry>>(this);
            aroundMeLeaderBoardEntriesReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets top entries in the leaderboard from the server
        /// </summary>
        /// <param name="leaderBoardName">Name of the leaderboard to get entries from</param>
        /// <param name="offset">The offset from the top of the leaderboard, 0 is top</param>
        /// <param name="count">Number of elements to be received</param>
        /// <param name="OnSuccess">Will be called if successful. int argument is number of total elements in the leaderboard and the list argument contains the returned entries</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        public Result<int, List<LeaderBoardEntry>> GetTopPlayersInLeaderBoard(string leaderBoardName, int offset, int count)
        {
            AssertIsLoggedIn();
            uint id = client.SendGetLeaderBoardRequest(leaderBoardName, GetLeaderBoardRequest.RequestType.Top, offset, count, ticket);
            var result = new Result<int, List<LeaderBoardEntry>>(this);
            topLeaderBoardEntriesReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// Requests an opponent from the server for a specific game.
        /// The match maker will try to match players based on their ELO as much as it can.
        /// </summary>
        /// <param name="game">Name of the game which we are looking a match in it, (i.e. game mode/game type)</param>
        /// <param name="skill">Cost of the game to play</param>
        ///<param name="OnSuccess">Will be called if MatchMaker finds a match to play, The argument will be the information of the match, custom data and all player ids in the match</param>
        ///<param name="OnGameNotFound">Will be called if the MatchMaker can not find a game to play before timeout</param>
        /// <param name="OnFailure">Will be called if the operation fails due to an error</param>
        public Result<FindMatchResponse> FindMatch(string game, uint skill)
        {
            AssertIsLoggedIn();
            uint id = client.SendFindMatchRequest(game, skill, ticket);
            var result = new Result<FindMatchResponse>(this);
            gameFoundResults[id] = result;

            return result;
        }

        /// <summary>
        /// Updates a player's value in the game and receives the opponents values in callbacks as well.
        /// </summary>
        /// <param name="game">The game to update player's value in</param>
        /// <param name="value">The updated player value</param>
        /// <remarks>OnGameUpdated is called when the opponent value is received.</remarks>
        public void UpdateGame<T>(string game, T value, WildCatMsgType type, QosType qos) where T : MessageBase, new()
        {
            AssertIsLoggedIn();
            if (IsSupportedQOS(qos))
                client.SendGameMessage<T>(game, value, type, ticket, qos);
            else
                throw new ArgumentException("QOS of type " + qos.ToString() + " is not supported");
        }

        /// <summary>
        /// Finishes playing a game session and leaves it. when all players leave, the game will end and finish event will be sent to all players
        /// </summary>
        /// <param name="game">Id of thegame session</param>
        /// <param name="OnSuccess">Will be called if sending is successful but the game is not finished here, OnGameFinished is used for that when both players are finished or others are disconnected</param>
        /// <param name="OnFailure">Will be called if sending the request fails</param>
        public Result FinishGame(string game)
        {
            AssertIsLoggedIn();
            uint id = client.SendLeaveGameRequest(game, ticket);
            var result = new Result(this);
            finishGameSentResults[id] = result;
            return result;
        }

        /// <summary>
        /// Sets a player property in a match, each player can only set his/her own properties
        /// and you will not receive OnPlayerInMatchPropertyChanged event for your own changes and only
        /// receive it when others change their properties.
        /// These will be sent to you as well as global dat when resyncing after rejoin
        /// </summary>
        /// <param name="game"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="OnSuccess"></param>
        /// <param name="OnFailure"></param>
        /// <returns></returns>
        public Result SetPlayerMatchProperty(Guid game, string key, byte[] value)
        {
            return SetMatchProperty(game, true, key, value);
        }

        public Result SetPlayerMatchProperty(Guid game, string key, MessageBase value, Action OnSuccess = null, Action<int, string> OnFailure = null)
        {
            using (MemoryStream str = new MemoryStream())
            {
                using (BinaryWriter nw = new BinaryWriter(str))
                {
                    value.Serialize(nw);
                }
            }
            return SetPlayerMatchProperty(game, key, value, OnSuccess, OnFailure);
        }

        /// <summary>
        /// Sets a property for the match which is sent to all players and can be read after rejoining
        /// when you get disconnected. It will be sent to yourself as well.in OnMatchPropertyChanged
        /// </summary>
        /// <param name="game">The match to set its property</param>
        /// <param name="key">Key of the property</param>
        /// <param name="value">Up to 10 k value of the property</param>
        /// <param name="OnSuccess">Will be called if sending is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A result which can be yielded on and shows the success of the operation</returns>
        /// <remarks>
        /// There is a possibility of the value being set by someone else just after you set it
        /// but the server applies both changes and sends the events one by one in sequence/order to everyone
        /// the last event received is the current value.
        /// </remarks>
        public Result SetGlobalMatchProperty(Guid game, string key, byte[] value)
        {
            return SetMatchProperty(game, false, key, value);
        }

        public Result SetGlobalMatchProperty(Guid game, string key, MessageBase value, Action OnSuccess = null, Action<int, string> OnFailure = null)
        {
            byte[] data;
            using (MemoryStream str = new MemoryStream())
            {
                using (BinaryWriter nw = new BinaryWriter(str))
                {
                    value.Serialize(nw);
                }
                data = str.ToArray();
            }

            return SetGlobalMatchProperty(game, key, data);
        }

        /// <summary>
        /// Sets a player property in a match, each player can only set his/her own properties
        /// and you will not receive OnPlayerInMatchPropertyChanged event for your own changes and only
        /// receive it when others change their properties.
        /// These will be sent to you as well as global dat when resyncing after rejoin
        /// </summary>
        /// <param name="game"></param>
        /// <param name="isPlayerProperty"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="OnSuccess"></param>
        /// <param name="OnFailure"></param>
        /// <returns></returns>
        private Result SetMatchProperty(Guid game, bool isPlayerProperty, string key, byte[] value)
        {
            AssertIsLoggedIn();
            AssertValueIsLessThan10K(value);
            uint id = client.SendSetMatchPropertyRequest(game, isPlayerProperty, key, value, ticket);
            var result = new Result(this);
            setMatchPropertyResults[id] = result;
            return result;
        }

        private static void AssertValueIsLessThan10K(byte[] value)
        {
            if (value.Length > 1024 * 10)
                throw new ArgumentException();
        }

        /// <summary>
        /// Rejoins a match which is returned in the list of GetMatches method, You only need to send
        ///the match's id to the server to rejoin and receive OnMatchRejoinSyncDataReceived and
        ///read all global and player properties and continue playing
        /// </summary>
        /// <param name="game"></param>
        /// <param name="OnSuccess"></param>
        /// <param name="OnFailure"></param>
        /// <returns></returns>
        public Result RejoinMatch(Guid game)
        {
            AssertIsLoggedIn();
            uint id = client.SendRejoinMatchRequest(game, ticket);
            var result = new Result(this);
            matchRejoinResults[id] = result;
            return result;
        }

        /// <summary>
        /// Gets the player's pending matches which were being played before he/she disconnects and
        /// are one of those kinds which don't finish until everybody calls finish onthem
        /// </summary>
        /// <param name="OnSuccess"></param>
        /// <param name="OnFailure"></param>
        /// <returns></returns>
        public Result<PendingMatchesCollectionData> GetPendingMatches()
        {
            AssertIsLoggedIn();
            uint id = client.SendGetPendingMatchesRequest(ticket);
            var result = new Result<PendingMatchesCollectionData>(this);
            getMatchesResults[id] = result;
            return result;
        }

        /// <summary>
        /// Request to receive the list of friends that the player has and also all sent and received invitations
        /// You can send a set of keys for friend public datas which will be read and sent back
        /// </summary>
        /// <param name="keys">The public user data keys of friends which should be read</param>
        /// <param name="OnSuccess">Will be called if the operation is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        /// <returns>A yieldable result object which contains the friend list and invitations</returns>
        /// <remarks>
        /// Since there might be general interst for knowing about this, an event called OnFriendlistReceived is fired when the resposne comes back as well
        /// 
        /// It is good to ask for every public data key you need in this request to do what you want faster however if the friend list is huge
        /// sending a very big set of keys might not get a response back
        /// </remarks>
        public Result<FriendlistCollection> GetFriends(string[] keys)
        {
            AssertIsLoggedIn();
            uint id = client.SendGetFriendsRequest(keys, ticket);
            var result = new Result<FriendlistCollection>(this);
            getFriendsResults[id] = result;
            return result;
        }

        /// <summary>
        /// Invite a player to become friends using their user name
        /// </summary>
        /// <param name="userName">Name of the user to invite</param>
        /// <param name="data">The additional data to send</param>
        /// <param name="OnSuccess">This is fired when the operation is successful</param>
        /// <param name="OnFailure">This is fired when the operation is failed</param>
        /// <returns>A yieldable result which tells you if sending the request is successful or not</returns>
        public Result InviteFriendByUserName(string userName, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendInviteFriendRequest(InvitationType.UserId, userName, data, ticket);
            var result = new Result(this);
            inviteFriendsResults[id] = result;
            return result;
        }

        /// <summary>
        /// Invite a player to become friends by email
        /// </summary>
        /// <param name="email">Email of the user to invite</param>
        /// <param name="data">The additional arbitrary data to send</param>
        /// <param name="OnSuccess">This is fired when the operation is successful</param>
        /// <param name="OnFailure">This is fired when the operation is failed</param>
        /// <returns>A yieldable result which tells you if sending the invitation was successful or not</returns>
        public Result InviteFriendByEmail(string email, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendInviteFriendRequest(InvitationType.Email, email, data, ticket);
            var result = new Result(this);
            inviteFriendsResults[id] = result;
            return result;
        }

        /// <summary>
        /// Invites a player to become friend using the player's Id which we refer to it as player Id/Account Id/user Id
        /// </summary>
        /// <param name="playerId">Id ofthe player to invite</param>
        /// <param name="data">Arbitrary data to send</param>
        /// <param name="OnSuccess">This is fired when the operation is successful</param>
        /// <param name="OnFailure">This is fired when the operation is failed</param>
        /// <returns>A yieldable result which tells you if sending the invitation was successful or not</returns>
        public Result InviteFriendByPlayerId(string playerId, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendInviteFriendRequest(InvitationType.AccountId, playerId, data, ticket);
            var result = new Result(this);
            inviteFriendsResults[id] = result;
            return result;
        }

        /// <summary>
        /// Accepts a friendship request
        /// </summary>
        /// <param name="playerId">Id of the player who sent the request</param>
        /// <param name="data">Arbitrary data to send</param>
        /// <param name="OnSuccess">This is fired when the operation is successful</param>
        /// <param name="OnFailure">This is fired when the operation is failed</param>
        /// <returns>A yieldable result which tells you if the operation was successful or not</returns>
        public Result AcceptFriendInvitation(string playerId, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendAcceptFriendRequest(playerId, data, ticket);
            var result = new Result(this);
            acceptFriendsResults[id] = result;
            return result;
        }

        /// <summary>
        /// Denies a friendship request
        /// </summary>
        /// <param name="playerId">Id of the player who sent the request</param>
        /// <param name="data">Arbitrary data to send</param>
        /// <param name="OnSuccess">This is fired when the operation is successful</param>
        /// <param name="OnFailure">This is fired when the operation is failed</param>
        /// <returns>A yieldable result which tells you if the operation was successful or not</returns>
        public Result DenyFriendInvitation(string playerId, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendDenyFriendRequest(playerId, data, ticket);
            var result = new Result(this);
            denyFriendsResults[id] = result;
            return result;
        }

        /// <summary>
        /// Sends a challenge request to another user
        /// </summary>
        /// <param name="player">ID of the player to send the challenge to</param>
        /// <param name="timeout">The time that the challenge will timeout</param>
        /// <param name="data">Custom data to be sent</param>
        /// <param name="OnSuccess">Will be called if the send is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        public Result SendChallengeRequest(string player, uint timeout, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendChallengeRequest(player, timeout, data, ticket);
            var result = new Result(this);
            challengeSentResults[id] = result;
            return result;
        }

        /// <summary>
        /// Accepts a received challenge
        /// </summary>
        /// <param name="player">The player to accept his/her challenge</param>
        /// <param name="data">The custom data to be sent with the acceptance</param>
        /// <param name="OnSuccess">Will be called if sending is successful</param>
        /// <param name="OnFailure">Will be called if sending fails</param>
        public Result AcceptChallengeRequest(string player, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendAcceptChallengeRequest(player, data, ticket);
            var result = new Result(this);
            acceptChallengeSentResults[id] = result;
            return result;
        }

        /// <summary>
        /// Denies challenge of a player
        /// </summary>
        /// <param name="player">The player to deny his/her challenge</param>
        /// <param name="data">Custom data to be sent with the denial</param>
        /// <param name="OnSuccess">Will be called if sending is successful</param>
        /// <param name="OnFailure">Will be called if the operation fails</param>
        public Result DenyChallengeRequest(string player, byte[] data)
        {
            AssertIsLoggedIn();
            uint id = client.SendDenyChallengeRequest(player, data, ticket);
            var result = new Result(this);
            denyChallengeSentResults[id] = result;
            return result;
        }

        #region  DevAPI

        public Result<ChallengeRequestsMessage> GetChallenges()
        {
            AssertIsLoggedIn();
            uint id = client.SendGetChallengesRequest(ticket);
            var result = new Result<ChallengeRequestsMessage>(this);
            getChallengesResults[id] = result;
            return result;
        }

        public Result<GetInventoryResponseMessage> GetInventory()
        {
            AssertIsLoggedIn();
            var id = client.SendGetInventoryRequest(ticket);
            var result = new Result<GetInventoryResponseMessage>(this);
            inventoryReceivedResults[id] = result;
            return result;
        }

        /// <summary>
        /// The server rejects this call from clients (error code 1) because players could grant themselves anything.
        /// Grant items from a server script event (WildCat.AddInventoryItems) and run it with ExecuteScriptEvent.
        /// </summary>
        [Obsolete("Not allowed from clients. Grant items and set scores from a server script event.")]
        public Result<BuyItemInventoryRequestMessage> AddInventoryItems((string, int)[] items)
        {
            AssertIsLoggedIn();
            var id = client.SendAddInventoryItemRequest(new BuyItemInventoryRequestMessage { items = items }, ticket);
            var result = new Result<BuyItemInventoryRequestMessage>(this);
            inventoryAddedResults[id] = result;
            return result;
        }

        public Result<RemoveItemInventoryRequestMessage> RemoveInventoryItems((string, int)[] items)
        {
            AssertIsLoggedIn();
            var id = client.SendRemoveInventoryItemRequest(new RemoveItemInventoryRequestMessage { items = items }, ticket);
            var result = new Result<RemoveItemInventoryRequestMessage>(this);
            inventoryRemovedResults[id] = result;
            return result;
        }

        #endregion

        public void RegisterHandler(WildCatMsgType type, Action<WildCatMessageBase> action)
        {
            client.RegisterHandler(type, action);
        }

        public void UnregisterHandler(WildCatMsgType type, Action<WildCatMessageBase> action)
        {
            client.UnregisterHandler(type, action);
        }


        private static bool IsSupportedQOS(QosType qos)
        {
            return qos == QosType.Reliable || qos == QosType.ReliableFragmented || qos == QosType.Unreliable;
        }

        private static void AssertCorrectKeySize(string key)
        {
            if (key.Length > 100)
                throw new ArgumentException("Key should be less than 100 characters");
        }

    }
}