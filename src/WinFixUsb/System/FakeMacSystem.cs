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

    /// <summary>The `diskutil list -plist external physical` output of a Mac with two USB disks plugged in.</summary>
    public string ExternalDisks { get; init; } =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0"><dict><key>AllDisksAndPartitions</key><array>
        <dict><key>DeviceIdentifier</key><string>disk4</string><key>Size</key><integer>32000000000</integer><key>MediaName</key><string>SanDisk Ultra</string></dict>
        <dict><key>DeviceIdentifier</key><string>disk5</string><key>Size</key><integer>500107862016</integer><key>MediaName</key><string>Samsung T7</string></dict>
        </array></dict></plist>
        """;

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
