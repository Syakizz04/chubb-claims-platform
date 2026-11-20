using Claims.Contracts;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Insights.Api.Projection;
using static Insights.Tests.TestEvents;

namespace Insights.Tests;

public class ProjectionTests
{
    [Fact]
    public void Applying_a_stream_in_order_yields_the_final_state()
    {
        var snapshot = new ClaimSnapshot();
        foreach (var e in FullLifecycle()) Assert.True(ClaimProjection.Apply(snapshot, e));

        Assert.Equal("Settled", snapshot.Status);
        Assert.Equal(0m, snapshot.ReserveAmount);
        Assert.Equal(5, snapshot.LastSequence);
        Assert.False(snapshot.IsOpen);
    }

    [Fact]
    public void Older_event_never_overwrites_newer_state()
    {
        var events = FullLifecycle();
        var snapshot = new ClaimSnapshot();
        ClaimProjection.Apply(snapshot, events[3]); // approved
        Assert.False(ClaimProjection.Apply(snapshot, events[1])); // late "picked up" arrives
        Assert.Equal("Approved", snapshot.Status);
    }

    /// <summary>
    /// E-3 as a property: take a claim's event stream, then duplicate, drop-in-replay and shuffle it
    /// at random (what at-least-once delivery and consumer restarts can do). The projection must
    /// always end up identical to the in-order result.
    /// </summary>
    [Property(MaxTest = 300)]
    public Property Duplicates_and_reordering_never_change_the_outcome() =>
        Prop.ForAll(
            Gen.ArrayOf(Gen.Choose(0, 4)).ToArbitrary(),  // indices of events to redeliver
            Gen.Choose(0, int.MaxValue).ToArbitrary(),    // shuffle seed
            (redeliver, seed) =>
            {
                var events = FullLifecycle();
                var expected = new ClaimSnapshot();
                foreach (var e in events) ClaimProjection.Apply(expected, e);

                // Every event at least once, plus random redeliveries, in random order, but the
                // final event delivered last (per-partition ordering means the latest is never lost).
                var delivered = events.Take(4).Concat(redeliver.Select(i => events[i]))
                    .OrderBy(_ => new Random(seed).Next()).Append(events[4]).ToList();
                var actual = new ClaimSnapshot();
                foreach (var e in delivered) ClaimProjection.Apply(actual, e);

                return Same(expected, actual);
            });

    [Property(MaxTest = 300)]
    public Property Projection_always_reflects_the_highest_sequence_seen() =>
        Prop.ForAll(Gen.Shuffle(FullLifecycle().ToArray()).ToArbitrary(), shuffled =>
        {
            var s = new ClaimSnapshot();
            var highest = 0;
            foreach (var e in shuffled)
            {
                ClaimProjection.Apply(s, e);
                highest = Math.Max(highest, e.Sequence);
                if (s.LastSequence != highest) return false;
            }
            return s.Status == "Settled";
        });

    private static bool Same(ClaimSnapshot a, ClaimSnapshot b) =>
        a.Status == b.Status && a.ReserveAmount == b.ReserveAmount && a.LastSequence == b.LastSequence &&
        a.ApprovedAmount == b.ApprovedAmount && a.SettledAmount == b.SettledAmount && a.HandlerId == b.HandlerId;

    [Theory]
    [InlineData("InformationRequested", true)]
    [InlineData("Approved", true)]
    [InlineData("Rejected", true)]
    [InlineData("Settled", true)]
    [InlineData("ReserveAdjusted", false)] // internal: claimants are never told about reserves
    [InlineData("NoteAdded", false)]
    public void Only_claimant_facing_events_notify(string type, bool notifies)
    {
        var e = Event(Guid.NewGuid(), 2, type, "UnderReview", 100m, approved: 100m, settled: 100m);
        Assert.Equal(notifies, ClaimProjection.NotificationFor(e) is not null);
    }

    [Fact]
    public void Notification_amounts_follow_currency_minor_units()
    {
        var krw = Event(Guid.NewGuid(), 4, "Approved", "Approved", 3_500_000m, "KR", "KRW", approved: 3_500_000m);
        Assert.Contains("KRW 3,500,000.", ClaimProjection.NotificationFor(krw));
        Assert.DoesNotContain("3,500,000.00", ClaimProjection.NotificationFor(krw));
    }
}
