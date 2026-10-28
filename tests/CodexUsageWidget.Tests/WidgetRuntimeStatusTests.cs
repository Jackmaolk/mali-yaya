using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class WidgetRuntimeStatusTests : IDisposable
{
    private const long ResetsAt = 1800000000;
    private static readonly DateTimeOffset ConfirmedAt =
        DateTimeOffset.FromUnixTimeSeconds(ResetsAt - 18000);
    private readonly TempPath _path = new();

    [Fact]
    public void NewCheckpointTakesPriorityOverLegacyWindowAndUpdateTime()
    {
        var record = NewRecord();
        record["fiveHourResetsAt"] = ResetsAt + 18000;
        record["updatedAtUtc"] = JsonValue.Create(ConfirmedAt.AddHours(1));

        Assert.Equal(new CountdownCheckpoint(ResetsAt, ConfirmedAt), Read(record.ToJsonString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyCheckpointCanBeRestoredEvenAfterShutdown(bool running)
    {
        var json = JsonSerializer.Serialize(new
        {
            fiveHourResetsAt = ResetsAt,
            updatedAtUtc = ConfirmedAt,
            running
        });

        Assert.Equal(new CountdownCheckpoint(ResetsAt, ConfirmedAt), Read(json));
    }

    [Fact]
    public void ShutdownDoesNotReplaceTheConfirmedAnchorWithTheUpdateTime()
    {
        var record = NewRecord();
        record["running"] = false;
        record["updatedAtUtc"] = JsonValue.Create(ConfirmedAt.AddHours(2));

        Assert.Equal(new CountdownCheckpoint(ResetsAt, ConfirmedAt), Read(record.ToJsonString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrNullConfirmationFallsBackToUpdateTime(bool explicitNull)
    {
        var record = NewRecord();
        if (explicitNull) record["clockConfirmedAtUtc"] = null;
        else record.Remove("clockConfirmedAtUtc");

        Assert.Equal(new CountdownCheckpoint(ResetsAt, ConfirmedAt), Read(record.ToJsonString()));
    }

    [Fact]
    public void ExplicitNullClockClearsEvenAValidLegacyCheckpoint()
    {
        var record = NewRecord();
        record["clockResetsAt"] = null;
        record["fiveHourResetsAt"] = ResetsAt;

        Assert.Null(Read(record.ToJsonString()));
    }

    [Fact]
    public void ExpiredCheckpointIsPreservedWithoutAddingAnotherFiveHours()
    {
        var expiredAt = DateTimeOffset.UnixEpoch;
        var confirmedAt = expiredAt.AddHours(-5);
        var json = JsonSerializer.Serialize(new
        {
            clockResetsAt = expiredAt.ToUnixTimeSeconds(),
            clockConfirmedAtUtc = confirmedAt,
            updatedAtUtc = DateTimeOffset.UtcNow
        });

        Assert.Equal(new CountdownCheckpoint(0, confirmedAt), Read(json));
    }

    [Theory]
    [InlineData(-62135596800L)]
    [InlineData(253402300799L)]
    public void ValidUnixTimestampBoundariesAreAccepted(long resetsAt)
    {
        var record = NewRecord();
        record["clockResetsAt"] = resetsAt;

        Assert.Equal(new CountdownCheckpoint(resetsAt, ConfirmedAt), Read(record.ToJsonString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    public void MalformedJsonOrMissingFieldsReturnNull(string json)
    {
        Assert.Null(Read(json));
    }

    [Theory]
    [InlineData("\"1800000000\"")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("253402300800")]
    [InlineData("-62135596801")]
    [InlineData("1e1000")]
    public void InvalidResetTypeOrRangeCannotFallBackToLegacy(string resetJson)
    {
        var record = NewRecord();
        record["clockResetsAt"] = JsonNode.Parse(resetJson);
        record["fiveHourResetsAt"] = ResetsAt;

        Assert.Null(Read(record.ToJsonString()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"1800000000\"")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("253402300800")]
    [InlineData("-62135596801")]
    public void InvalidLegacyResetReturnsNull(string resetJson)
    {
        var record = NewRecord();
        record.Remove("clockResetsAt");
        record["fiveHourResetsAt"] = JsonNode.Parse(resetJson);

        Assert.Null(Read(record.ToJsonString()));
    }

    [Theory]
    [InlineData("1800000000")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"not-a-date\"")]
    [InlineData("\"2026-02-30T00:00:00Z\"")]
    [InlineData("\"10000-01-01T00:00:00Z\"")]
    [InlineData("\"0000-01-01T00:00:00Z\"")]
    [InlineData("\"2026-10-27T00:00:00+25:00\"")]
    public void InvalidConfirmationCannotFallBackToValidUpdateTime(string timeJson)
    {
        var record = NewRecord();
        record["clockConfirmedAtUtc"] = JsonNode.Parse(timeJson);

        Assert.Null(Read(record.ToJsonString()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1800000000")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("\"not-a-date\"")]
    [InlineData("\"10000-01-01T00:00:00Z\"")]
    public void InvalidFallbackTimeReturnsNull(string timeJson)
    {
        var record = NewRecord();
        record.Remove("clockConfirmedAtUtc");
        record["updatedAtUtc"] = JsonNode.Parse(timeJson);

        Assert.Null(Read(record.ToJsonString()));
    }

    [Fact]
    public void MissingConfirmationAndUpdateTimeReturnNull()
    {
        var record = NewRecord();
        record.Remove("clockConfirmedAtUtc");
        record.Remove("updatedAtUtc");

        Assert.Null(Read(record.ToJsonString()));
    }

    [Fact]
    public void MissingFileReturnsNullWithoutCreatingAnything()
    {
        Assert.Null(WidgetRuntimeStatus.ReadCountdownCheckpoint(_path.FilePath));
        Assert.Empty(Directory.GetFileSystemEntries(_path.DirectoryPath));
    }

    [Fact]
    public void ReadsDoNotRewriteTheCheckpoint()
    {
        var json = NewRecord().ToJsonString();

        Assert.NotNull(Read(json));
        Assert.Equal(json, File.ReadAllText(_path.FilePath));
        Assert.Single(Directory.GetFileSystemEntries(_path.DirectoryPath));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void TwoMiBReadLimitIsInclusive(int extraBytes, bool accepted)
    {
        var json = NewRecord().ToJsonString();
        var padded = json + new string(' ', 2 * 1024 * 1024 + extraBytes - Encoding.UTF8.GetByteCount(json));

        Assert.Equal(accepted, Read(padded) is not null);
    }

    [Fact]
    public void LockedFileReturnsNullInsteadOfThrowing()
    {
        File.WriteAllText(_path.FilePath, NewRecord().ToJsonString());
        using var locked = new FileStream(_path.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Null(WidgetRuntimeStatus.ReadCountdownCheckpoint(_path.FilePath));
    }

    [Fact]
    public void DirectoryPathReturnsNullInsteadOfThrowing()
    {
        Assert.Null(WidgetRuntimeStatus.ReadCountdownCheckpoint(_path.DirectoryPath));
    }

    [Fact]
    public void InvalidPathReturnsNullInsteadOfThrowing()
    {
        Assert.Null(WidgetRuntimeStatus.ReadCountdownCheckpoint(_path.FilePath + "\0"));
    }

    private static JsonObject NewRecord() => JsonSerializer.SerializeToNode(new
    {
        clockResetsAt = ResetsAt,
        clockConfirmedAtUtc = ConfirmedAt,
        updatedAtUtc = ConfirmedAt,
        running = true
    })!.AsObject();

    private CountdownCheckpoint? Read(string json)
    {
        File.WriteAllText(_path.FilePath, json);
        return WidgetRuntimeStatus.ReadCountdownCheckpoint(_path.FilePath);
    }

    public void Dispose() => _path.Dispose();

    private sealed class TempPath : IDisposable
    {
        private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexUsageWidget.Tests"));
        private readonly string _randomName = Guid.NewGuid().ToString("N");

        public string DirectoryPath { get; }
        public string FilePath => Path.Combine(DirectoryPath, "widget-runtime.json");

        public TempPath()
        {
            DirectoryPath = Path.Combine(_root, _randomName);
            Directory.CreateDirectory(DirectoryPath);
        }

        public void Dispose()
        {
            var directory = Path.GetFullPath(DirectoryPath);
            if (!string.Equals(Path.GetDirectoryName(directory), _root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(directory), _randomName, StringComparison.Ordinal) ||
                !Guid.TryParseExact(_randomName, "N", out _))
                throw new InvalidOperationException("Refusing to delete outside the random test directory.");

            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
