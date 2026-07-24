namespace LastTrain.Api.Models;

/// <summary>A physical MRT/LRT station, e.g. "Bishan". One row per station,
/// regardless of how many lines interchange there.</summary>
public class Station
{
    public int Id { get; set; }
    public string Name { get; set; } = "";          // "Bishan"
    public double? Lat { get; set; }
    public double? Lng { get; set; }

    public List<StationLine> StationLines { get; set; } = new();
}

/// <summary>A line, e.g. North-South Line. Colour/code matches the map legend.</summary>
public class Line
{
    public int Id { get; set; }
    public string Code { get; set; } = "";           // "NSL", "CCL", "DTL", ...
    public string Name { get; set; } = "";           // "North-South Line"
    public string ColourHex { get; set; } = "";       // for drawing the map

    public List<StationLine> StationLines { get; set; } = new();
}

/// <summary>Join row: this station sits at this position on this line,
/// with this local station code (e.g. NS17, CC15 — a station can have
/// several of these, one per line it interchanges with).
/// SequenceIndex is the station's ordinal position along the line, used to
/// build adjacency (consecutive SequenceIndex on the same LineId = an edge)
/// and to work out which stations lie "beyond" a given point (needed to
/// resolve which "last train to X" applies).</summary>
public class StationLine
{
    public int Id { get; set; }
    public int StationId { get; set; }
    public Station Station { get; set; } = null!;
    public int LineId { get; set; }
    public Line Line { get; set; } = null!;
    public string StationCode { get; set; } = "";    // "NS17"
    public int SequenceIndex { get; set; }           // 0,1,2... along the line
    /// Typical scheduled running time in minutes to the NEXT StationLine
    /// (SequenceIndex + 1) on the same line. Null on the last stop of a branch.
    public double? MinutesToNextStop { get; set; }
}

/// <summary>The actual thing this whole app exists to answer:
/// "from this station, on this line, heading toward this terminus,
/// what's the last train?" Mirrors exactly what sgtrains.com publishes
/// per station per line per direction.</summary>
public class LastTrainService
{
    public int Id { get; set; }
    public int StationId { get; set; }
    public Station Station { get; set; } = null!;
    public int LineId { get; set; }
    public Line Line { get; set; } = null!;

    /// The terminus/destination exactly as published, e.g. "Marina South Pier".
    /// This is what "Train to ___" says on the source site — it may be a full
    /// branch terminus or a short-turn destination.
    public int TowardsStationId { get; set; }
    public Station TowardsStation { get; set; } = null!;

    // Last train — confirmed to be the SAME every day (Mon-Sun & PH) from
    // the real data pulled from sgtrains.com. Kept as one field, not three.
    public TimeOnly LastTrainTime { get; set; }

    // First train DOES vary by day type — kept for completeness /
    // possible future "first train" feature, unused by the last-train flow.
    public TimeOnly? FirstTrainWeekday { get; set; }
    public TimeOnly? FirstTrainSaturday { get; set; }
    public TimeOnly? FirstTrainSundayPH { get; set; }

    /// True only for rows entered from a real sgtrains.com read.
    /// False = generated placeholder, needs verifying before you trust it late at night.
    public bool IsVerified { get; set; }
    public DateOnly? SourcedOn { get; set; }
}

/// <summary>Singapore public holidays. Not actually needed for LAST train
/// (that's day-invariant per the real data), but kept because it's cheap to
/// have and useful if you ever add first-train or off-peak-frequency features.</summary>
public class PublicHoliday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}
