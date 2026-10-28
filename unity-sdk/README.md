# WildCat Unity SDK project

The WildCat client SDK for Unity 6.6, and the Unity project it is developed in.

```
Packages/com.noopsoftworks.wildcat/   the SDK, a UPM package (README.md there: install, quick start, migration)
Packages/manifest.json                the project's Unity packages; the SDK loads as an embedded package
ProjectSettings/                      project settings
Assets/WebGLSmoke/                     browser integration scene; the SDK and Basic sample live in the package
Tests/WildCat.Sdk.Tests/              plain .NET tests of the package's engine-free code against the real server
Tests/WildCat.Sdk.CoreCheck/          builds the engine-free code as Unity does (netstandard2.1, C# 9)
```

Open this folder with Unity 6.6. `ProjectVersion.txt` selects 6000.6.5f1. Install Web Build
Support for this Editor to build the browser integration scene.

The core tests need only the .NET 10 SDK. Run them from the repository root:

```bash
dotnet test unity-sdk/Tests/WildCat.Sdk.Tests/WildCat.Sdk.Tests.csproj
dotnet build unity-sdk/Tests/WildCat.Sdk.CoreCheck/WildCat.Sdk.CoreCheck.csproj
```

For a browser integration check, start the backend host in Development on port 5000 with its
default seeded developer and game. Build `Assets/WebGLSmoke/Smoke.unity` for Web, then serve
the output with the compression headers it needs. The scene exercises connection, login,
heartbeats, request timeouts, two-player matchmaking, relay, reconnection and disconnect.
It prints `WILDCAT_WEBGL_SMOKE_PASS` when the checks finish, or `WILDCAT_WEBGL_SMOKE_FAIL`
with the failing step. These checks create disposable device accounts in the development game.

The repository's browser runner starts a local server and headless Chrome. From the repository
root, install its test dependency outside the source directories and pass the build output:

```bash
npm install --prefix .tmp/webgl-review playwright --no-audit --no-fund
node tools/webgl-smoke.mjs .tmp/webgl-review/player .tmp/webgl-review/node_modules/playwright/index.mjs
```

Chrome must be installed. The runner writes `browser-results.json` beside the build directory.
With the Unity CLI installed, the build itself can run without an open Editor:

```bash
unity run unity-sdk --timeout 600 -- -executeMethod WebGLSmokeBuild.Build
```

`Packages/com.noopsoftworks.wildcat/Runtime/Protocol` is generated from `backend/WildCat.Protocol`
by `tools/sync-protocol.sh`; never edit it by hand. After adding files to the package without
Unity, run `tools/unity-package-meta.sh` to give them .meta files.
