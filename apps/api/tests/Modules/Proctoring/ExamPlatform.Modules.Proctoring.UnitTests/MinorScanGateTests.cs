using ExamPlatform.Modules.Proctoring.Domain;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>The minors gate: with the switch off, attempts by under-18 candidates are left out and counted; with it on, all are scanned (section 7.2).</summary>
public class MinorScanGateTests
{
    private static readonly MinorScanPolicy Off = new(MinorsScanEnabled: false);
    private static readonly MinorScanPolicy On = new(MinorsScanEnabled: true);

    [Fact]
    public void WithTheSwitchOff_AnAttemptByAMinor_IsLeftOutAndCounted()
    {
        var adult = Guid.NewGuid();
        var minor = Guid.NewGuid();

        var selection = MinorScanGate.Select([adult, minor], new HashSet<Guid> { minor }, Off);

        Assert.Equal([adult], selection.ToScan);
        Assert.Equal(1, selection.ExcludedUnder18);
    }

    [Fact]
    public void WithTheSwitchOn_AnAttemptByAMinor_IsScanned()
    {
        var adult = Guid.NewGuid();
        var minor = Guid.NewGuid();

        var selection = MinorScanGate.Select([adult, minor], new HashSet<Guid> { minor }, On);

        Assert.Equal([adult, minor], selection.ToScan);
        Assert.Equal(0, selection.ExcludedUnder18);
    }

    [Fact]
    public void WithTheSwitchOff_WhenEveryAttemptIsByAMinor_NothingIsScanned_AndAllAreCounted()
    {
        var attempts = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var selection = MinorScanGate.Select(attempts, attempts.ToHashSet(), Off);

        Assert.Empty(selection.ToScan);
        Assert.Equal(2, selection.ExcludedUnder18);
    }

    [Fact]
    public void WithTheSwitchOff_WhenNoAttemptIsByAMinor_ExcludesNothing()
    {
        var attempts = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var selection = MinorScanGate.Select(attempts, new HashSet<Guid>(), Off);

        Assert.Equal(attempts, selection.ToScan);
        Assert.Equal(0, selection.ExcludedUnder18);
    }

    [Fact]
    public void AMinorAttemptOutsideTheList_IsNotCountedAsExcluded()
    {
        // The count is of the attempts in the scope, not of every id the age check happened to name.
        var adult = Guid.NewGuid();

        var selection = MinorScanGate.Select([adult], new HashSet<Guid> { Guid.NewGuid() }, Off);

        Assert.Equal([adult], selection.ToScan);
        Assert.Equal(0, selection.ExcludedUnder18);
    }

    [Fact]
    public void TheSwitchDefaultsToOff() =>
        Assert.False(new ExamPlatform.Modules.Proctoring.Endpoints.ProctoringOptions().MinorsScanEnabled);
}
