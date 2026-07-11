using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustMapsApi.V4.Assets;
using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Map.Assets;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MonumentIconSourceTests
{
    private static MonumentIconSource CreateSource() =>
        new(new MonumentAssetSource(), NullLogger<MonumentIconSource>.Instance);

    [Fact]
    public void Monument_rasterizes_known_token_at_requested_size()
    {
        var icon = CreateSource().Monument("launchsite", 30);

        Assert.NotNull(icon);
        Assert.Equal(30, icon!.Width);
        Assert.Equal(30, icon.Height);
    }

    [Fact]
    public void Monument_caches_per_type_and_size()
    {
        var source = CreateSource();

        Assert.Same(source.Monument("launchsite", 30), source.Monument("launchsite", 30));
        Assert.NotSame(source.Monument("launchsite", 30), source.Monument("launchsite", 24));
    }

    [Fact]
    public void Monument_returns_null_and_logs_once_for_unknown_token()
    {
        var logger = new RecordingLogger<MonumentIconSource>();
        var source = new MonumentIconSource(new MonumentAssetSource(), logger);

        Assert.Null(source.Monument("definitely_not_a_monument", 30));
        Assert.Null(source.Monument("definitely_not_a_monument", 30));
        Assert.Null(source.Monument("definitely_not_a_monument", 24));

        Assert.Equal(1, logger.Count(LogLevel.Information));
    }

    [Theory]
    [InlineData(RigKind.Small)]
    [InlineData(RigKind.Large)]
    public void Rig_resolves_for_known_kinds(RigKind kind) =>
        Assert.NotNull(CreateSource().Rig(kind, 30));

    [Fact]
    public void Every_known_token_rasterizes()
    {
        // SVG-engine smoke test across the whole mapping: every token must yield a real image.
        var source = CreateSource();
        foreach (var token in MonumentTokenMap.KnownTokens)
        {
            Assert.True(source.Monument(token, 30) is not null, $"{token} produced no icon");
        }
    }

    [Fact]
    public void Monument_caches_null_for_assetless_type_and_does_not_retry()
    {
        var assets = Substitute.For<IMonumentAssetSource>();
        assets.TryGetAsset(Arg.Any<MonumentType>(), out Arg.Any<MonumentAsset?>()).Returns(false);
        var logger = new RecordingLogger<MonumentIconSource>();
        var source = new MonumentIconSource(assets, logger);

        Assert.Null(source.Monument("launchsite", 30));
        Assert.Null(source.Monument("launchsite", 30));
        Assert.Null(source.Monument("launchsite", 30));

        Assert.Equal(1, logger.Count(LogLevel.Information));
        assets.Received(1).TryGetAsset(Arg.Any<MonumentType>(), out Arg.Any<MonumentAsset?>());
    }

    [Fact]
    public void Monument_logs_warning_once_when_rasterization_fails()
    {
        var bogus = CreateBogusAsset();
        var assets = Substitute.For<IMonumentAssetSource>();
        assets.TryGetAsset(Arg.Any<MonumentType>(), out Arg.Any<MonumentAsset?>()).Returns(call =>
        {
            call[1] = bogus;
            return true;
        });
        var logger = new RecordingLogger<MonumentIconSource>();
        var source = new MonumentIconSource(assets, logger);

        Assert.Null(source.Monument("launchsite", 30));
        Assert.Null(source.Monument("launchsite", 30));

        Assert.Equal(1, logger.Count(LogLevel.Warning));
        Assert.Equal(1, logger.Count(LogLevel.Information));
    }

    /// <summary>Builds a MonumentAsset whose OpenStream throws (no such embedded resource). The package's
    /// ctor is internal with no InternalsVisibleTo grant, so reflection is the only seam; the ctor does
    /// not validate its arguments, so a bogus asset name survives construction and fails on first read.</summary>
    private static MonumentAsset CreateBogusAsset()
    {
        var ctor = typeof(MonumentAsset).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, [typeof(MonumentType), typeof(string)]);
        Assert.NotNull(ctor);
        return (MonumentAsset)ctor!.Invoke([MonumentType.LaunchSite, "Definitely_Not_An_Asset"]);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogLevel> _entries = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Add(logLevel);

        public int Count(LogLevel level) => _entries.Count(l => l == level);
    }
}
