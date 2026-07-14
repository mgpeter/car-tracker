using CarTracker.Domain;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Domain.Tests;

public sealed class ClockTests
{
    private static Clock At(string utcInstant) =>
        new(new FakeTimeProvider(DateTimeOffset.Parse(utcInstant, null, System.Globalization.DateTimeStyles.RoundtripKind)));

    [Fact]
    public void Resolves_a_plain_utc_instant_to_the_same_local_date()
    {
        Assert.Equal(new DateOnly(2026, 7, 14), At("2026-07-14T10:00:00Z").Today());
    }

    /// <summary>
    /// The reason this type exists. 23:30 UTC on 13 July is 00:30 on 14 July in London, because BST is
    /// UTC+1 in summer. Computing "today" in UTC would answer 13 July, and every day-countdown on the
    /// Dashboard would be off by one for the last hour of every summer day.
    /// </summary>
    [Fact]
    public void Resolves_a_late_evening_utc_instant_to_the_next_day_during_BST()
    {
        Assert.Equal(new DateOnly(2026, 7, 14), At("2026-07-13T23:30:00Z").Today());
    }

    [Fact]
    public void Resolves_a_late_evening_utc_instant_to_the_same_day_during_GMT()
    {
        // Same clock time in winter: London is UTC+0, so 13 January stays 13 January.
        Assert.Equal(new DateOnly(2026, 1, 13), At("2026-01-13T23:30:00Z").Today());
    }

    [Theory]
    // The 2026 BST transitions: forward 29 March 01:00 UTC, back 25 October 02:00 UTC.
    [InlineData("2026-03-29T00:59:00Z", 2026, 3, 29)] // GMT, minutes before the spring-forward
    [InlineData("2026-03-29T01:00:00Z", 2026, 3, 29)] // BST begins; local jumps to 02:00, date unchanged
    [InlineData("2026-10-25T01:59:00Z", 2026, 10, 25)] // BST, minutes before the fall-back
    [InlineData("2026-10-25T02:00:00Z", 2026, 10, 25)] // GMT resumes
    [InlineData("2026-12-31T23:59:00Z", 2026, 12, 31)] // GMT, year boundary
    [InlineData("2026-06-30T23:00:00Z", 2026, 7, 1)] // BST: midnight local, so already July
    public void Resolves_correctly_across_the_BST_transitions(string utcInstant, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), At(utcInstant).Today());
    }

    [Fact]
    public void Reference_date_matches_the_workbook()
    {
        // Every figure in the spec is stated at this reference date; a test suite that cannot pin "now"
        // cannot assert any of them.
        Assert.Equal(new DateOnly(2026, 7, 14), At("2026-07-14T21:50:00Z").Today());
    }
}
