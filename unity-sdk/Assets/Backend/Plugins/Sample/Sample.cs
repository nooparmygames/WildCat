using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using WildCat.Protocol;
using UnityEngine.Networking;
using WildCat;
using System.Linq;

public class Sample : MonoBehaviour
{
    /// <summary>
    /// Used for keeping track of the application state
    /// </summary>
    public enum ApplicationState
    {
        Initial,//Before logging in and initializing
        Connected,//connected to server
        loggedIn,//logged in
        InGame,//inside a game and playing
        ShowMessage,//showing a message (will be returned to another state)
        LeaderBoard,
        ChallengeRequestReceived,//received a challenge request
        FriendInvitationReceived
    }

    /// <summary>
    /// current state of the app
    /// </summary>
    private ApplicationState state;

    /// <summary>
    /// The message that will be shown in ShowMessage state
    /// </summary>
    private string MessageToShow = "";

    /// <summary>
    /// The ID of the player which is challenging you
    /// </summary>
    private string ChallengingPlayer = "";

    /// <summary>
    /// The data for the friend which we received inviation from
    /// </summary>
    private BackendPlatform.FriendData invitationFriendData;

    /// <summary>
    /// The friendList we have
    /// </summary>
    private FriendlistCollection friendList;

    /// <summary>
    /// Custom uNet messages for the realtime game
    /// </summary>
    public enum GameMessageType
    {
        GameStart = WildCatMsgType.HighestInternal + 1,
        Update,
        GameFinish
    }

    /// <summary>
    /// A custom message to send vector3 on the network
    /// </summary>
    public class Vector3Message : MessageBase
    {
        public Vector3 msg;
        
        public override void Serialize(BinaryWriter writer)
        {
            var xByte = BitConverter.GetBytes(msg[0]);
            var yByte = BitConverter.GetBytes(msg[1]);
            var zByte = BitConverter.GetBytes(msg[2]);
            var data = new Byte[12];
            xByte.CopyTo(data, 0);
            yByte.CopyTo(data, 4);
            zByte.CopyTo(data, 8);
            writer.WriteBytesFull(data);
        }

        public override void Deserialize(BinaryReader reader)
        {
            var data = reader.ReadBytesAndSize();
            msg.x = BitConverter.ToSingle(data, 0);
            msg.y = BitConverter.ToSingle(data, 4);
            msg.z = BitConverter.ToSingle(data, 8);
        }
    }

    /// <summary>
    /// The prefab that we will instantiate for the other player
    /// </summary>
    public GameObject prefab;

    /// <summary>
    /// The amount of time that each player simulates the other player later
    /// If this is say 0.1 seconds, it means every move of each player will be seen by others 0.1 seconds after receiving it
    /// </summary>
    public float interpolationBackTime = 0.2f;

    /// <summary>
    /// developer key of your account should be here
    /// </summary>
    [Tooltip("Put your account's developer key here")]
    public string developerKey;

    /// <summary>
    /// The game key of the game to be used
    /// </summary>
    [Tooltip("Put your game's key here")]
    public string gameKey;

    private Guid developer;
    private Guid game;

    public string device;
    public string userName = "";
    public string boardName = "global";
    [HideInInspector]
    public int score;
    private List<LeaderBoardEntry> entriesToShow;

    /// <summary>
    /// If true, will try to connect to a local WildCat server
    /// </summary>
    public bool isLocal;

    /// <summary>
    /// The current running match's ID
    /// </summary>
    private Guid currentMatch;

    /// <summary>
    /// Other player's gameObject
    /// </summary>
    private GameObject other;

    /// <summary>
    /// This is the buffer to store other player's movements
    /// </summary>
    private TimestampedBuffer<Vector3> movementBuffer;

    void Awake()
    {
        state = ApplicationState.Initial;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width / 2 - 150, 10, 300, 400));
        switch (state)
        {
            case ApplicationState.Initial:
                ShowInitialGUI();
                break;
            case ApplicationState.InGame:
                if (GUILayout.Button("Finish Game"))
                {
                    StartCoroutine(FinishGameRoutine());
                }
                if (GUILayout.Button("set match data"))
                {
                    StartCoroutine(SetMatchRoutine());
                }
                break;
            case ApplicationState.loggedIn:
                ShowMainMenu();
                break;
            case ApplicationState.LeaderBoard:
                ShowLeaderBoardUI();
                break;

            case ApplicationState.ShowMessage:
                GUILayout.Label(MessageToShow);
                break;
            case ApplicationState.ChallengeRequestReceived:
                ShowChallengeAcceptanceGUI();
                break;
            case ApplicationState.FriendInvitationReceived:
                ShowFriendAcceptanceGUI();
                break;
        }
        GUILayout.EndArea();
        GUILayout.BeginArea(new Rect(Screen.width - 320, 10, 300, 400));
        if (friendList != null)
        {
            GUILayout.BeginVertical();
            foreach (var friend in friendList.friends)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(friend.name + " " + friend.userId + " " + friend.status);
                if (GUILayout.Button("Challenge"))
                {
                    BackendPlatform.Instance.SendChallengeRequest(friend.userId, 10, new byte[0]);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndArea();
    }

    IEnumerator ShowMessageAndChangeState(string message, int time, ApplicationState targetState)
    {
        state = ApplicationState.ShowMessage;
        this.MessageToShow = message;
        yield return new WaitForSeconds(time);
        state = targetState;
    }

    private void ShowChallengeAcceptanceGUI()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Challenge Request from " + ChallengingPlayer);
        if (GUILayout.Button("Accept"))
        {
            BackendPlatform.Instance.AcceptChallengeRequest(ChallengingPlayer, new byte[0]);
        }
        if (GUILayout.Button("Deny"))
        {
            BackendPlatform.Instance.DenyChallengeRequest(ChallengingPlayer, new byte[0]);
        }
        GUILayout.EndHorizontal();
    }

    private void ShowFriendAcceptanceGUI()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Invitation from " + invitationFriendData.playerName + " " + invitationFriendData.playerId);
        if (GUILayout.Button("Accept"))
        {
            BackendPlatform.Instance.AcceptFriendInvitation(invitationFriendData.playerId, new byte[0]);
            state = ApplicationState.loggedIn;
        }
        if (GUILayout.Button("Deny"))
        {
            BackendPlatform.Instance.DenyFriendInvitation(invitationFriendData.playerId, new byte[0]);
            state = ApplicationState.loggedIn;
        }
        GUILayout.EndHorizontal();
    }
    
    public IEnumerator PrintPlayerName()
    {
        var response = BackendPlatform.Instance.GetName();
        yield return response.Wait();
        if (response.isSuccessful)
        {
            print("The playerName is: " + response.value);
        }
        else
        {
            Debug.LogError($"GetName failed > code: {response.errorCode} message: {response.errorMessage}");
        }
    }

    public IEnumerator FindGameRoutine()
    {
        var response = BackendPlatform.Instance.FindMatch("nodc", 150);
        yield return response.Wait();
        if (response.isSuccessful)
        {
            currentMatch = response.value.gameId;
            state = ApplicationState.InGame;
            StartCoroutine(UpdateGame(BackendPlatform.Instance, currentMatch.ToString()));
        }
        else
        {
            Debug.LogError($"Find Game failed > code: {response.errorCode} message: {response.errorMessage}");
        }
    }

    public IEnumerator SetMatchRoutine()
    {
        print("Set match data called");
        var response = BackendPlatform.Instance.SetGlobalMatchProperty(currentMatch, "k1", new IntegerMessage { value = 3 });
        yield return response.Wait();
        if (response.isSuccessful)
        {
            print("**SENT");
        }
        else
        {
            Debug.LogError($"Set Match failed > code: {response.errorCode} message: {response.errorMessage}");
        }
    }
    
    public IEnumerator FinishGameRoutine()
    {
        var response = BackendPlatform.Instance.FinishGame(currentMatch.ToString());
        yield return response.Wait();
        if (response.isSuccessful)
        {
            Debug.Log("Game Finished");
        }
        else
        {
            Debug.LogError($"Finish Game failed > code: {response.errorCode} message: {response.errorMessage}");
        }
    }


    private void ShowMainMenu()
    {
        GUILayout.BeginVertical();
        GUILayout.Label("Other player");
        device = GUILayout.TextField(device);
        if (GUILayout.Button("Challenge"))
        {
            BackendPlatform.Instance.SendChallengeRequest(device, 15, new byte[] { });
        }
        if (GUILayout.Button("Find Match"))
        {
            StartCoroutine(FindGameRoutine());
            /*, matchInfo =>
             {
                 currentMatch = matchInfo.gameId;
                 state = ApplicationState.InGame;
                 StartCoroutine(UpdateGame(BackendGamePlatform.Instance, currentMatch.ToString()));
             }, () => print("Match not found"), (errCode, errMsg) => Debug.LogError("Find Match error" + errCode + errMsg)*/
        }
        if (GUILayout.Button("Get and join old match"))
        {
            BackendPlatform.Instance.GetPendingMatches();
        }
        if (GUILayout.Button("Invite friend"))
        {
            BackendPlatform.Instance.InviteFriendByUserName(device, new byte[0]);
        }
        if (GUILayout.Button("Get Server Time"))
        {
            BackendPlatform.Instance.GetServerTime();
        }

        if (GUILayout.Button("Show LeaderBoard"))
        {
            state = ApplicationState.LeaderBoard;
        }
        userName = GUILayout.TextField(userName);
        if (GUILayout.Button("SetName"))
        {
            BackendPlatform.Instance.
                SetName(userName);
        }
        if (GUILayout.Button("GetName"))
        {
            StartCoroutine(PrintPlayerName());
        }


        if (GUILayout.Button("Get player Name"))
        {
            BackendPlatform.Instance.
                GetPlayerName(userName);
        }
        if (GUILayout.Button("set public data"))
        {
            BackendPlatform.Instance.SavePublicCloudData("test", "test value");
        }
        if (GUILayout.Button("get player public data"))
        {
            BackendPlatform.Instance.GetPlayerPublicCloudData(userName, "test");
        }
        GUILayout.EndVertical();
    }

    private void ShowLeaderBoardUI()
    {
        GUILayout.BeginVertical();
        if (GUILayout.Button("back"))
        {
            state = ApplicationState.loggedIn;
        }
        score = int.Parse(GUILayout.TextField(score.ToString()));
        boardName = GUILayout.TextField(boardName);
        if (GUILayout.Button("Send score"))
        {
            BackendPlatform.Instance.
                SetScoreInLeaderBoard(boardName, score);
        }
        if (GUILayout.Button("ShowTop"))
        {
            BackendPlatform.Instance.GetTopPlayersInLeaderBoard(boardName, 0, 5);
        }
        if (entriesToShow != null && entriesToShow.Count > 0)
        {
            GUILayout.Label("board");
            for (int i = 0; i < entriesToShow.Count; ++i)
                GUILayout.Label(string.Format("{0} {1} {2}"
                    , i + 1, entriesToShow[i].userName, entriesToShow[i].score.ToString()));
        }
        GUILayout.EndVertical();
    }

    private void ShowInitialGUI()
    {
        GUILayout.BeginVertical();
        device = GUILayout.TextField(device);
        if (GUILayout.Button("Start"))
        {
            ConnectAndLogin();
        }
        GUILayout.EndVertical();
    }

    public IEnumerator InitWildCatForServer()
    {
        //we need to parse game and developer keys to Guid to be able to send them
        developer = new Guid(developerKey);
        game = new Guid(gameKey);
        if (!isLocal)
            BackendPlatform.Instance.Connect("ws://localhost:5000/ws", 5000);
        else
            BackendPlatform.Instance.ConnectLocally();
        while (!BackendPlatform.Instance.isConnected)
            yield return null;
        var sessionResult = BackendPlatform.Instance.InitializeSession(developer, game);

        yield return sessionResult.Wait();
        if (sessionResult.isSuccessful)
            print("success");
    }

    void ConnectAndLogin()
    {
        //we need to parse game and developer keys to Guid to be able to send them
        developer = new Guid(developerKey);
        game = new Guid(gameKey);

        RegisterCallbacks();

        if (!isLocal)
            BackendPlatform.Instance.Connect("ws://localhost:5000/ws", 5000);
        else
            BackendPlatform.Instance.ConnectLocally();
    }

    /// <summary>
    /// Registers all global callbacks
    /// </summary>
    private void RegisterCallbacks()
    {
        BackendPlatform.Instance.OnFriendshipInvitation += (friendData) =>
        {
            state = ApplicationState.FriendInvitationReceived;
            this.invitationFriendData = friendData;
        };
        BackendPlatform.Instance.OnMatchPropertyChanged += (gm, key, val) =>
        {
            using (System.IO.MemoryStream str = new System.IO.MemoryStream(val))
            {
                using (System.IO.BinaryReader nr = new System.IO.BinaryReader(str))
                {
                    IntegerMessage intmsg = new IntegerMessage();
                    intmsg.Deserialize(nr);
                    Debug.Log("received game prop " + intmsg.value.ToString());
                }
            }
        };

        BackendPlatform.Instance.OnFriendshipAccepted += friend => StartCoroutine(UpdateFriends());
        BackendPlatform.Instance.OnFriendshipDenied += friendData => Debug.LogError("Denied our friendship");
        BackendPlatform.Instance.OnFriendStatusChanged += (player, status) =>
        {
            var fr = friendList.friends.FirstOrDefault(x => x.userId == player);
            if (fr != null)
                fr.status = status;
        };

        //In event of receiving a challenge we will show the proper UI
        BackendPlatform.Instance.OnchallengeRequestReceived += (player, playerName, timeOut, data) =>
        {
            state = ApplicationState.ChallengeRequestReceived;
            ChallengingPlayer = player;
        };

        //When receiving a challenge start event,
        BackendPlatform.Instance.OnchallengeStarted += (player, playerName, gameGuid, data) =>
        {
            //save the match's ID to be used for sending game messages
            currentMatch = gameGuid;
            state = ApplicationState.InGame;
            StartCoroutine(UpdateGame(BackendPlatform.Instance, currentMatch.ToString()));
        };

        BackendPlatform.Instance.OnchallengeDenied += (player, playerName, data) =>
        {
            Debug.LogError("Challenge denied by " + player);
            state = ApplicationState.loggedIn;
        };

        //when a game is finished, destroy the other player's object and go back to 
        //login state
        BackendPlatform.Instance.OnGameFinished += gameName =>
        {
            state = ApplicationState.loggedIn;
            if (other != null)
            {
                Destroy(other.gameObject);
                other = null;
            }
        };

        //game is finished because opponent is disconnected
        BackendPlatform.Instance.OnGameFinishedDueToDisconnect += gameName =>
        {
            state = ApplicationState.loggedIn;
            if (other != null)
            {
                Destroy(other.gameObject);
                other = null;
            }
        };

        //uNet like message registration for player update messages in game
        BackendPlatform.Instance.RegisterHandler((WildCatMsgType)GameMessageType.Update, x =>
        {
            UpdateOtherPlayer(x);
        });

        //called when a player gets connected
        BackendPlatform.Instance.OnConnected += x =>
        {
            StartCoroutine(StartConnectionSequence(x));
        };

        //called if a player is disconnected
        BackendPlatform.Instance.OnDisconnected += x =>
        {
            print("disconnected " + x.ToString());
        };
    }

    /// <summary>
    /// Updates our list of friends
    /// </summary>
    /// <returns></returns>
    private IEnumerator UpdateFriends()
    {
        var res = BackendPlatform.Instance.GetFriends(new string[0]);
        yield return res.Wait();
        if (res.isSuccessful)
            friendList = res.value;
        else
            Debug.Log(res.errorMessage);
    }

    private IEnumerator StartConnectionSequence(int x)
    {
        state = ApplicationState.Connected;
        print("connected " + x.ToString());
        var sessionResult = BackendPlatform.Instance.InitializeSession(developer, game);

        yield return sessionResult.Wait();
        if (sessionResult.isSuccessful)
        {
            print("session initialized");

            // var loginResult = BackendPlatform.Instance.AuthenticateWithDevice(true);
            var loginResult = BackendPlatform.Instance.AuthenticateWithTestDevice(device, true);
            yield return loginResult.Wait();
            if (loginResult.isSuccessful)
            {
                state = ApplicationState.loggedIn;
                print("LoggedIn " + loginResult.value.userId);
                //We ask for list of our friends, the UI will react on its own in the main OnGUI function if the friendList variable is assigned
                StartCoroutine(UpdateFriends());
            }
            else
            {
                Debug.LogError("Could not login " + loginResult.errorCode + loginResult.errorMessage);
            }
        }
        else
        {
            Debug.LogError(sessionResult.errorCode + " " + sessionResult.errorMessage);
        }
    }

    private void UpdateOtherPlayer(WildCatMessageBase x)
    {
        var data = x.GetData<Vector3Message>();
        if (other == null)
        {
            other = Instantiate(prefab) as GameObject;
            other.GetComponent<Renderer>().material.color = Color.blue;
            //We create a buffer for the player's movement data and set its interpolation function
            movementBuffer = new TimestampedBuffer<Vector3>((v1, v2, t) => Vector3.Lerp(v1, v2, (float)t));
        }
        //Add the received movement to the buffer
        movementBuffer.Add(Time.time, data.msg);
    }

    void Update()
    {
        transform.position += new Vector3(Input.GetAxis("Horizontal") * Time.deltaTime, 0, Input.GetAxis("Vertical") * Time.deltaTime);
        //If the opponent exist so let's move it if possible
        if (other != null)
        {
            //Now we check if at current time we have a value for the opponent to move toward
            //If the value exist then we set the position,
            //keep in mind that the value is interpolated when being returned from the buffer
            //so no additional processing is needed
            bool hasValue = false;
            var pos = movementBuffer.GetValue(Time.time - interpolationBackTime, out hasValue);
            if (hasValue)
                other.transform.position = pos;
        }
    }

    /// <summary>
    /// Sends player position to the server 10 times a second
    /// </summary>
    /// <param name="platform"></param>
    /// <param name="game"></param>
    /// <returns></returns>
    IEnumerator UpdateGame(BackendPlatform platform, string game)
    {

        while (state == ApplicationState.InGame)
        {
            platform.UpdateGame<Vector3Message>(game, new Vector3Message { msg = transform.position }, (WildCatMsgType)GameMessageType.Update, QosType.Reliable);
            yield return new WaitForSeconds(0.05f); //20 times per second
        }
    }

}
