namespace WildCat.Admin;

// Request and response bodies of the admin API. JSON uses camelCase names. Grain state classes are
// never returned directly: every response is mapped to one of these types, so password hashes,
// code hashes and (outside /me) the developer secret never leave the server. Request bodies are
// classes with optional properties; the endpoints report missing values as validation errors.

// Auth

public sealed class RegisterRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

public sealed class ActivateRequest
{
    public string? Email { get; init; }
    public string? Code { get; init; }
}

public sealed class LoginRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
}

public sealed class EmailRequest
{
    public string? Email { get; init; }
}

public sealed class ResetPasswordRequest
{
    public string? Email { get; init; }
    public string? Code { get; init; }
    public string? NewPassword { get; init; }
}

public sealed class ChangePasswordRequest
{
    public string? OldPassword { get; init; }
    public string? NewPassword { get; init; }
}

/// <summary>
/// The developer as shown after login. The secret is only returned by /me.
/// </summary>
public sealed record DeveloperSummary(Guid Key, string Email, string? FirstName, string? LastName, bool IsAdmin);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, DeveloperSummary Developer);

// Me

public sealed record MeResponse(Guid Key, string Email, string? FirstName, string? LastName, bool IsAdmin, Guid Secret);

public sealed class UpdateMeRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

public sealed record SecretResponse(Guid Secret);

// Games

/// <summary>
/// Genre 0 Action, 1 Arcade, 2 Strategy, 3 Puzzle, 4 Casual, 5 Sports. Region 0 Domestic,
/// 1 Foreign. AgeRating 0 All, 1 Children, 2 Teens, 3 Adults.
/// </summary>
public sealed record GameResponse(Guid Key, string Name, string? Description, string? StudioName, string? Website,
    int Genre, int Region, string? Platform, int AgeRating);

public sealed class GameRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? StudioName { get; init; }
    public string? Website { get; init; }
    public int Genre { get; init; }
    public int Region { get; init; }
    public string? Platform { get; init; }
    public int AgeRating { get; init; }
}

public sealed record CreatedKeyResponse(Guid Key);

// Leaderboards

/// <summary>
/// BestScore true keeps each player's best score; false keeps the last one.
/// </summary>
public sealed record LeaderboardResponse(string Name, string Key, bool BestScore);

public sealed class CreateLeaderboardRequest
{
    public string? Name { get; init; }
    public bool BestScore { get; init; }
}

// Items

public sealed record ItemPrice(string? Item, int Count);

/// <summary>
/// EffectTime and RegenerationPeriod are TimeSpan strings in the invariant "[d.]hh:mm:ss" form,
/// such as "1.02:03:04" for one day, two hours, three minutes and four seconds.
/// </summary>
public sealed record ItemResponse(string Name, string? Description, TimeSpan EffectTime, bool Stackable,
    int MaxAllowedToHave, bool IsConsumable, bool AutomaticallyConsumed, bool IsRegenerating, int RegenerationAmount,
    TimeSpan RegenerationPeriod, Dictionary<string, string> MetaData, List<ItemPrice> Price);

public sealed class ItemRequest
{
    public string? Description { get; init; }
    public TimeSpan? EffectTime { get; init; }
    public bool Stackable { get; init; }
    public int MaxAllowedToHave { get; init; }
    public bool IsConsumable { get; init; }
    public bool AutomaticallyConsumed { get; init; }
    public bool IsRegenerating { get; init; }
    public int RegenerationAmount { get; init; }
    public TimeSpan? RegenerationPeriod { get; init; }
    public Dictionary<string, string>? MetaData { get; init; }
    public List<ItemPrice>? Price { get; init; }
}

// Script events

/// <summary>
/// Type is a number in EventParameter.ParamType order: 0 Bool, 1 Byte, 2 Short, 3 Int, 4 Long,
/// 5 Float, 6 Double, 7 String, 8 ByteArray.
/// </summary>
public sealed record EventParameterDto(string? Name, int Type);

public sealed record ScriptEventResponse(string Name, List<EventParameterDto> Parameters, string Script);

public sealed class ScriptEventRequest
{
    public List<EventParameterDto>? Parameters { get; init; }
    public string? Script { get; init; }
}

// Game modes

public sealed record MatchmakingCriterionDto(bool IsMinPlayersEnough, int AcceptableThreshold, int StartTimeInSeconds);

public sealed record GameModeResponse(string Name, int MinPlayers, int MaxPlayers, bool FinishGameWithOneDisconnect,
    List<MatchmakingCriterionDto> Criteria);

/// <summary>
/// FinishGameWithOneDisconnect is optional: when left out, an existing mode keeps its value and a
/// new mode gets true.
/// </summary>
public sealed class GameModeRequest
{
    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; }
    public bool? FinishGameWithOneDisconnect { get; init; }
    public List<MatchmakingCriterionDto>? Criteria { get; init; }
}

// Publisher data

public sealed record PublisherDataEntry(string Key, string Value);

public sealed class PublisherDataRequest
{
    public string? Value { get; init; }
}

// Admin

public sealed record AdminDeveloperResponse(Guid Key, string Email, string? FirstName, string? LastName, bool IsActivated);

public sealed record DeveloperPage(List<AdminDeveloperResponse> Items, int Total);
