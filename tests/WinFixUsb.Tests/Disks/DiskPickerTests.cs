using WinFixUsb.Disks;
using WinFixUsb.System;

namespace WinFixUsb.Tests.Disks;

public class DiskPickerTests
{
    private const string PlugIn = "Plug in a USB drive of at least 8 GB and run again.";

    private static FakeMacSystem TwoDisks(params string[] answers) => new()
    {
        ExternalDisks = DiskFixtures.Plist(
            DiskFixtures.Internal("disk0", 500_000_000_000),
            DiskFixtures.Usb("disk4", 32_000_000_000, "SanDisk Ultra"),
            DiskFixtures.Usb("disk6", 4_000_000_000, "Tiny Stick")),
        Answers = answers,
    };

    private static async Task<(Disk? Picked, string Text, FakeMacSystem Fake)> PickAsync(
        FakeMacSystem fake, string? requested = null)
    {
        var output = new StringWriter();
        var picked = await DiskPicker.PickAsync(fake, requested, output, CancellationToken.None);
        return (picked, output.ToString(), fake);
    }

    [Fact]
    public async Task Shows_each_disks_identifier_name_and_size()
    {
        var (_, text, _) = await PickAsync(TwoDisks("disk4"));

        Assert.Contains(text.Split('\n'), l => l.Contains("disk4") && l.Contains("SanDisk Ultra") && l.Contains("32.0 GB"));
    }

    [Fact]
    public async Task Never_shows_an_internal_disk()
    {
        var (_, text, _) = await PickAsync(TwoDisks("disk4"));

        Assert.DoesNotContain("disk0", text);
    }

    [Fact]
    public async Task Lists_a_small_disk_as_too_small()
    {
        var (_, text, _) = await PickAsync(TwoDisks("disk4"));

        Assert.Contains(text.Split('\n'), l => l.Contains("disk6") && l.Contains("too small"));
    }

    [Fact]
    public async Task Typing_the_identifier_picks_the_disk()
    {
        var (picked, _, _) = await PickAsync(TwoDisks("disk4"));

        Assert.Equal("disk4", picked?.Identifier);
    }

    [Fact]
    public async Task A_too_small_disk_cannot_be_chosen_even_when_typed()
    {
        var (picked, text, _) = await PickAsync(TwoDisks("disk6"));

        Assert.Null(picked);
        Assert.Contains("Nothing was erased.", text);
    }

    [Theory]
    [InlineData("disk5")]
    [InlineData("disk0")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("DISK4")]
    public async Task Any_other_answer_aborts(string answer)
    {
        var (picked, text, _) = await PickAsync(TwoDisks(answer));

        Assert.Null(picked);
        Assert.Contains("Nothing was erased.", text);
    }

    [Fact]
    public async Task Ended_input_aborts()
    {
        var (picked, text, _) = await PickAsync(TwoDisks());

        Assert.Null(picked);
        Assert.Contains("Nothing was erased.", text);
    }

    [Fact]
    public async Task Surrounding_spaces_in_the_typed_identifier_are_ignored()
    {
        var (picked, _, _) = await PickAsync(TwoDisks("  disk4 "));

        Assert.Equal("disk4", picked?.Identifier);
    }

    [Fact]
    public async Task A_requested_disk_is_preselected_but_still_asks_for_the_typed_confirmation()
    {
        var (picked, text, fake) = await PickAsync(TwoDisks("disk4"), requested: "disk4");

        Assert.Equal("disk4", picked?.Identifier);
        Assert.Single(fake.Calls, c => c.StartsWith("Ask "));
        Assert.Contains("SanDisk Ultra", fake.Calls.Single(c => c.StartsWith("Ask ")) + text);
    }

    [Fact]
    public async Task A_requested_disk_is_not_erased_when_the_confirmation_is_wrong()
    {
        var (picked, text, _) = await PickAsync(TwoDisks("yes"), requested: "disk4");

        Assert.Null(picked);
        Assert.Contains("Nothing was erased.", text);
    }

    [Theory]
    [InlineData("disk0")]
    [InlineData("disk99")]
    [InlineData("disk6")]
    public async Task A_requested_internal_unknown_or_too_small_disk_is_refused_without_asking(string requested)
    {
        var (picked, text, fake) = await PickAsync(TwoDisks("disk4", requested), requested);

        Assert.Null(picked);
        Assert.Contains(requested, text);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("Ask "));
    }

    [Fact]
    public async Task No_external_disk_prints_the_plug_in_message_and_asks_nothing()
    {
        var fake = DiskFixtures.Fake(DiskFixtures.Internal("disk0", 500_000_000_000));

        var (picked, text, _) = await PickAsync(fake);

        Assert.Null(picked);
        Assert.Contains(PlugIn, text);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("Ask "));
    }

    [Fact]
    public async Task Only_too_small_disks_print_the_plug_in_message_too()
    {
        var fake = DiskFixtures.Fake(DiskFixtures.Usb("disk6", 4_000_000_000, "Tiny Stick"));

        var (picked, text, _) = await PickAsync(fake);

        Assert.Null(picked);
        Assert.Contains("too small", text);
        Assert.Contains(PlugIn, text);
    }

    [Fact]
    public async Task No_external_disk_with_a_requested_disk_prints_the_plug_in_message()
    {
        var fake = DiskFixtures.Fake();

        var (picked, text, _) = await PickAsync(fake, requested: "disk4");

        Assert.Null(picked);
        Assert.Contains(PlugIn, text);
    }
}
