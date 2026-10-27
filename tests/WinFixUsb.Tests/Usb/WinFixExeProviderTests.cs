using System.Net;
using WinFixUsb.System;
using WinFixUsb.Usb;

namespace WinFixUsb.Tests.Usb;

public class WinFixExeProviderTests
{
    private const string ToolFolder = "/opt/winfixusb";
    private const string BesideTool = ToolFolder + "/WinFix.exe";
    private const string Temp = "/tmp/WinFixUsb/WinFix.exe";
    private static readonly byte[] Exe = [0x4D, 0x5A, 1, 2, 3, 4];

    private static (WinFixExeProvider Provider, FakeHandler Handler, FakeMacSystem Fake, MemoryFiles Files, StringWriter Output) Rig(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null, bool beside = false)
    {
        var handler = new FakeHandler(respond ?? (_ => FakeHandler.Bytes(Exe)));
        var fake = new FakeMacSystem();
        if (beside)
        {
            fake.FileSizes[BesideTool] = 100;
        }

        var files = new MemoryFiles();
        return (new WinFixExeProvider(fake, new HttpClient(handler), files, ToolFolder, Temp), handler, fake, files, new StringWriter());
    }

    [Fact]
    public async Task Uses_the_exe_beside_the_tool_without_any_download()
    {
        var rig = Rig(beside: true);

        var path = await rig.Provider.StageAsync(rig.Output, CancellationToken.None);

        Assert.Equal(BesideTool, path);
        Assert.Empty(rig.Handler.Requests);
        Assert.Empty(rig.Files.Data);
    }

    [Fact]
    public async Task Downloads_the_latest_release_asset_when_none_is_beside_the_tool()
    {
        var rig = Rig();

        var path = await rig.Provider.StageAsync(rig.Output, CancellationToken.None);

        Assert.Equal(Temp, path);
        Assert.Equal(Exe, rig.Files.Data[Temp].ToArray());
        Assert.Equal(
            new Uri("https://github.com/ckandrinirina/ck-winfix/releases/latest/download/WinFix.exe"),
            Assert.Single(rig.Handler.Requests));
    }

    [Fact]
    public async Task A_missing_release_gives_null_a_warning_and_no_leftover_file()
    {
        var rig = Rig(_ => FakeHandler.Status(HttpStatusCode.NotFound));

        var path = await rig.Provider.StageAsync(rig.Output, CancellationToken.None);

        Assert.Null(path);
        Assert.Contains("Warning", rig.Output.ToString());
        Assert.Contains("WinFix.exe", rig.Output.ToString());
        Assert.Empty(rig.Files.Data);
    }

    [Fact]
    public async Task A_network_failure_gives_null_and_a_warning()
    {
        var rig = Rig(_ => throw new HttpRequestException("offline"));

        var path = await rig.Provider.StageAsync(rig.Output, CancellationToken.None);

        Assert.Null(path);
        Assert.Contains("Warning", rig.Output.ToString());
        Assert.Empty(rig.Files.Data);
    }

    [Fact]
    public async Task An_empty_download_is_refused()
    {
        var rig = Rig(_ => FakeHandler.Bytes([]));

        var path = await rig.Provider.StageAsync(rig.Output, CancellationToken.None);

        Assert.Null(path);
        Assert.Empty(rig.Files.Data);
    }

    [Fact]
    public async Task Cleanup_deletes_the_downloaded_file_and_touches_nothing_else()
    {
        var rig = Rig();
        await rig.Provider.StageAsync(rig.Output, CancellationToken.None);

        rig.Provider.Cleanup();

        Assert.Empty(rig.Files.Data);
        Assert.DoesNotContain(rig.Fake.Calls, c => c.StartsWith("RunCommand "));
    }
}
