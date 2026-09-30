# WildCat Basic sample

One IMGUI screen that walks through the SDK: connect and log in with a device id, find a match
and relay cube positions between two clients, submit a score through a server script event and
read the leaderboard, and invite, accept and list friends.

The sample has no scene file; it builds what it needs at run time. Setting it up takes a minute:

1. Start a server. From the repository root, `dotnet run --project backend/WildCat.Host` runs
   one on `ws://localhost:5000/ws` with the development developer and game keys that
   `WildCatSettings.asset` in this folder already holds (`1111...` and `2222...`).
2. Import the sample: Window > Package Manager > WildCat > Samples > Basic > Import.
3. Open a new empty scene, then choose WildCat > Add Client To Scene. It adds a "WildCat" object
   with the WildCat Client component and the first WildCatSettings asset of the project; check
   that it picked the one from this sample (or your own, pointing at your server).
4. Select the "WildCat" object and add the Basic Sample component (Add Component > search
   "Basic Sample").
5. Press Play, then "Connect and log in".

For the match and friends parts you need a second client: build a player, or choose WildCat >
Create Clone and open the clone in a second editor. Each run makes up a new device id (you can
type your own before logging in), so two clients on one machine are two players.

## The score script event

Clients cannot set leaderboard scores; a server script event does, so the server decides what
is stored. Create an event named `submitScore` with one Int parameter for the game, in the admin
panel or with the admin API (`PUT /api/v1/games/{gameKey}/events/submitScore`), with this source:

```csharp
async Task<int> submitScore(int score)
{
    // The score comes from the client only to keep the sample short. A real game computes it
    // on the server (from match results, for example) or at least clamps it, as here.
    int accepted = Math.Max(0, Math.Min(score, 10000));
    await WildCat.SetScoreInLeaderBoard(WildCat.CurrentPlayer, "l1", accepted);
    return await WildCat.GetPlayerRankInLeaderBoard(WildCat.CurrentPlayer, "l1");
}
```

The development host seeds the leaderboard `l1` for its game. "Show leaderboard" reads it with
`client.Leaderboards.GetTopAsync`.

## What to look at

- `BasicSample.cs`: every call is `await`ed from the main thread; events and relay handlers also
  run on the main thread, because WildCatBehaviour pumps the SDK's dispatcher in Update.
- `PositionMessage.cs`: a game message. Types above `WildCatMsgType.HighestInternal` are relayed
  to the other players of the match, in order, without the server reading them.
- `TimestampedBuffer.cs`: shows the other player's cube slightly in the past, interpolated, so it
  moves smoothly although messages arrive unevenly.
