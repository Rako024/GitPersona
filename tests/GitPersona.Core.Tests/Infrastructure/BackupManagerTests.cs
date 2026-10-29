using GitPersona.Core.Infrastructure;

namespace GitPersona.Core.Tests.Infrastructure;

public sealed class BackupManagerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 29, 15, 30, 12, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Names_backups_with_a_timestamp_next_to_the_file()
    {
        var path = _temp.Combine("config");
        File.WriteAllText(path, "content");

        var backup = new BackupManager(_clock).Backup(path);

        Assert.Equal(path + ".20261029-153012.bak", backup);
        Assert.Equal("content", File.ReadAllText(backup!));
    }

    [Fact]
    public void Returns_null_when_there_is_nothing_to_back_up()
    {
        Assert.Null(new BackupManager(_clock).Backup(_temp.Combine("missing")));
    }

    [Fact]
    public void Adds_a_counter_when_two_backups_happen_in_the_same_second()
    {
        var path = _temp.Combine("config");
        File.WriteAllText(path, "v1");
        var manager = new BackupManager(_clock);

        manager.Backup(path);
        File.WriteAllText(path, "v2");
        var second = manager.Backup(path);

        Assert.Equal(path + ".20261029-153012-2.bak", second);
        Assert.Equal([second!, path + ".20261029-153012.bak"], BackupManager.ListBackups(path));
    }

    [Fact]
    public void Keeps_only_the_newest_backups_and_never_touches_other_files()
    {
        var path = _temp.Combine("config");
        var unrelated = _temp.Combine("config.mine.bak");
        File.WriteAllText(path, "x");
        File.WriteAllText(unrelated, "keep me");
        var manager = new BackupManager(_clock, retention: 2);

        for (var minute = 0; minute < 4; minute++)
        {
            _clock.Now = _clock.Now.AddMinutes(1);
            manager.Backup(path);
        }

        Assert.Equal(
            [path + ".20261029-153412.bak", path + ".20261029-153312.bak"],
            BackupManager.ListBackups(path));
        Assert.True(File.Exists(unrelated));
    }
}
