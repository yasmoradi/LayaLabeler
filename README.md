# Laya Labeler

Suggests an area and a kind label for [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) issues with
[Laya](https://github.com/NandhaKishorM/laya), an open-source decision model, running inside the .NET process
through [CSnakes](https://github.com/tonybaloney/CSnakes). One C# file, one small Python file, no `.csproj`.

```sh
export GITHUB_TOKEN=$(gh auth token)     # optional; PowerShell: $env:GITHUB_TOKEN = gh auth token
dotnet run LayaLabeler.cs                # interactive
dotnet run LayaLabeler.cs -- 69948       # one issue
dotnet run LayaLabeler.cs -- learn       # refresh training-data.json from GitHub (then commit it)
```

Needs the .NET 10 SDK, nothing else. The first run downloads Python 3.12, PyTorch, Laya and its weights
(about 1.8 GB); that is the only time pip runs as a separate process. 

**From then on Python is a DLL inside the .NET process**

`decision.py` exposes one typed function; CSnakes turns it into a C# method at build time:

```python
def choose(state: str, question: str, options: dict[str, str]) -> dict[str, float]: ...
```
```csharp
var probabilities = decision.Choose(issueText, "Which part of ASP.NET Core does this issue belong to?", areas);
```

Laya alone doesn't know that this repo files OpenAPI under `area-minimal` or Kestrel under `area-networking`.
`training-data.json` holds 557 past labeled issues. From their titles the app picks each area's typical words
as that option's text, and weights Laya's answer by how common each area is. Nothing is trained; it is counting.

| 50 issues not in the training data | Right area |
|---|---|
| Always `area-blazor` | 50% |
| Laya alone | 38% |
| Laya + training data | **74%** (26 of 28 right when it is at least 70% sure) |
