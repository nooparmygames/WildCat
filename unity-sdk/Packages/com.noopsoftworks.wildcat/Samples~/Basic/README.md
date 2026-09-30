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
is stored. The sample needs an event named `submitScore` with one Int parameter for the game,
with this source:

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

Events are created in the developer panel or with the admin API (`docs/admin-api.md` in the
repository), both of which need a developer login. The developer the development host seeds,
`developer@example.com`, has no password, so give it one through a password reset first. From
a shell, with the host from step 1 running on port 5000 (the admin API is served by the same
process):

1. Ask for a reset code. The answer is always 202.

   ```bash
   curl -i -X POST http://localhost:5000/api/v1/auth/request-password-reset \
     -H "Content-Type: application/json" \
     -d '{"email":"developer@example.com"}'
   ```

2. Read the code from the host's console. Without SMTP settings the host does not send email: it
   logs a warning that starts with "SMTP is not configured" and holds the whole message (subject
   "Reset your WildCat password"); the code is the line after "choose a new password:". It
   stays valid for an hour.

3. Set a password (8 characters or more), with the code in place of `CODE`:

   ```bash
   curl -i -X POST http://localhost:5000/api/v1/auth/reset-password \
     -H "Content-Type: application/json" \
     -d '{"email":"developer@example.com","code":"CODE","newPassword":"sample-password"}'
   ```

4. Log in and keep the token (it is valid for eight hours, or until the development host
   restarts, since it makes a new signing key on every start):

   ```bash
   TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"developer@example.com","password":"sample-password"}' \
     | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
   ```

5. Create the event for the development game. Parameter type 3 is Int; the script is the one
   above on one line:

   ```bash
   curl -i -X PUT http://localhost:5000/api/v1/games/22222222-2222-2222-2222-222222222222/events/submitScore \
     -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"parameters":[{"name":"score","type":3}],"script":"async Task<int> submitScore(int score) { int accepted = Math.Max(0, Math.Min(score, 10000)); await WildCat.SetScoreInLeaderBoard(WildCat.CurrentPlayer, \"l1\", accepted); return await WildCat.GetPlayerRankInLeaderBoard(WildCat.CurrentPlayer, \"l1\"); }"}'
   ```

   The answer is 200 with the saved event, or 400 with the compiler's messages in `errors`.

The memory storage of the development host forgets the event (and the password) when it stops;
repeat the steps after a restart, or run the host against PostgreSQL.

## What to look at

- `BasicSample.cs`: every call is `await`ed from the main thread; events and relay handlers also
  run on the main thread, because WildCatBehaviour pumps the SDK's dispatcher in Update.
- `PositionMessage.cs`: a game message. Types above `WildCatMsgType.HighestInternal` are relayed
  to the other players of the match, in order, without the server reading them.
- `TimestampedBuffer.cs`: shows the other player's cube slightly in the past, interpolated, so it
  moves smoothly although messages arrive unevenly.
