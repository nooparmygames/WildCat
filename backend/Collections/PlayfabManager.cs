using Interfaces;
using Orleans;
using PlayFab;
using PlayFab.GroupsModels;
using PlayFab.ServerModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EntityKey = PlayFab.ClientModels.EntityKey;

namespace Collections
{
    public class PlayfabManager : IBackendStorageServer
    {
        private string titleId;
        private string secret = ""; // TODO: read from configuration; the original value was removed before open-sourcing
        private bool isInitialized;

        public PlayfabManager()
        {

        }

        public string GetSecret()
        {
            return PlayFabSettings.staticSettings.DeveloperSecretKey;
        }

        private async Task GetTokenAsync(int tryCount = 0)
        {
            if (tryCount >= 10)
            {
                System.Environment.Exit(1);
                return;
            }
            var result = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new PlayFab.AuthenticationModels.GetEntityTokenRequest());
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                await Task.Delay(1000);
                await GetTokenAsync(tryCount++);
            }
            else
            {
                if (!isInitialized)
                    isInitialized = true;
                await Task.Delay(TimeSpan.FromHours(2));
                GetTokenAsync().Ignore();
            }
        }

        public async Task<(int, string, GetPlayerCombinedInfoResultPayload, string, string)> ValidateSessionTicket(string ticket)
        {
            var result = await PlayFabServerAPI.AuthenticateSessionTicketAsync(new PlayFab.ServerModels.AuthenticateSessionTicketRequest
            {
                SessionTicket = ticket

            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null, "", "");
            var data = result.Result.UserInfo;
            var entity = data.TitleInfo.TitlePlayerAccount;
            var result2 = await PlayFabServerAPI.GetPlayerCombinedInfoAsync(new GetPlayerCombinedInfoRequest
            {
                PlayFabId = data.PlayFabId,
                InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                {
                    ProfileConstraints = new PlayerProfileViewConstraints
                    {
                        ShowContactEmailAddresses = true
                    },
                    GetCharacterList = true,
                    GetPlayerProfile = true,
                    GetPlayerStatistics = true,
                    GetUserAccountInfo = true,
                    GetUserInventory = true,
                    GetUserVirtualCurrency = true,
                    GetCharacterInventories = true,
                }
            });
            if (result2.Error != null && result.Error.Error != PlayFabErrorCode.Success)
                return ((int)result2.Error.Error, result2.Error.GenerateErrorReport(), null, "", "");
            return (0, "", result2.Result.InfoResultPayload, entity.Id, entity.Type);
        }

        public async Task<(int, string, string)> GetPlayfabIdFromUserName(string userName)
        {
            var result = await PlayFabAdminAPI.GetUserAccountInfoAsync(new PlayFab.AdminModels.LookupUserAccountInfoRequest
            {
                Username = userName
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null);
            var id = (result.Result.UserInfo.PlayFabId);
            return (0, "", id);
        }

        public async Task<(int, string, string, string)> GetPlayerEntityInfo(string player)
        {
            var result = await PlayFabServerAPI.GetUserAccountInfoAsync(new GetUserAccountInfoRequest
            {
                PlayFabId = player
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null, null);
            var (id, type) = (result.Result.UserInfo.TitleInfo.TitlePlayerAccount.Id, result.Result.UserInfo.TitleInfo.TitlePlayerAccount.Type);
            return (0, "", id, type);
        }

        public async Task<(int, string)> CreateCharacter(string playfabId, string type, string name)
        {
            var result = await PlayFabServerAPI.GrantCharacterToUserAsync(new PlayFab.ServerModels.GrantCharacterToUserRequest
            {
                PlayFabId = playfabId,
                CharacterType = type,
                CharacterName = name
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            }
            return (0, result.Result.CharacterId);
        }

        public async Task<(int, string, GetLeaderboardResult)> GetLeaderboard(string name)
        {
            var result = await PlayFabServerAPI.GetLeaderboardAsync(new GetLeaderboardRequest
            {
                StatisticName = name
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null);
            }

            return (0, "", result.Result);
        }

        public async Task<(int, string, GetLeaderboardAroundUserResult)> GetLeaderboardAroundUser(string name, string playfabId)
        {
            var result = await PlayFabServerAPI.GetLeaderboardAroundUserAsync(new GetLeaderboardAroundUserRequest
            {
                StatisticName = name,
                PlayFabId = playfabId
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null);
            }

            return (0, "", result.Result);
        }

        public async Task<(int, string)> ResetLeaderboard(string name)
        {
            var result = await PlayFabAdminAPI.IncrementPlayerStatisticVersionAsync(new PlayFab.AdminModels.IncrementPlayerStatisticVersionRequest
            {
                StatisticName = name
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            }
            return (0, "");
        }

        public async Task<(int, string)> DeleteCharacter(string playfabId, string characterId)
        {
            var result = await PlayFabServerAPI.DeleteCharacterFromUserAsync(new PlayFab.ServerModels.DeleteCharacterFromUserRequest
            {
                CharacterId = characterId,
                PlayFabId = playfabId,
                SaveCharacterInventory = false
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            }
            return (0, "");
        }

        public async Task<(int, string, List<CharacterResult>)> GetCharacters(string playfabId)
        {
            var result = await PlayFabServerAPI.GetAllUsersCharactersAsync(new PlayFab.ServerModels.ListUsersCharactersRequest
            {
                PlayFabId = playfabId
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null);
            }
            return (0, "", result.Result.Characters);
        }

        public async Task<(int, string, Dictionary<string, PlayFab.DataModels.ObjectResult>, int)> GetObjects(string entityId, string entityType)
        {
            var result = await PlayFabDataAPI.GetObjectsAsync(new PlayFab.DataModels.GetObjectsRequest
            {
                Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null, -1);
            }
            return (0, "", result.Result.Objects, result.Result.ProfileVersion);
        }

        public async Task<(int, string, int)> SetObjects(string entityId, string entityType, List<PlayFab.DataModels.SetObject> objects, int? version)
        {
            var result = await PlayFabDataAPI.SetObjectsAsync(new PlayFab.DataModels.SetObjectsRequest
            {
                Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
                Objects = objects,
                ExpectedProfileVersion = version
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), -1);
            }
            return (0, "", result.Result.ProfileVersion);
        }

        public const string confirmRegistrationEmailTemplateId = ""; // TODO: read from configuration
        public async Task<(int, string)> SendConformationEmail(string playfabId)
        {
            var res = await PlayFabServerAPI.SendEmailFromTemplateAsync(new SendEmailFromTemplateRequest
            {
                PlayFabId = playfabId,
                EmailTemplateId = confirmRegistrationEmailTemplateId
            });
            if (res.Error != null && res.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)res.Error.Error, res.Error.GenerateErrorReport());
            }
            return (0, "");
        }

        public async Task<(int, string)> SendNotification(string playfabId, string template)
        {
            var result = await PlayFabServerAPI.SendPushNotificationFromTemplateAsync(new SendPushNotificationFromTemplateRequest
            {
                Recipient = playfabId,
                PushNotificationTemplateId = template
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            }
            return (0, "");
        }

        HashSet<string> saveRequests = new HashSet<string>();

        public async Task<(int, string, int)> SaveFile(string entityId, string entityType, Dictionary<string, byte[]> fileNames, int? version)
        {
            if (saveRequests.Contains(entityId))
                return (-1, "", -1);
            saveRequests.Add(entityId);
            var result = await PlayFabDataAPI.InitiateFileUploadsAsync(new PlayFab.DataModels.InitiateFileUploadsRequest
            {
                ProfileVersion = version,
                Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
                FileNames = fileNames.Keys.ToList(),
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                if (result.Error.Error == PlayFabErrorCode.EntityFileOperationPending)
                {
                    var abortRes = await PlayFabDataAPI.AbortFileUploadsAsync(new PlayFab.DataModels.AbortFileUploadsRequest
                    {
                        Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
                        FileNames = fileNames.Keys.ToList(),
                    });
                }
                saveRequests.Remove(entityId);
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), -1);
            }
            foreach (var uploadDetail in result.Result.UploadDetails)
            {
                string uploadUrl = uploadDetail.UploadUrl;
                try
                {
                    using (HttpClient c = new HttpClient())
                    {
                        await c.PutAsync(uploadUrl, new ByteArrayContent(fileNames[uploadDetail.FileName]));
                    }
                }
                catch (Exception ex)
                {
                    saveRequests.Remove(entityId);
                    return (-1, ex.Message, -1);
                }
            }
            var finalizeResult = await PlayFabDataAPI.FinalizeFileUploadsAsync(new PlayFab.DataModels.FinalizeFileUploadsRequest
            {
                Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType },
                FileNames = fileNames.Keys.ToList()
            });
            if (finalizeResult.Error != null && finalizeResult.Error.Error != PlayFabErrorCode.Success)
            {
                saveRequests.Remove(entityId);
                return ((int)finalizeResult.Error.Error, finalizeResult.Error.GenerateErrorReport(), -1);
            }
            saveRequests.Remove(entityId);
            return (0, result.Result.UploadDetails[0].FileName, result.Result.ProfileVersion);
        }

        public async Task<(int, string, Dictionary<string, byte[]>, int)> LoadFile(string entityId, string entityType, string[] filenames)
        {

            var result = await PlayFabDataAPI.GetFilesAsync(new PlayFab.DataModels.GetFilesRequest
            {
                Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType }
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                if (result.Error.Error == PlayFabErrorCode.FileNotFound)
                    return (1, result.Error.GenerateErrorReport(), new Dictionary<string, byte[]>(), -1);
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), new Dictionary<string, byte[]>(), -1);
            }
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            foreach (var file in filenames)
            {

                if (!result.Result.Metadata.ContainsKey(file))
                    files.Add(file, null);
                else
                {
                    try
                    {
                        using (HttpClient c = new HttpClient())
                        {
                            var data = await c.GetByteArrayAsync(result.Result.Metadata[file].DownloadUrl);
                            files.Add(file, data);
                        }
                    }
                    catch (Exception ex)
                    {
                        return (-1, ex.Message, new Dictionary<string, byte[]>(), -1);
                    }
                }
            }
            int code = files.Any(x => x.Value == null) ? 1 : 0;
            return (code, "", files, result.Result.ProfileVersion);
        }

        public async Task SaveTitleData(Dictionary<string, string> data)
        {
            foreach (var kvp in data)
            {
                var res = await PlayFabServerAPI.SetTitleDataAsync(new SetTitleDataRequest
                {
                    Key = kvp.Key,
                    Value = kvp.Value
                });
                if (res.Error != null && res.Error.Error != PlayFabErrorCode.Success)
                    throw new System.Exception("saving failed");
            }
        }

        public async Task<(int, string, Dictionary<string, string>)> LoadTitleData(List<string> keys)
        {
            var result = await PlayFabServerAPI.GetTitleDataAsync(new GetTitleDataRequest
            {
                Keys = keys
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null);
            return (0, "", result.Result.Data);
        }

        public async Task<(int, string)> SaveStats(string characterId, string playfabId, Dictionary<string, int> stats)
        {
            var result = await PlayFabServerAPI.UpdateCharacterStatisticsAsync(new UpdateCharacterStatisticsRequest
            {
                CharacterId = characterId,
                PlayFabId = playfabId,
                CharacterStatistics = stats
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            return (0, "");
        }

        public async Task<(int, string)> SavePlayerStats(string playfabId, List<StatisticUpdate> stats)
        {
            var result = await PlayFabServerAPI.UpdatePlayerStatisticsAsync(new UpdatePlayerStatisticsRequest
            {
                PlayFabId = playfabId,
                Statistics = stats
            });
            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            return (0, "");
        }

        public async Task<(int, string, GetCharacterInventoryResult)> LoadCharacterInventory(string characterId, string playfabId)
        {
            var inventoryRes = await PlayFabServerAPI.GetCharacterInventoryAsync(new GetCharacterInventoryRequest
            {
                CharacterId = characterId,
                PlayFabId = playfabId
            });
            if (inventoryRes.Error != null && inventoryRes.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)inventoryRes.Error.Error, inventoryRes.Error.GenerateErrorReport(), null);
            }
            return (0, "", inventoryRes.Result);
        }

        public async Task<(int, string)> ConsumeItem(string characterId, string playfabId, string itemId, int consumeAmount, int tryCount = 0)
        {
            var consumeItemResult = await PlayFabServerAPI.ConsumeItemAsync(new ConsumeItemRequest()
            {
                CharacterId = characterId,
                PlayFabId = playfabId,
                ItemInstanceId = itemId,
                ConsumeCount = consumeAmount,

            });

            if (consumeItemResult.Error != null && consumeItemResult.Error.Error != PlayFabErrorCode.Success)
            {
                if (tryCount < 4)
                {
                    return await ConsumeItem(characterId, playfabId, itemId, consumeAmount, tryCount + 1);
                }
                else
                    return ((int)consumeItemResult.Error.Error, consumeItemResult.Error.GenerateErrorReport());

            }

            return (0, "");

        }



        public async Task<(int, string, int)> AddCurrency(string playfabId, string characterId, string currency, int amount)
        {
            if (amount > 0)
            {
                var addRes = await PlayFabServerAPI.AddCharacterVirtualCurrencyAsync(new AddCharacterVirtualCurrencyRequest
                {
                    PlayFabId = playfabId,
                    CharacterId = characterId,
                    Amount = amount,
                    VirtualCurrency = currency
                });
                if (addRes.Error != null && addRes.Error.Error != PlayFabErrorCode.Success)
                    return ((int)addRes.Error.Error, addRes.Error.GenerateErrorReport(), 0);
                return (0, "", addRes.Result.Balance);
            }
            else
            {
                var subtractRes = await PlayFabServerAPI.SubtractCharacterVirtualCurrencyAsync(new SubtractCharacterVirtualCurrencyRequest
                {
                    PlayFabId = playfabId,
                    CharacterId = characterId,
                    Amount = -amount,
                    VirtualCurrency = currency
                });
                if (subtractRes.Error != null && subtractRes.Error.Error != PlayFabErrorCode.Success)
                    return ((int)subtractRes.Error.Error, subtractRes.Error.GenerateErrorReport(), 0);
                return (0, "", subtractRes.Result.Balance);
            }
        }

        public async Task<(int, string, GetCatalogItemsResult)> GetCatalog(string version)
        {
            var res = await PlayFabServerAPI.GetCatalogItemsAsync(new GetCatalogItemsRequest
            {
                CatalogVersion = version
            });
            if (res.Error != null && res.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)res.Error.Error, res.Error.GenerateErrorReport(), null);
            }
            return (0, "", res.Result);
        }

        public DateTime GetCurrentTime()
        {
            return DateTime.UtcNow;
        }

        public void AddDateTimeOffset(TimeSpan ts)
        {
            throw new InvalidOperationException("You are not allowed to change time in real playfab implementation");
        }

        public async Task<(int, string)> CloseServerForMatchMaking(string lobbyId)
        {
            var res = await PlayFabServerAPI.SetGameServerInstanceStateAsync(new SetGameServerInstanceStateRequest { LobbyId = lobbyId, State = GameInstanceState.Closed });
            if (res.Error != null && res.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)res.Error.Error, res.Error.GenerateErrorReport());
            }
            else
            {
                return (0, "");
            }
        }

        public async Task<(int, string, string token, string[] players, int count)> GetPlayersForSegment(string segment, string token = null)
        {
            var res = await PlayFabServerAPI.GetPlayersInSegmentAsync(new GetPlayersInSegmentRequest
            {
                SegmentId = segment,
                SecondsToLive = 1800,
                MaxBatchSize = 1000,
                ContinuationToken = token
            });
            if (res.Error != null && res.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)res.Error.Error, res.Error.GenerateErrorReport(), null, null, 0);
            }
            else
            {
                return (0, "", res.Result.ContinuationToken, res.Result.PlayerProfiles.Select(x => x.PlayerId).ToArray(), res.Result.ProfilesInSegment);
            }

        }

        public async Task<(int, string, PlayFab.GroupsModels.EntityKey)> CreateGroup(string cId, string cType, string groupName)
        {
            var result = await PlayFabGroupsAPI.CreateGroupAsync(new PlayFab.GroupsModels.CreateGroupRequest
            {
                Entity = new PlayFab.GroupsModels.EntityKey() { Id = cId, Type = cType },
                GroupName = groupName
            });


            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null);
            }
            return (0, "", result.Result.Group);
        }

        public async Task<(int, string, ListMembershipResponse)> GetCharacterMembership(PlayFab.GroupsModels.EntityKey characterKey)
        {
            var result = await PlayFabGroupsAPI.ListMembershipAsync(new ListMembershipRequest()
            {
                Entity = characterKey
            });


            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport(), null);
            }

            return (0, "", result.Result);

        }

        public async Task<(int, string)> InviteMember(PlayFab.GroupsModels.EntityKey invitedMemberEntityKey, PlayFab.GroupsModels.EntityKey schoolKey)
        {
            var result = await PlayFabGroupsAPI.InviteToGroupAsync(new InviteToGroupRequest()
            {
                Entity = invitedMemberEntityKey,
                Group = schoolKey,
                RoleId = "members",
            });

            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            }

            return (0, "");

        }

        public async Task<(int, string)> AcceptGroupInvitation(PlayFab.GroupsModels.EntityKey characterEntityKey, PlayFab.GroupsModels.EntityKey schoolKey)
        {
            var result = await PlayFabGroupsAPI.AcceptGroupInvitationAsync(new AcceptGroupInvitationRequest()
            {
                Entity = characterEntityKey,
                Group = schoolKey,
            });

            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            }

            return (0, "");
        }

        public async Task<(int, string)> ApplyToGroup(PlayFab.GroupsModels.EntityKey characterEntityKey, PlayFab.GroupsModels.EntityKey schoolKey)
        {
            var result = await PlayFabGroupsAPI.ApplyToGroupAsync(new ApplyToGroupRequest()
            {
                Entity = characterEntityKey,
                Group = schoolKey,
            });

            if (result.Error != null && result.Error.Error != PlayFabErrorCode.Success)
            {
                return ((int)result.Error.Error, result.Error.GenerateErrorReport());
            }

            return (0, "");
        }

        public Task<bool> IsPlayfabRun()
        {
            return Task.FromResult(!string.IsNullOrEmpty(titleId));
        }

        public Task<string> GetPlayfabId()
        {
            return Task.FromResult(titleId);
        }
        public async Task WaitForInitialization()
        {
            var start = DateTime.UtcNow;
            while (!isInitialized)
            {
                if ((DateTime.UtcNow - start).TotalMinutes > 2)
                {
                    throw new System.TimeoutException("Could not get playfab token");
                }
                await Task.Delay(100);
            }
        }
        public void SetTitleId(string titleId)
        {
            this.titleId = titleId;
            if (!string.IsNullOrEmpty(titleId))
            {
                PlayFabSettings.staticSettings.TitleId = titleId;
                PlayFabSettings.staticSettings.DeveloperSecretKey = secret;
                GetTokenAsync().Ignore();
            }
            else
            {
                isInitialized = true;
            }
        }

        public string GetTitleId()
        {
            return PlayFabSettings.staticSettings.TitleId;
        }

    }
}