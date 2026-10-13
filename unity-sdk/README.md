# WildCat Unity SDK project

The WildCat client SDK for Unity 6, and the Unity project it is developed in.

```
Packages/com.noopsoftworks.wildcat/   the SDK, a UPM package (README.md there: install, quick start, migration)
Packages/manifest.json                the project's Unity packages; the SDK loads as an embedded package
ProjectSettings/                      project settings
Assets/                               empty: the SDK lives in the package, the sample under its Samples~
Tests/WildCat.Sdk.Tests/              plain .NET tests of the package's engine-free code against the real server
Tests/WildCat.Sdk.CoreCheck/          builds the engine-free code as Unity does (netstandard2.1, C# 9)
```

Open this folder with Unity 6 LTS. `ProjectVersion.txt` selects 6000.0.80f1. Some settings
originate from the earlier Unity project and are upgraded by the Editor when needed.

The core tests need only the .NET 10 SDK. Run them from the repository root:

```bash
dotnet test unity-sdk/Tests/WildCat.Sdk.Tests/WildCat.Sdk.Tests.csproj
dotnet build unity-sdk/Tests/WildCat.Sdk.CoreCheck/WildCat.Sdk.CoreCheck.csproj
```

`Packages/com.noopsoftworks.wildcat/Runtime/Protocol` is generated from `backend/WildCat.Protocol`
by `tools/sync-protocol.sh`; never edit it by hand. After adding files to the package without
Unity, run `tools/unity-package-meta.sh` to give them .meta files.
