using WinFix.System;

namespace WinFixUsb.System;

/// <summary>Records every call and touches nothing; backs --dry-run and the unit tests.</summary>
public sealed class FakeMacSystem : IMacSystem
{
    private const string ListExternalDisks = "list -plist external physical";

    private readonly List<string> _calls = [];
    private readonly Queue<string> _answers = [];

    public IReadOnlyList<string> Calls => _calls;

    /// <summary>In-memory file system keyed by path.</summary>
    public Dictionary<string, string> Files { get; } = [];

    /// <summary>The external disks a Mac with one USB stick plugged in would list.</summary>
    public string ExternalDisks { get; init; } =
        "/dev/disk4 (external, physical): SanDisk Ultra 32.0 GB" + "\n" +
        "/dev/disk5 (external, physical): Samsung T7 500.1 GB";

    /// <summary>Lines AskAsync returns in order; null once they run out.</summary>
    public IReadOnlyList<string> Answers
    {
        init
        {
            foreach (var answer in value)
            {
                _answers.Enqueue(answer);
            }
        }
    }

    public Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        _calls.Add($"RunCommand {fileName} {arguments}".TrimEnd());
        var isDiskList = fileName == "diskutil" && arguments == ListExternalDisks;
        return Task.FromResult(new CommandResult(0, isDiskList ? ExternalDisks : string.Empty));
    }

    public string? ReadFile(string path)
    {
        _calls.Add($"ReadFile {path}");
        return Files.GetValueOrDefault(path);
    }

    public void WriteFile(string path, string content)
    {
        _calls.Add($"WriteFile {path}");
        Files[path] = content;
    }

    public Task<string?> AskAsync(string prompt, CancellationToken ct)
    {
        _calls.Add($"Ask {prompt}");
        return Task.FromResult(_answers.TryDequeue(out var answer) ? answer : null);
    }
}
