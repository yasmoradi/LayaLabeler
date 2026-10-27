# AGENTS.md

Setup notes for AI coding agents. Humans: see README.md.

- **Shape**: a .NET 10 file-based app. `LayaLabeler.cs` is the whole C# program; its `#:package` and
  `#:property` lines replace a `.csproj`. `decision.py` is the whole Python side; CSnakes generates the typed
  `IDecision` wrapper from its type hints and embeds the file. `requirements.txt` and `training-data.json`
  sit next to them and are read at run time.
- **Prerequisite**: the .NET 10 SDK or later. Do not install Python; CSnakes downloads CPython 3.12.
- **Run**: `dotnet run LayaLabeler.cs -- 69948`. Each issue costs one GitHub API call; set `GITHUB_TOKEN`
  (for example from `gh auth token`) if GitHub answers 403.
- **First run** takes 2 to 5 minutes and downloads about 1.8 GB: CPython to `%APPDATA%\CSnakes`, the venv to
  `%LOCALAPPDATA%\LayaLabeler\venv` (`~/.local/share/LayaLabeler/venv` on Linux/macOS), weights to the
  Hugging Face cache. pip runs only while `venv/.installed` is missing; later runs start no child process and
  take about 10 seconds. After changing `requirements.txt`, delete that marker so pip runs again.
- **Success**: a table titled `Area` and one titled `Kind`, each with probabilities, for the issue.
- **Training data**: `dotnet run LayaLabeler.cs -- learn` rewrites `training-data.json` from the newest
  ~2,000 aspnetcore issues and pull requests (about 25 API calls; use a token). Commit the result.

## Troubleshooting

- `CS0246: 'IDecision' could not be found`: CSnakes could not parse `decision.py` (see the `PSG...` warning).
  Annotated parameters need types it maps, with type arguments: `dict[str, str]`, not `dict`.
- `WinError 87 ... 'bin'` on `import torch`: CSnakes bug #653. Keep the workaround at the top of `decision.py`.
- Broken or interrupted first run: delete the venv folder and run again.
- Linux without a GPU: add `--extra-index-url https://download.pytorch.org/whl/cpu` as the first line of
  `requirements.txt` before the first run, or pip pulls the multi-GB CUDA build of torch.
- Keep `#:property PublishAot=false` (AOT disables the reflection JSON used for GitHub) and
  `#:property EnableDefaultNoneItems=true` (without it CSnakes never sees `decision.py`).
