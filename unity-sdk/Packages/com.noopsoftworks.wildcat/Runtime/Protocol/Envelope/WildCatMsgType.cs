namespace WildCat.Protocol
{
    /// <summary>
    /// The message types. Every value is pinned: the ids of removed messages are gaps that must not
    /// be reused, and a new message takes the next free id after the highest one. Ids above
    /// <see cref="HighestInternal"/> belong to games and are relayed between the players of a match.
    /// </summary>
    public enum WildCatMsgType : short
    {
        InitializeSessionRequest = 6,
        InitializeSessionResponse = 7,

        ErrorMsg = 8,

        GamePlayerAddedEvent = 9,
        GameFinishedEvent = 10,
        LeaveGameRequest = 13,
        LeaveGameResponse = 14,

        FindMatchRequest = 15,
        FindMatchResponse = 16,

        StartChallengeRequest = 17,
        StartChallengeResponse = 18,
        StartChallengeRequestedEvent = 19,
        AcceptChallengeRequest = 20,
        AcceptChallengeResponse = 21,
        ChallengeAcceptedEvent = 22,
        DenyChallengeRequest = 23,
        DenyChallengeResponse = 24,
        ChallengeDeniedEvent = 25,

        GetLeaderboardRequest = 26,
        GetLeaderboardResponse = 27,
        SetLeaderboardRequest = 28,
        SetLeaderboardResponse = 29,

        GetCloudDataRequest = 30,
        GetCloudDataResponse = 31,
        SetCloudDataRequest = 32,
        SetCloudDataResponse = 33,

        GetPublisherDataRequest = 34,
        GetPublisherDataResponse = 35,

        GetNameRequest = 36,
        GetNameResponse = 37,
        SetNameRequest = 38,
        SetNameResponse = 39,

        GetServerTimeRequest = 42,
        GetServerTimeResponse = 43,

        // Developer (server to server) messages; their payloads are in ServerOnly/, which the
        // Unity SDK copy leaves out.
        CreateEnvironmentRequest = 44,
        CreateEnvironmentResponse = 45,

        SetPublisherDataRequest_S = 62,
        SetPublisherDataResponse_S = 63,
        GetPublisherDataRequest_S = 64,
        GetPublisherDataResponse_S = 65,

        ExecuteScriptEventRequest = 66,
        ExecuteScriptEventResponse = 67,
        GetPlayerNameRequest = 68,
        GetPlayerNameResponse = 69,
        GetPlayerPublicCloudDataRequest = 70,
        GetPlayerPublicCloudDataResponse = 71,
        SetPublicCloudDataRequest = 72,
        SetPublicCloudDataResponse = 73,
        GetPublicCloudDataRequest = 74,
        GetPublicCloudDataResponse = 75,
        AuthenticateRequest = 76,
        AuthenticateResponse = 77,
        CreateNewAccountRequest = 78,
        CreateNewAccountResponse = 79,
        EmailActivationRequest = 80,
        EmailActivationResponse = 81,
        AddAuthMechanismToAccountRequest = 82,
        AddAuthMechanismToAccountResponse = 83,
        RecoverPasswordRequest = 84,
        RecoverPasswordResponse = 85,
        ResendActivationRequest = 86,
        ResendActivationResponse = 87,

        GetFriendsRequest = 88,
        GetFriendsResponse = 89,
        InviteFriendRequest = 90,
        InviteFriendResponse = 91,
        FriendshipInvitationEvent = 92,
        AcceptFriendRequest = 93,
        AcceptFriendResponse = 94,
        FriendshipAcceptedEvent = 95,
        DenyFriendRequest = 96,
        DenyFriendResponse = 97,
        FriendshipDeniedEvent = 98,
        FriendStatusChangedEvent = 99,
        HeartBeatRequest = 100,
        HeartBeatResponse = 101,

        GetDefinedItemsRequest = 107,
        GetDefinedItemsResponse = 108,
        GetInventoryRequest = 109,
        GetInventoryResponse = 110,
        AddItemToInventoryRequest = 111,
        AddItemToInventoryResponse = 112,
        SetItemToInventoryRequest = 113,
        SetItemToInventoryResponse = 114,
        RemoveInventoryItemRequest = 115,
        RemoveInventoryItemResponse = 116,
        BuyItemRequest = 117,
        BuyItemResponse = 118,
        UnlockInventoryRequest = 119,
        UnlockInventoryResponse = 120,
        GetInventoryUnlockTimeRequest = 121,
        GetInventoryUnlockTimeResponse = 122,
        ConsumeItemRequest = 123,
        ConsumeItemResponse = 124,
        GamePlayerRemovedEvent = 125,
        GamePlayerRejoinedEvent = 126,
        MatchRejoinSyncEvent = 127,
        MatchPropertyChangedEvent = 128,
        SetMatchPropertyRequest = 129,
        SetMatchPropertyResponse = 130,
        GetPlayerPendingMatchesRequest = 131,
        GetPlayerMatchesResponse = 132,
        RejoinMatchRequest = 133,
        RejoinMatchResponse = 134,
        GetPlayerChallengesRequest = 161,
        GetPlayerChallengesResponse = 162,
        ResetPasswordWithCodeRequest = 163,
        ResetPasswordWithCodeResponse = 164,

        // Game (user) messages start at HighestInternal + 1.
        HighestInternal = 1023
    }
}
