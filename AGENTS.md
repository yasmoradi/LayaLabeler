# AGENTS.md

Setup notes for AI coding agents. Humans: see README.md.

- **Shape**: a .NET 10 file-based app. `LayaLabeler.cs` is the whole C# program; its `#:package` and
  `#:property` lines replace a `.csproj`. `decision.py` is the whole Python side; CSnakes generates the typed
  `IDecision` wrapper from its type hints and embeds the file. `requirements.txt` (or `requirements.amd.txt`,
  see Troubleshooting), `training-data.json` and `issues.json` sit next to them and are read at run time.
- **Prerequisite**: the .NET 10 SDK or later, and `gh` signed in (`gh auth login`). Do not install Python;
  CSnakes downloads CPython 3.12.
- **Run**: `dotnet run LayaLabeler.cs -- 69948`, or `-- all` for every issue in `issues.json`. GitHub is read
  only through `gh api`, and only for an issue missing from `issues.json` (50 issues not in
  `training-data.json`, read at run time).
- **First run** takes 2 to 5 minutes and downloads about 1.8 GB: CPython to `%APPDATA%\CSnakes`, the venv to
  `%LOCALAPPDATA%\LayaLabeler\venv` (`~/.local/share/LayaLabeler/venv` on Linux/macOS), weights to the
  Hugging Face cache. pip runs only while `venv/.installed` is missing; later runs start no pip process and
  take about 10 seconds. After changing `requirements.txt`, delete that marker so pip runs again.
- **Success**: a table titled `Area` and one titled `Kind`, each with probabilities, for the issue, and at the
  end a `Performance` summary of the decision times and the hardware they ran on.
- **Training data**: `dotnet run LayaLabeler.cs -- learn` rewrites `training-data.json` from the newest
  ~2,000 aspnetcore issues and pull requests (about 25 `gh api` calls). Commit the result.

## Troubleshooting

- `CS0246: 'IDecision' could not be found`: CSnakes could not parse `decision.py` (see the `PSG...` warning).
  Annotated parameters need types it maps, with type arguments: `dict[str, str]`, not `dict`.
- `WinError 87 ... 'bin'` on `import torch`: CSnakes bug #653. Keep the workaround at the top of `decision.py`.
- Broken or interrupted first run: delete the venv folder and run again.
- Linux without a GPU: add `--extra-index-url https://download.pytorch.org/whl/cpu` as the first line of
  `requirements.txt` before the first run, or pip pulls the multi-GB CUDA build of torch.
- AMD GPU on Windows (Ryzen AI Max / Radeon 8060S, gfx1151): pip on Windows installs the CPU build of torch.
  Build the venv from `requirements.amd.txt` instead: set `LAYA_REQUIREMENTS=requirements.amd.txt` for the
  first run (delete an existing venv first). ROCm shows up as `torch.cuda`, so Laya uses the GPU with no code
  change; `Laya ran on CUDA (...)` in the report confirms it. Other Radeon GPUs: change `device-gfx1151`.
- Keep `#:property PublishAot=false` (AOT disables the reflection JSON used for GitHub) and
  `#:property EnableDefaultNoneItems=true` (without it CSnakes never sees `decision.py`).
