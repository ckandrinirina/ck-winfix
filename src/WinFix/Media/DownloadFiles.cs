using System.Security.Cryptography;

namespace WinFix.Media;

/// <summary>The disk the ISO download writes to; a seam so tests stay in memory.</summary>
public interface IDownloadFiles
{
    /// <summary>0 when the file does not exist.</summary>
    long Length(string path);

    /// <summary>Creates missing folders; appends to the file when <paramref name="append"/>, else replaces it.</summary>
    Stream Open(string path, bool append);

    /// <summary>Replaces an existing <paramref name="to"/>.</summary>
    void Move(string from, string to);

    /// <summary>A missing file is not an error.</summary>
    void Delete(string path);

    string Sha256Hex(string path);
}

public sealed class DiskDownloadFiles : IDownloadFiles
{
    public long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    public Stream Open(string path, bool append)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
    }

    public void Move(string from, string to) => File.Move(from, to, overwrite: true);

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
