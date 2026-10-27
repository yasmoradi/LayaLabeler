#:package Spectre.Console@0.57.2
#:package CSnakes.Runtime@2.0.0-beta.350
#:package Microsoft.Extensions.Hosting@10.0.14

#:property TargetFramework=net10.0
#:property PublishAot=false
#:property EmbedPythonSources=true
#:property EnableDefaultNoneItems=true

using System.Diagnostics;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

using Spectre.Console;
using CSnakes.Runtime;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Repo = "dotnet/aspnetcore";
var folder = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? AppContext.BaseDirectory;
var dataFile = Path.Combine(folder, "training-data.json");

using var http = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
http.DefaultRequestHeaders.UserAgent.ParseAdd("LayaLabeler");
if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
    http.DefaultRequestHeaders.Authorization = new("Bearer", token);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

if (args is ["learn"])
{
    await LearnAsync();
    return 0;
}

// Laya alone does not know that this repo files OpenAPI under area-minimal or Kestrel under area-networking.
// training-data.json (past labeled issues) teaches it each area's typical words and how common each area is.
var data = JsonSerializer.Deserialize<TrainingData>(File.ReadAllText(dataFile), json)!;
var (areas, prior) = Profile(data);
var kinds = new Dictionary<string, string>
{
    ["bug"] = "something is broken or behaves incorrectly",
    ["enhancement"] = "a request for a new feature or an improvement",
    ["api-suggestion"] = "a proposal to add or change a public .NET API",
    ["question"] = "the author asks how to do something",
    ["Docs"] = "documentation is missing, wrong or unclear",
};

// Python runs inside this process: CSnakes loads python312.dll into it, and torch's DLLs follow on import.
// Only the one-time package install runs pip as a separate process.
var venv = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayaLabeler", "venv");
var installed = Path.Combine(venv, ".installed");
var firstRun = !File.Exists(installed);
var builder = Host.CreateApplicationBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var python = builder.Services.WithPython().WithHome(folder).FromRedistributable("3.12").WithVirtualEnvironment(venv);
if (firstRun)
    python.WithPipInstaller();
using var host = builder.Build();

var decision = await AnsiConsole.Status().StartAsync(
    firstRun ? "First run: installing Python 3.12, PyTorch and Laya (about 1.8 GB, once)..." : "Loading Python and Laya into this process...",
    _ => Task.Run(() => host.Services.GetRequiredService<IPythonEnvironment>().Decision()));
File.WriteAllText(installed, "");

var loaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(m => m.ModuleName)
    .Where(name => name.StartsWith("python3", StringComparison.OrdinalIgnoreCase) || name.StartsWith("torch_cpu", StringComparison.OrdinalIgnoreCase));
AnsiConsole.MarkupLine($"[green]✓[/] Python is running inside this .NET process (PID {Environment.ProcessId}): {string.Join(", ", loaded)}");

if (args.Length > 0)
    foreach (var arg in args)
        await ShowAsync(arg);
else
    while (AnsiConsole.Prompt(new TextPrompt<string>("[mediumpurple]Issue #[/] [grey](empty to quit)[/]").AllowEmpty()) is { Length: > 0 } input)
        await ShowAsync(input);
return 0;

async Task ShowAsync(string input)
{
    if (!int.TryParse(input.Trim().TrimStart('#'), out var number))
    {
        AnsiConsole.MarkupLine("[red]Type an issue number.[/]");
        return;
    }
    try
    {
        var issue = (await http.GetFromJsonAsync<Issue>($"repos/{Repo}/issues/{number}", json))!;
        var body = Regex.Replace(issue.Body ?? "", @"<!--.*?-->|### Is there an existing issue for this\?\s*- \[[ xX]\][^\n]*", "", RegexOptions.Singleline);
        var text = issue.Title + "\n\n" + body.Trim();
        text = text[..Math.Min(text.Length, 4_000)]; // Laya reads the first 512 tokens anyway

        var (area, kind) = await AnsiConsole.Status().StartAsync("Laya is deciding...", _ => Task.Run(() => (
            decision.Choose(text, "Which part of ASP.NET Core does this GitHub issue belong to?", areas),
            decision.Choose(text, "What kind of GitHub issue is this?", kinds))));

        // Bayes: Laya scores how well the text fits each area, the prior says how common the area is.
        var weighted = area.ToDictionary(a => a.Key, a => a.Value * prior[a.Key]);
        var total = weighted.Values.Sum();

        AnsiConsole.Write(new Rule($"[bold]#{issue.Number}[/] {Markup.Escape(issue.Title)}").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]{issue.HtmlUrl}[/]");
        AnsiConsole.MarkupLine($"[grey]Current labels:[/] {Markup.Escape(string.Join(", ", issue.Labels.Select(l => l.Name)))}");
        AnsiConsole.Write(new Columns(
            Scores("Area", weighted.ToDictionary(w => w.Key, w => w.Value / total), "area-"),
            Scores("Kind", kind, "")).Collapse());
    }
    catch (HttpRequestException ex)
    {
        AnsiConsole.MarkupLine($"[red]GitHub: {Markup.Escape(ex.Message)}[/] [grey](set GITHUB_TOKEN if this is a rate limit)[/]");
    }
}

// Downloads the area labels and ~2,000 recent issues once; commit the resulting training-data.json.
async Task LearnAsync()
{
    var areaLabels = (await GetAllAsync<Label>($"repos/{Repo}/labels?per_page=100", 10))
        .Where(l => l.Name.StartsWith("area-") && l.Name != "area-runtime" && !l.Name.StartsWith("area-unified-build"))
        .ToDictionary(l => l.Name, l => l.Description?.Replace("Includes:", "").Trim() ?? "");
    var issues = (await GetAllAsync<Issue>($"repos/{Repo}/issues?state=all&sort=created&per_page=100", 20))
        .Where(i => i.PullRequest is null && i.User.Type == "User")
        .Select(i => (Issue: i, Areas: i.Labels.Where(l => areaLabels.ContainsKey(l.Name)).ToList()))
        .Where(x => x.Areas.Count == 1)
        .Select(x => new TrainingIssue(x.Issue.Number, x.Issue.Title, x.Areas[0].Name))
        .ToList();
    File.WriteAllText(dataFile, JsonSerializer.Serialize(new TrainingData(areaLabels, issues), json));
    AnsiConsole.MarkupLine($"[green]✓[/] Saved {issues.Count} labeled issues (#{issues[^1].Number} to #{issues[0].Number}) to {dataFile}");
}

// Follows GitHub's Link header: past page 10, plain page numbers are rejected for anonymous callers.
async Task<List<T>> GetAllAsync<T>(string url, int maxPages)
{
    var all = new List<T>();
    for (string? next = url; next is not null && maxPages-- > 0;)
    {
        using var response = await http.GetAsync(next);
        response.EnsureSuccessStatusCode();
        all.AddRange((await response.Content.ReadFromJsonAsync<List<T>>(json))!);
        next = response.Headers.TryGetValues("Link", out var link)
            && Regex.Match(string.Join(",", link), @"<([^>]+)>;\s*rel=""next""") is { Success: true } m ? m.Groups[1].Value : null;
    }
    return all;
}

// Each area's option text becomes the title words that occur mostly in that area; its prior is how often it occurs.
static (Dictionary<string, string> Options, Dictionary<string, double> Prior) Profile(TrainingData data)
{
    HashSet<string> stopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "not", "when", "should", "can", "cannot", "does", "doesn", "this", "that",
        "after", "before", "into", "via", "using", "use", "add", "support", "allow", "make", "fix", "update", "remove",
        "new", "api", "proposal", "design", "issue", "bug", "error", "exception", "fails", "failure", "net", "dotnet",
        "aspnetcore", "asp", "core", "microsoft", "aspnet", "how", "why", "what", "all", "only", "same", "instead",
        "still", "but", "has", "have", "get", "set", "while", "throws", "missing", "wrong", "incorrect", "its",
    };
    var counts = data.Issues
        .SelectMany(i => Regex.Matches(i.Title, @"[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*")
            .Select(m => m.Value).Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(word => (Word: word, i.Area)))
        .GroupBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Area).ToDictionary(a => a.Key, a => a.Count()));

    string Short(string area) => area["area-".Length..];
    var options = data.Areas.ToDictionary(a => Short(a.Key), a =>
    {
        var words = counts
            .Select(c => (Word: c.Key, InArea: c.Value.GetValueOrDefault(a.Key), Total: c.Value.Values.Sum()))
            .Where(c => c.InArea >= 2 && c.InArea >= 0.6 * c.Total)
            .OrderByDescending(c => c.InArea * c.InArea / (double)c.Total)
            .Take(6)
            .Select(c => c.Word)
            .ToList();
        return words.Count > 0 ? string.Join(", ", words) : a.Value;
    });
    var prior = data.Areas.Keys.ToDictionary(Short, a => (data.Issues.Count(i => i.Area == a) + 1.0) / (data.Issues.Count + data.Areas.Count));
    return (options, prior);
}

static Table Scores(string title, IReadOnlyDictionary<string, double> probabilities, string prefix)
{
    var table = new Table().RoundedBorder().Title($"[bold]{title}[/]").AddColumn("Label").AddColumn("Probability").AddColumn("");
    foreach (var (name, p) in probabilities.OrderByDescending(x => x.Value).Take(5))
    {
        var filled = (int)Math.Round(p * 20);
        var color = p >= 0.7 ? "green" : p >= 0.4 ? "yellow" : "grey";
        table.AddRow(Markup.Escape(prefix + name), $"[{color}]{new string('█', filled)}[/][grey23]{new string('░', 20 - filled)}[/]", $"{p:P0}");
    }
    return table;
}

record Label(string Name, string? Description);
record User(string Type);
record Issue(int Number, string Title, string? Body, string HtmlUrl, List<Label> Labels, User User, JsonElement? PullRequest);
record TrainingIssue(int Number, string Title, string Area);
record TrainingData(Dictionary<string, string> Areas, List<TrainingIssue> Issues);
