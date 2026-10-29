using System.Text;
using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.ConfigFiles;

public sealed class ConfigWriterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ConfigWriter _writer = new(new BackupManager(), NullLogger<ConfigWriter>.Instance);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Creates_a_new_file_without_a_backup()
    {
        var path = _temp.Combine(".ssh", "config");
        var change = new ConfigChange(path, null, new TextFileContent("Host a\n"), "ssh config");

        var applied = _writer.Apply(new ConfigChangeSet([change]));

        Assert.Equal("Host a\n", File.ReadAllText(path));
        Assert.Equal(ConfigChangeKind.Create, Assert.Single(applied).Kind);
        Assert.Null(applied[0].BackupPath);
    }

    [Fact]
    public void Modifies_a_file_after_backing_up_the_previous_content()
    {
        var path = _temp.Combine(".gitconfig");
        File.WriteAllText(path, "old\n");
        var change = new ConfigChange(path, new TextFileContent("old\n"), new TextFileContent("new\n"), "gitconfig");

        var applied = Assert.Single(_writer.Apply(new ConfigChangeSet([change])));

        Assert.Equal("new\n", File.ReadAllText(path));
        Assert.NotNull(applied.BackupPath);
        Assert.Equal("old\n", File.ReadAllText(applied.BackupPath));
    }

    [Fact]
    public void Deletes_a_file_after_backing_it_up()
    {
        var path = _temp.Combine(".gitconfig-old");
        File.WriteAllText(path, "[user]\n");
        var change = new ConfigChange(path, new TextFileContent("[user]\n"), null, "profile config");

        var applied = Assert.Single(_writer.Apply(new ConfigChangeSet([change])));

        Assert.False(File.Exists(path));
        Assert.Equal("[user]\n", File.ReadAllText(applied.BackupPath!));
    }

    [Fact]
    public void Refuses_to_write_anything_when_any_file_changed_since_the_preview()
    {
        var first = _temp.Combine("first");
        var second = _temp.Combine("second");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "edited by the user meanwhile");
        var changes = new ConfigChangeSet(
        [
            new ConfigChange(first, new TextFileContent("a"), new TextFileContent("A"), "first"),
            new ConfigChange(second, new TextFileContent("b"), new TextFileContent("B"), "second"),
        ]);

        var ex = Assert.Throws<ConfigConflictException>(() => _writer.Apply(changes));

        Assert.Equal(second, ex.FilePath);
        Assert.Equal("a", File.ReadAllText(first));
        Assert.Empty(BackupManager.ListBackups(first));
    }

    [Fact]
    public void Treats_a_file_created_since_the_preview_as_a_conflict()
    {
        var path = _temp.Combine("config");
        var change = new ConfigChange(path, null, new TextFileContent("x"), "config");
        File.WriteAllText(path, "appeared");

        Assert.Throws<ConfigConflictException>(() => _writer.Apply(new ConfigChangeSet([change])));
        Assert.Equal("appeared", File.ReadAllText(path));
    }

    [Fact]
    public void Preserves_a_utf8_bom()
    {
        var path = _temp.Combine(".gitconfig");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, (byte)'a']);
        var original = TextFile.ReadIfExists(path)!;

        _writer.Apply(new ConfigChangeSet([new ConfigChange(path, original, original with { Text = "b" }, "x")]));

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'b' }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Skips_changes_that_do_not_change_anything()
    {
        var path = _temp.Combine("same");
        File.WriteAllText(path, "same");
        var content = new TextFileContent("same");

        Assert.Empty(_writer.Apply(new ConfigChangeSet([new ConfigChange(path, content, content, "x")])));
        Assert.Empty(BackupManager.ListBackups(path));
    }

    [Fact]
    public void Reading_a_file_that_is_not_utf8_fails_instead_of_mangling_it()
    {
        var path = _temp.Combine("latin1");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("name = Gülər ç"));

        Assert.Throws<DecoderFallbackException>(() => TextFile.ReadIfExists(path));
    }
}
