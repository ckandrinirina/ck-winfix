using System.Text.Json;
using WinFix.System;

namespace WinFix.Pipeline;

/// <summary>Progress that survives a reboot: NextStage is the index of the first repair step still to run.</summary>
public sealed record RunState(int NextStage)
{
    /// <summary>The real system expands %ProgramData%.</summary>
    public const string FilePath = @"%ProgramData%\WinFix\state.json";

    public const string RunOnceKey = @"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce";

    public const string RunOnceName = "WinFix";

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Null when the text is not a valid state.</summary>
    public static RunState? Parse(string? json)
    {
        try
        {
            var state = json is null ? null : JsonSerializer.Deserialize<RunState>(json);
            return state is { NextStage: >= 0 } ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static RunState? Load(ISystem system) => Parse(system.ReadFile(FilePath));

    public void Save(ISystem system)
    {
        system.WriteFile(FilePath, ToJson());
        system.WriteRegistryValue(RunOnceKey, RunOnceName, $"\"{system.ExecutablePath}\" --resume");
    }

    public static void Clear(ISystem system)
    {
        system.DeleteFile(FilePath);
        system.DeleteRegistryValue(RunOnceKey, RunOnceName);
    }
}
