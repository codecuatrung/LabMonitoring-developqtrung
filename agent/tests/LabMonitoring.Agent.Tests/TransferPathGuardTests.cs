using System.Diagnostics;
using System.IO.Compression;
using FluentAssertions;
using LabMonitoring.Agent.Commands;
using LabMonitoring.Agent.Protocol;

namespace LabMonitoring.Agent.Tests;

public sealed class TransferPathGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lm-transfer-" + Guid.NewGuid().ToString("N"));

    public TransferPathGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private AgentConfigResponse ConfigWith(params TransferFolderDto[] folders) => new() { TransferFolders = folders };

    [Fact]
    public void Folder_in_synced_list_with_matching_path_is_allowed()
    {
        var config = ConfigWith(new TransferFolderDto(1, _root, "Push"));

        TransferPathGuard.TryResolveFolder(1, _root, "Push", config, out var full, out var error).Should().BeTrue(error);
        full.Should().Be(Path.GetFullPath(_root));
    }

    [Fact]
    public void Folder_is_rejected_when_config_has_not_synced()
    {
        TransferPathGuard.TryResolveFolder(1, _root, "Push", null, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Folder_is_rejected_for_the_wrong_direction()
    {
        var config = ConfigWith(new TransferFolderDto(1, _root, "Collect"));
        TransferPathGuard.TryResolveFolder(1, _root, "Push", config, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Folder_is_rejected_when_payload_path_differs_from_the_allowed_one()
    {
        var config = ConfigWith(new TransferFolderDto(1, _root, "Push"));
        TransferPathGuard.TryResolveFolder(1, @"C:\Windows\System32", "Push", config, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"\\?\C:\x")]
    [InlineData(@"C:\a\..\Windows")]
    [InlineData(@"%NOT_DEFINED_ANYWHERE_123%\x")]
    [InlineData(@"relative\path")]
    public void Unsafe_paths_are_rejected(string path)
    {
        TransferPathGuard.TryExpand(path, out _, out var error).Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void Environment_variables_are_expanded()
    {
        TransferPathGuard.TryExpand(@"%PUBLIC%\Desktop", out var full, out var error).Should().BeTrue(error);
        full.Should().EndWith(@"\Desktop");
        full.Should().NotContain("%");
    }

    [Theory]
    [InlineData("baitap.docx", true)]
    [InlineData("de thi (1).pdf", true)]
    [InlineData("..\\evil.docx", false)]
    [InlineData("sub\\file.txt", false)]
    [InlineData("C:\\x.txt", false)]
    [InlineData("file.txt:stream", false)]
    [InlineData("CON.txt", false)]
    [InlineData("nul", false)]
    [InlineData("name.", false)]
    [InlineData("", false)]
    public void File_names_are_validated(string name, bool expected)
    {
        TransferPathGuard.IsSafeFileName(name, out _).Should().Be(expected);
    }

    [Fact]
    public void IsInside_rejects_siblings_that_only_share_a_prefix()
    {
        TransferPathGuard.IsInside(@"C:\Data\Lab", @"C:\Data\Lab\a.txt").Should().BeTrue();
        TransferPathGuard.IsInside(@"C:\Data\Lab", @"C:\Data\Lab2\a.txt").Should().BeFalse();
        TransferPathGuard.IsInside(@"C:\Data\Lab", @"C:\Data\Lab\..\Other\a.txt").Should().BeFalse();
    }

    [Fact]
    public void Junction_on_the_way_to_the_folder_is_rejected()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var target = Path.Combine(_root, "real");
        var link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            p.WaitForExit();
        Directory.Exists(link).Should().BeTrue("the junction must have been created for this test to mean anything");

        var config = ConfigWith(new TransferFolderDto(7, link, "Push"));
        TransferPathGuard.TryResolveFolder(7, link, "Push", config, out _, out var error).Should().BeFalse();
        error.Should().Contain("junction");
    }

    [Fact]
    public void BuildZip_collects_matching_files_recursively_and_respects_the_filter()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.docx"), "aaa");
        File.WriteAllText(Path.Combine(src, "sub", "b.docx"), "bbb");
        File.WriteAllText(Path.Combine(src, "c.txt"), "ccc");
        var zipPath = Path.Combine(_root, "out.zip");

        var (count, truncated) = FileTransferExecutor.BuildZip(src, zipPath, "*.docx", 10_000_000);

        count.Should().Be(2);
        truncated.Should().BeFalse();
        using var zip = ZipFile.OpenRead(zipPath);
        zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo("a.docx", "sub/b.docx");
    }

    [Fact]
    public void BuildZip_stops_at_the_size_cap_and_flags_truncation()
    {
        var src = Path.Combine(_root, "big");
        Directory.CreateDirectory(src);
        var random = new Random(1);
        foreach (var name in new[] { "1.bin", "2.bin", "3.bin" })
        {
            var bytes = new byte[400_000];
            random.NextBytes(bytes); // incompressible
            File.WriteAllBytes(Path.Combine(src, name), bytes);
        }
        var zipPath = Path.Combine(_root, "capped.zip");

        var (count, truncated) = FileTransferExecutor.BuildZip(src, zipPath, null, 900_000);

        truncated.Should().BeTrue();
        count.Should().BeLessThan(3);
        new FileInfo(zipPath).Length.Should().BeLessThan(1_000_000);
    }

    [Fact]
    public void BuildZip_reads_files_that_another_process_has_open()
    {
        var src = Path.Combine(_root, "open");
        Directory.CreateDirectory(src);
        var path = Path.Combine(src, "locked.docx");
        using var holder = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        holder.Write("still being edited"u8);
        holder.Flush();

        var (count, _) = FileTransferExecutor.BuildZip(src, Path.Combine(_root, "open.zip"), null, 1_000_000);

        count.Should().Be(1);
    }
}