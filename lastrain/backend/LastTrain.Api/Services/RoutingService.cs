using LastTrain.Api.Data;
using LastTrain.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LastTrain.Api.Services;

public record LegResult(
    string FromStation, string ToStation, string LineName,
    string TowardsTerminus, TimeOnly LatestDeparture, bool IsVerified,
    double TravelMinutes);

public record JourneyResult(
    bool Feasible,
    string? Reason,
    TimeOnly? LatestDepartureFromOrigin,
    List<LegResult> Legs,
    bool AnyUnverifiedData,
    bool AlreadyDeparted,
    int? MinutesRemaining,
    string? TimingWarning,
    double? TotalTripMinutes,
    TimeOnly? EstimatedArrival);

/// <summary>
/// Finds the route, splits it into single-line legs, then walks the legs
/// BACKWARDS from the destination to compute the true latest-possible
/// departure time from the origin — because (as the Bishan -> Expo example
/// shows) the very last train from the origin can miss a later connection.
/// </summary>
public class RoutingService
{
    private readonly AppDbContext _db;
    private const double TransferBufferMinutes = 4; // walking + waiting at an interchange

    public RoutingService(AppDbContext db) => _db = db;

    public async Task<JourneyResult> PlanLastTrainAsync(string originName, string destName)
    {
        var stations = await _db.Stations.ToListAsync();
        var origin = stations.FirstOrDefault(s => s.Name.Equals(originName, StringComparison.OrdinalIgnoreCase));
        var dest = stations.FirstOrDefault(s => s.Name.Equals(destName, StringComparison.OrdinalIgnoreCase));
        if (origin is null || dest is null)
            return new JourneyResult(false, "Unknown station name.", null, new(), false, false, null, null, null, null);

        var stationLines = await _db.StationLines.Include(sl => sl.Line).ToListAsync();
        var path = FindShortestPath(origin.Id, dest.Id, stationLines);
        if (path is null)
            return new JourneyResult(false, "No route found between these stations.", null, new(), false, false, null, null, null, null);

        var legs = SplitIntoLegs(path, stationLines);
        if (legs.Count == 0)
            return new JourneyResult(false, "Origin and destination are the same station.", null, new(), false, false, null, null, null, null);

        var lastTrainRows = await _db.LastTrainServices.ToListAsync();
        var resolvedLegs = new List<LegResult>();
        var stationNameById = stations.ToDictionary(s => s.Id, s => s.Name);

        // Work out the latest-departure deadline for each leg, walking backwards.
        int? nextLegDeadlineMinutes = null; // "service minutes" (see ToServiceMinutes)

        for (int i = legs.Count - 1; i >= 0; i--)
        {
            var (lineId, stopIds) = legs[i];
            var fromId = stopIds[0];
            var toId = stopIds[^1];

            var ownLastTrain = ResolveLastTrain(fromId, lineId, toId, stationLines, lastTrainRows);
            if (ownLastTrain is null)
                return new JourneyResult(false,
                    $"No last-train data for {stationNameById[fromId]} on this line towards {stationNameById[toId]}.",
                    null, new(), false, false, null, null, null, null);

            var travelMinutes = LegTravelMinutes(lineId, stopIds, stationLines);

            int ownDeadline = ToServiceMinutes(ownLastTrain.Value.Time);
            int deadline = ownDeadline;

            if (nextLegDeadlineMinutes is not null)
            {
                // Must also arrive early enough to make the next leg, allowing
                // a transfer buffer at the interchange station.
                var arrivalCap = nextLegDeadlineMinutes.Value - TransferBufferMinutes;
                var deadlineViaConnection = (int)Math.Floor(arrivalCap - travelMinutes);
                deadline = Math.Min(deadline, deadlineViaConnection);
            }

            nextLegDeadlineMinutes = deadline;

            resolvedLegs.Insert(0, new LegResult(
                FromStation: stationNameById[fromId],
                ToStation: stationNameById[toId],
                LineName: (await _db.Lines.FindAsync(lineId))!.Name,
                TowardsTerminus: stationNameById[ownLastTrain.Value.TowardsId],
                LatestDeparture: FromServiceMinutes(deadline),
                IsVerified: ownLastTrain.Value.Verified,
                TravelMinutes: travelMinutes));
        }

        var overallDeadlineMinutes = nextLegDeadlineMinutes!.Value;
        var overallDeadline = FromServiceMinutes(overallDeadlineMinutes);
        var anyUnverified = resolvedLegs.Any(l => !l.IsVerified);

        // Compare against the clock right now. Times before ~04:00 are treated
        // as a continuation of "tonight" (same convention as ToServiceMinutes),
        // so this only makes sense for a same-night query — there's no date
        // picker yet, so "now" always means "tonight".
        const int WarningThresholdMinutes = 15;
        var nowMinutes = ToServiceMinutes(TimeOnly.FromDateTime(DateTime.Now));
        var minutesRemaining = overallDeadlineMinutes - nowMinutes;
        var alreadyDeparted = minutesRemaining < 0;

        string? timingWarning = null;
        if (alreadyDeparted)
            timingWarning = $"That last train already left {-minutesRemaining} minute(s) ago — this plan is no longer possible tonight.";
        else if (minutesRemaining <= WarningThresholdMinutes)
            timingWarning = $"Cutting it close — only {minutesRemaining} minute(s) left to make this deadline.";

        // Forward-looking figure: if you actually depart at the deadline above,
        // how long does A→B→C...→destination take, and when do you land?
        // (Sum of each leg's travel time, plus a transfer buffer between legs.)
        var totalTravelMinutes = resolvedLegs.Sum(l => l.TravelMinutes);
        var totalTransferMinutes = Math.Max(0, resolvedLegs.Count - 1) * TransferBufferMinutes;
        var totalTripMinutes = totalTravelMinutes + totalTransferMinutes;
        var estimatedArrival = FromServiceMinutes(overallDeadlineMinutes + (int)Math.Round(totalTripMinutes));

        return new JourneyResult(true, null, overallDeadline, resolvedLegs, anyUnverified,
            alreadyDeparted, minutesRemaining, timingWarning, totalTripMinutes, estimatedArrival);
    }

    // ---- graph search -------------------------------------------------

    /// Returns the ordered list of (StationId, LineIdUsedToArrive) for the
    /// fastest path, or null if unreachable. LineIdUsedToArrive is null for
    /// the origin node itself.
    private List<(int StationId, int? LineId)>? FindShortestPath(
        int originId, int destId, List<StationLine> stationLines)
    {
        // Nodes are (stationId, lineId) = "standing at this station, currently
        // riding this line". This is what makes interchanges cost a transfer
        // buffer while continuing on the same line costs nothing extra.
        var byLine = stationLines.GroupBy(sl => sl.LineId)
            .ToDictionary(g => g.Key, g => g.OrderBy(sl => sl.SequenceIndex).ToList());
        var byStation = stationLines.GroupBy(sl => sl.StationId)
            .ToDictionary(g => g.Key, g => g.Select(sl => sl.LineId).Distinct().ToList());

        // (station,line) -> [(toStation, toLine, minutes)]
        var edges = new Dictionary<(int St, int Ln), List<(int ToSt, int ToLn, double Minutes)>>();
        void AddEdge(int fromSt, int fromLn, int toSt, int toLn, double minutes)
        {
            var key = (fromSt, fromLn);
            if (!edges.TryGetValue(key, out var list)) edges[key] = list = new();
            list.Add((toSt, toLn, minutes));
        }

        // Same-line hops, both directions.
        foreach (var (lineId, stops) in byLine)
        {
            for (int i = 0; i < stops.Count - 1; i++)
            {
                var a = stops[i]; var b = stops[i + 1];
                var mins = a.MinutesToNextStop ?? 2.5;
                AddEdge(a.StationId, lineId, b.StationId, lineId, mins);
                AddEdge(b.StationId, lineId, a.StationId, lineId, mins);
            }
        }
        // Interchange hops: switching lines at the same station costs the buffer.
        foreach (var (stationId, lineIds) in byStation)
            foreach (var fromLine in lineIds)
                foreach (var toLine in lineIds)
                    if (fromLine != toLine)
                        AddEdge(stationId, fromLine, stationId, toLine, TransferBufferMinutes);

        const int NoLine = 0; // pseudo "not yet boarded" line id
        var dist = new Dictionary<(int, int), double> { [(originId, NoLine)] = 0 };
        var prev = new Dictionary<(int, int), (int, int)?> { [(originId, NoLine)] = null };
        var visited = new HashSet<(int, int)>();
        var pq = new PriorityQueue<(int St, int Ln), double>();
        pq.Enqueue((originId, NoLine), 0);

        // Free "boarding" edges from the unset start state onto every line
        // available at the origin station.
        if (byStation.TryGetValue(originId, out var originLines))
            foreach (var l in originLines)
                AddEdge(originId, NoLine, originId, l, 0);

        while (pq.Count > 0)
        {
            var cur = pq.Dequeue();
            if (!visited.Add(cur)) continue;
            if (cur.St == destId)
                return ReconstructPath(cur, prev);

            if (!edges.TryGetValue((cur.St, cur.Ln), out var outgoing)) continue;
            foreach (var (toSt, toLn, minutes) in outgoing)
            {
                var nextKey = (toSt, toLn);
                var cost = dist[cur] + minutes;
                if (!dist.TryGetValue(nextKey, out var best) || cost < best)
                {
                    dist[nextKey] = cost;
                    prev[nextKey] = cur;
                    pq.Enqueue(nextKey, cost);
                }
            }
        }
        return null;
    }

    private List<(int StationId, int? LineId)> ReconstructPath(
        (int St, int Ln) end, Dictionary<(int, int), (int, int)?> prev)
    {
        var chain = new List<(int St, int Ln)> { end };
        var cur = end;
        while (prev.TryGetValue(cur, out var p) && p is not null)
        {
            chain.Add(p.Value);
            cur = p.Value;
        }
        chain.Reverse();
        return chain.Select(c => (c.St, c.Ln == 0 ? (int?)null : c.Ln)).ToList();
    }

    // ---- leg splitting --------------------------------------------------

    /// Collapses the raw path into (LineId, [stationIds in order]) legs,
    /// merging consecutive hops on the same physical line and dropping the
    /// zero-distance "interchange" nodes into the leg boundary.
    private List<(int LineId, List<int> Stops)> SplitIntoLegs(
        List<(int StationId, int? LineId)> path, List<StationLine> stationLines)
    {
        var legs = new List<(int LineId, List<int> Stops)>();
        int? currentLine = null;
        List<int>? currentStops = null;

        foreach (var (stationId, lineId) in path)
        {
            if (lineId is null) continue; // interchange marker node, no distance travelled
            if (lineId != currentLine)
            {
                if (currentStops is { Count: > 1 })
                    legs.Add((currentLine!.Value, currentStops));
                currentLine = lineId;
                currentStops = new List<int> { stationId };
            }
            else
            {
                currentStops!.Add(stationId);
            }
        }
        if (currentStops is { Count: > 1 })
            legs.Add((currentLine!.Value, currentStops));

        return legs;
    }

    private double LegTravelMinutes(int lineId, List<int> stopIds, List<StationLine> stationLines)
    {
        var byStation = stationLines.Where(sl => sl.LineId == lineId)
            .ToDictionary(sl => sl.StationId, sl => sl);
        double total = 0;
        for (int i = 0; i < stopIds.Count - 1; i++)
            total += byStation[stopIds[i]].MinutesToNextStop ?? 2.5;
        return total;
    }

    // ---- last-train lookup ----------------------------------------------

    /// Finds the latest possible last-train time at `fromId` on `lineId` that
    /// still reaches `mustReachId` — i.e. any published "last train towards X"
    /// where X is at-or-beyond mustReachId in the travel direction. Returns the
    /// LATEST such time (the best case), since any of those services gets you
    /// to your stop.
    private (TimeOnly Time, int TowardsId, bool Verified)? ResolveLastTrain(
        int fromId, int lineId, int mustReachId,
        List<StationLine> stationLines, List<LastTrainService> rows)
    {
        var seqByStation = stationLines.Where(sl => sl.LineId == lineId)
            .ToDictionary(sl => sl.StationId, sl => sl.SequenceIndex);
        if (!seqByStation.TryGetValue(fromId, out var fromSeq)) return null;
        if (!seqByStation.TryGetValue(mustReachId, out var reachSeq)) return null;
        int direction = Math.Sign(reachSeq - fromSeq);
        if (direction == 0) return null;

        var candidates = rows.Where(r => r.StationId == fromId && r.LineId == lineId)
            .Where(r => seqByStation.TryGetValue(r.TowardsStationId, out var tSeq)
                        && Math.Sign(tSeq - fromSeq) == direction
                        && (tSeq - reachSeq) * direction >= 0)
            .ToList();
        if (candidates.Count == 0) return null;

        var best = candidates.OrderByDescending(r => ToServiceMinutes(r.LastTrainTime)).First();
        return (best.LastTrainTime, best.TowardsStationId, best.IsVerified);
    }

    // ---- time helpers -----------------------------------------------------
    // Last-train times cross midnight, so "00:14" is really later than "23:34".
    // We represent times as minutes since 18:00, wrapping anything before
    // ~04:00 forward by 24h so ordering/subtraction works normally.

    private static int ToServiceMinutes(TimeOnly t)
    {
        int minutes = t.Hour * 60 + t.Minute;
        if (t.Hour < 4) minutes += 24 * 60;
        return minutes;
    }

    private static TimeOnly FromServiceMinutes(int m)
    {
        m %= (24 * 60);
        if (m < 0) m += 24 * 60;
        return new TimeOnly(m / 60, m % 60);
    }
}
