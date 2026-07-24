using LastTrain.Api.Models;

namespace LastTrain.Api.Data;

/// <summary>
/// Builds the station/line network from the July 2026 SGTrains map, plus:
///  - REAL last-train data for stations explicitly requested so far
///    (pulled directly from sgtrains.com/guide-traintiming), and
///  - PLACEHOLDER (IsVerified = false) last-train rows everywhere else, so the
///    routing engine has something to compute against for any station pair.
///
/// To replace a placeholder with a real number: find the row (StationId +
/// LineId + TowardsStationId), update LastTrainTime, set IsVerified = true.
/// See /scraper/scrape_last_trains.py for a semi-automated way to do this at scale.
/// </summary>
public static class SeedData
{
    // name -> Station, so interchange stations (added once per line) are reused.
    private static readonly Dictionary<string, Station> _stations = new();
    private static readonly List<Line> _lines = new();
    private static readonly List<StationLine> _stationLines = new();
    private static int _stationId = 1;
    private static int _lineId = 1;
    private static int _stationLineId = 1;

    public static void Apply(AppDbContext db)
    {
        if (db.Stations.Any()) return; // already seeded

        BuildNetwork();

        db.Stations.AddRange(_stations.Values);
        db.Lines.AddRange(_lines);
        db.StationLines.AddRange(_stationLines);
        db.SaveChanges();

        SeedRealDemoData(db);
        db.SaveChanges(); // persist real rows FIRST so the placeholder pass can see them
        SeedPlaceholders(db);
        SeedPublicHolidays(db);
        db.SaveChanges();
    }

    private static Station GetOrAddStation(string name)
    {
        if (!_stations.TryGetValue(name, out var s))
        {
            s = new Station { Id = _stationId++, Name = name };
            _stations[name] = s;
        }
        return s;
    }

    /// Adds one line's full station sequence. minutesPerHop is a flat estimate
    /// (real running times vary ~1.5-3 min between stops) — good enough to
    /// compute "can I make the connection", not good enough for a precise ETA.
    private static Line AddLine(string code, string name, string colourHex,
        double minutesPerHop, params (string Name, string StnCode)[] stops)
    {
        var line = new Line { Id = _lineId++, Code = code, Name = name, ColourHex = colourHex };
        _lines.Add(line);

        for (int i = 0; i < stops.Length; i++)
        {
            var station = GetOrAddStation(stops[i].Name);
            var sl = new StationLine
            {
                Id = _stationLineId++,
                StationId = station.Id,
                LineId = line.Id,
                StationCode = stops[i].StnCode,
                SequenceIndex = i,
                MinutesToNextStop = i < stops.Length - 1 ? minutesPerHop : null
            };
            _stationLines.Add(sl);
        }
        return line;
    }

    private static void BuildNetwork()
    {
        AddLine("NSL", "North-South Line", "#D42E12", 2.2,
            ("Jurong East", "NS1"), ("Bukit Batok", "NS2"), ("Bukit Gombak", "NS3"),
            ("Choa Chu Kang", "NS4"), ("Yew Tee", "NS5"), ("Kranji", "NS7"),
            ("Marsiling", "NS8"), ("Woodlands", "NS9"), ("Admiralty", "NS10"),
            ("Sembawang", "NS11"), ("Canberra", "NS12"), ("Yishun", "NS13"),
            ("Khatib", "NS14"), ("Yio Chu Kang", "NS15"), ("Ang Mo Kio", "NS16"),
            ("Bishan", "NS17"), ("Braddell", "NS18"), ("Toa Payoh", "NS19"),
            ("Novena", "NS20"), ("Newton", "NS21"), ("Orchard", "NS22"),
            ("Somerset", "NS23"), ("Dhoby Ghaut", "NS24"), ("City Hall", "NS25"),
            ("Raffles Place", "NS26"), ("Marina Bay", "NS27"), ("Marina South Pier", "NS28"));

        AddLine("EWL", "East-West Line", "#009645", 2.3,
            ("Pasir Ris", "EW1"), ("Tampines", "EW2"), ("Simei", "EW3"),
            ("Tanah Merah", "EW4"), ("Bedok", "EW5"), ("Kembangan", "EW6"),
            ("Eunos", "EW7"), ("Paya Lebar", "EW8"), ("Aljunied", "EW9"),
            ("Kallang", "EW10"), ("Lavender", "EW11"), ("Bugis", "EW12"),
            ("City Hall", "EW13"), ("Raffles Place", "EW14"), ("Tanjong Pagar", "EW15"),
            ("Outram Park", "EW16"), ("Tiong Bahru", "EW17"), ("Redhill", "EW18"),
            ("Queenstown", "EW19"), ("Commonwealth", "EW20"), ("Buona Vista", "EW21"),
            ("Dover", "EW22"), ("Clementi", "EW23"), ("Jurong East", "EW24"),
            ("Chinese Garden", "EW25"), ("Lakeside", "EW26"), ("Boon Lay", "EW27"),
            ("Pioneer", "EW28"), ("Joo Koon", "EW29"), ("Gul Circle", "EW30"),
            ("Tuas Crescent", "EW31"), ("Tuas West Road", "EW32"), ("Tuas Link", "EW33"));

        AddLine("CGL", "Changi Airport Branch", "#009645", 3.0,
            ("Tanah Merah", "CG"), ("Expo", "CG1"), ("Changi Airport", "CG2"));

        AddLine("CCL", "Circle Line", "#F9A000", 2.1,
            ("Dhoby Ghaut", "CC1"), ("Bras Basah", "CC2"), ("Esplanade", "CC3"),
            ("Promenade", "CC4"), ("Nicoll Highway", "CC5"), ("Stadium", "CC6"),
            ("Mountbatten", "CC7"), ("Dakota", "CC8"), ("Paya Lebar", "CC9"),
            ("MacPherson", "CC10"), ("Tai Seng", "CC11"), ("Bartley", "CC12"),
            ("Serangoon", "CC13"), ("Lorong Chuan", "CC14"), ("Bishan", "CC15"),
            ("Marymount", "CC16"), ("Caldecott", "CC17"), ("Botanic Gardens", "CC19"),
            ("Farrer Road", "CC20"), ("Holland Village", "CC21"), ("Buona Vista", "CC22"),
            ("one-north", "CC23"), ("Kent Ridge", "CC24"), ("Haw Par Villa", "CC25"),
            ("Pasir Panjang", "CC26"), ("Labrador Park", "CC27"), ("Telok Blangah", "CC28"),
            ("HarbourFront", "CC29"), ("Keppel", "CC30"), ("Cantonment", "CC31"),
            ("Prince Edward Road", "CC32"), ("Marina Bay", "CC33"), ("Bayfront", "CC34"));

        AddLine("NEL", "North East Line", "#9900AA", 2.0,
            ("HarbourFront", "NE1"), ("Outram Park", "NE3"), ("Chinatown", "NE4"),
            ("Clarke Quay", "NE5"), ("Dhoby Ghaut", "NE6"), ("Little India", "NE7"),
            ("Farrer Park", "NE8"), ("Boon Keng", "NE9"), ("Potong Pasir", "NE10"),
            ("Woodleigh", "NE11"), ("Serangoon", "NE12"), ("Kovan", "NE13"),
            ("Hougang", "NE14"), ("Buangkok", "NE15"), ("Sengkang", "NE16"),
            ("Punggol", "NE17"), ("Punggol Coast", "NE18"));

        AddLine("DTL", "Downtown Line", "#005EC4", 2.1,
            ("Bukit Panjang", "DT1"), ("Cashew", "DT2"), ("Hillview", "DT3"),
            ("Beauty World", "DT5"), ("King Albert Park", "DT6"), ("Sixth Avenue", "DT7"),
            ("Tan Kah Kee", "DT8"), ("Botanic Gardens", "DT9"), ("Stevens", "DT10"),
            ("Newton", "DT11"), ("Little India", "DT12"), ("Rochor", "DT13"),
            ("Bugis", "DT14"), ("Promenade", "DT15"), ("Bayfront", "DT16"),
            ("Downtown", "DT17"), ("Telok Ayer", "DT18"), ("Chinatown", "DT19"),
            ("Fort Canning", "DT20"), ("Bencoolen", "DT21"), ("Jalan Besar", "DT22"),
            ("Bendemeer", "DT23"), ("Geylang Bahru", "DT24"), ("Mattar", "DT25"),
            ("MacPherson", "DT26"), ("Ubi", "DT27"), ("Kaki Bukit", "DT28"),
            ("Bedok North", "DT29"), ("Bedok Reservoir", "DT30"), ("Tampines West", "DT31"),
            ("Tampines", "DT32"), ("Tampines East", "DT33"), ("Upper Changi", "DT34"),
            ("Expo", "DT35"));

        AddLine("TEL", "Thomson-East Coast Line", "#9D5B25", 2.1,
            ("Woodlands North", "TE1"), ("Woodlands", "TE2"), ("Woodlands South", "TE3"),
            ("Springleaf", "TE4"), ("Lentor", "TE5"), ("Mayflower", "TE6"),
            ("Bright Hill", "TE7"), ("Upper Thomson", "TE8"), ("Caldecott", "TE9"),
            ("Stevens", "TE11"), ("Napier", "TE12"), ("Orchard Boulevard", "TE13"),
            ("Orchard", "TE14"), ("Great World", "TE15"), ("Havelock", "TE16"),
            ("Outram Park", "TE17"), ("Maxwell", "TE18"), ("Shenton Way", "TE19"),
            ("Marina Bay", "TE20"), ("Gardens by the Bay", "TE22"), ("Tanjong Rhu", "TE23"),
            ("Katong Park", "TE24"), ("Tanjong Katong", "TE25"), ("Marine Parade", "TE26"),
            ("Marine Terrace", "TE27"), ("Siglap", "TE28"), ("Bayshore", "TE29"));

        // LRT lines intentionally omitted from v1 — add the same way via AddLine()
        // if you need Bukit Panjang / Sengkang / Punggol LRT routing later.
    }

    private static LastTrainService Verified(AppDbContext db, string station, string line,
        string towards, TimeOnly time)
        => new()
        {
            StationId = _stations[station].Id,
            LineId = db.Lines.First(l => l.Code == line).Id,
            TowardsStationId = _stations[towards].Id,
            LastTrainTime = time,
            IsVerified = true,
            SourcedOn = new DateOnly(2026, 7, 16)
        };

    /// Real numbers read from sgtrains.com, station by station as requested.
    private static void SeedRealDemoData(AppDbContext db)
    {
        db.LastTrainServices.AddRange(
            // Bishan, North-South Line
            Verified(db, "Bishan", "NSL", "Marina South Pier", new TimeOnly(23, 34)),
            Verified(db, "Bishan", "NSL", "Jurong East", new TimeOnly(0, 14)),
            // Bishan, North-South Line, short-turn services (towards Kranji / Toa Payoh)
            Verified(db, "Bishan", "NSL", "Kranji", new TimeOnly(0, 30)),
            Verified(db, "Bishan", "NSL", "Toa Payoh", new TimeOnly(0, 16)),

            // Bishan, Circle Line — read directly from sgtrains.com (screenshot, Jul 2026).
            // "Anticlockwise Loop"/"Clockwise Loop" are full-loop services (no single
            // named terminus on the live site) — mapped here onto the two ends of our
            // linear CCL model (Bayfront / Dhoby Ghaut). Accurate for routes that stay
            // within Bishan..Bayfront (covers this route); NOT accurate for anything
            // needing the real wraparound segment back past Bayfront to Dhoby Ghaut,
            // since this app doesn't model CCL as an actual loop yet.
            Verified(db, "Bishan", "CCL", "Bayfront", new TimeOnly(23, 18)),        // "Anticlockwise Loop"
            Verified(db, "Bishan", "CCL", "Pasir Panjang", new TimeOnly(23, 46)),
            Verified(db, "Bishan", "CCL", "one-north", new TimeOnly(0, 3)),
            Verified(db, "Bishan", "CCL", "Caldecott", new TimeOnly(0, 34)),
            Verified(db, "Bishan", "CCL", "Dhoby Ghaut", new TimeOnly(23, 20)),     // "Clockwise Loop"
            Verified(db, "Bishan", "CCL", "Marina Bay", new TimeOnly(23, 35)),
            Verified(db, "Bishan", "CCL", "Mountbatten", new TimeOnly(0, 12)),
            Verified(db, "Bishan", "CCL", "Bartley", new TimeOnly(0, 35)),

            // Buona Vista, East-West Line
            Verified(db, "Buona Vista", "EWL", "Pasir Ris", new TimeOnly(23, 51)),
            Verified(db, "Buona Vista", "EWL", "Tanah Merah", new TimeOnly(23, 7)), // "Changi Airport via Tanah Merah"
            Verified(db, "Buona Vista", "EWL", "Tuas Link", new TimeOnly(0, 15)),
            // Buona Vista, Circle Line — was completely missing; added after re-checking
            // the live site (Jul 2026). Same Loop-label mapping convention as Bishan above.
            Verified(db, "Buona Vista", "CCL", "Bayfront", new TimeOnly(23, 34)),   // "Anticlockwise Loop"
            Verified(db, "Buona Vista", "CCL", "Pasir Panjang", new TimeOnly(0, 2)),
            Verified(db, "Buona Vista", "CCL", "one-north", new TimeOnly(0, 20)),
            Verified(db, "Buona Vista", "CCL", "Dhoby Ghaut", new TimeOnly(23, 3)), // "Clockwise Loop"
            Verified(db, "Buona Vista", "CCL", "Marina Bay", new TimeOnly(23, 18)),
            Verified(db, "Buona Vista", "CCL", "Mountbatten", new TimeOnly(23, 56)),
            Verified(db, "Buona Vista", "CCL", "Bartley", new TimeOnly(0, 18)),

            // Yishun, North-South Line
            Verified(db, "Yishun", "NSL", "Jurong East", new TimeOnly(0, 28)),
            Verified(db, "Yishun", "NSL", "Kranji", new TimeOnly(0, 44)),
            Verified(db, "Yishun", "NSL", "Marina South Pier", new TimeOnly(23, 20)),
            Verified(db, "Yishun", "NSL", "Toa Payoh", new TimeOnly(0, 2)),
            Verified(db, "Yishun", "NSL", "Ang Mo Kio", new TimeOnly(0, 51)),

            // Ang Mo Kio, North-South Line
            Verified(db, "Ang Mo Kio", "NSL", "Jurong East", new TimeOnly(0, 18)),
            Verified(db, "Ang Mo Kio", "NSL", "Kranji", new TimeOnly(0, 33)),
            Verified(db, "Ang Mo Kio", "NSL", "Marina South Pier", new TimeOnly(23, 31)),
            Verified(db, "Ang Mo Kio", "NSL", "Toa Payoh", new TimeOnly(0, 13)),

            // Pioneer, East-West Line
            Verified(db, "Pioneer", "EWL", "Pasir Ris", new TimeOnly(23, 32)),
            Verified(db, "Pioneer", "EWL", "Tanah Merah", new TimeOnly(22, 47)), // "Changi Airport via Tanah Merah"
            Verified(db, "Pioneer", "EWL", "Tuas Link", new TimeOnly(0, 35)),

            // MacPherson, Circle Line + Downtown Line
            Verified(db, "MacPherson", "CCL", "Dhoby Ghaut", new TimeOnly(23, 46)),
            Verified(db, "MacPherson", "CCL", "Mountbatten", new TimeOnly(0, 23)),
            Verified(db, "MacPherson", "CCL", "HarbourFront", new TimeOnly(23, 6)),
            Verified(db, "MacPherson", "CCL", "Pasir Panjang", new TimeOnly(23, 36)),
            Verified(db, "MacPherson", "CCL", "one-north", new TimeOnly(23, 52)),
            Verified(db, "MacPherson", "CCL", "Caldecott", new TimeOnly(0, 20)),
            Verified(db, "MacPherson", "CCL", "Tai Seng", new TimeOnly(0, 28)),
            Verified(db, "MacPherson", "DTL", "Bukit Panjang", new TimeOnly(0, 0)),
            Verified(db, "MacPherson", "DTL", "Expo", new TimeOnly(0, 24)),

            // Bedok, East-West Line
            Verified(db, "Bedok", "EWL", "Pasir Ris", new TimeOnly(0, 30)),
            Verified(db, "Bedok", "EWL", "Tanah Merah", new TimeOnly(23, 46)), // "Changi Airport via Tanah Merah"
            Verified(db, "Bedok", "EWL", "Tuas Link", new TimeOnly(23, 36)),

            // Tampines, East-West Line + Downtown Line
            Verified(db, "Tampines", "EWL", "Pasir Ris", new TimeOnly(0, 39)),
            Verified(db, "Tampines", "EWL", "Tuas Link", new TimeOnly(23, 26)),
            Verified(db, "Tampines", "EWL", "Tanah Merah", new TimeOnly(23, 26)), // "Changi Airport via Tanah Merah"
            Verified(db, "Tampines", "DTL", "Bukit Panjang", new TimeOnly(23, 47)),
            Verified(db, "Tampines", "DTL", "Expo", new TimeOnly(0, 36)),

            // Simei, East-West Line
            Verified(db, "Simei", "EWL", "Pasir Ris", new TimeOnly(0, 37)),
            Verified(db, "Simei", "EWL", "Tuas Link", new TimeOnly(23, 29)),
            Verified(db, "Simei", "EWL", "Tanah Merah", new TimeOnly(23, 29)), // "Changi Airport via Tanah Merah"

            // Dhoby Ghaut, North-South + North East + Circle Line
            Verified(db, "Dhoby Ghaut", "NSL", "Jurong East", new TimeOnly(23, 57)),
            Verified(db, "Dhoby Ghaut", "NSL", "Kranji", new TimeOnly(0, 13)),
            Verified(db, "Dhoby Ghaut", "NSL", "Marina South Pier", new TimeOnly(23, 51)),
            Verified(db, "Dhoby Ghaut", "NEL", "HarbourFront", new TimeOnly(23, 52)),
            Verified(db, "Dhoby Ghaut", "NEL", "Punggol Coast", new TimeOnly(0, 5)),
            Verified(db, "Dhoby Ghaut", "CCL", "HarbourFront", new TimeOnly(22, 48)),
            Verified(db, "Dhoby Ghaut", "CCL", "Pasir Panjang", new TimeOnly(23, 18)),
            Verified(db, "Dhoby Ghaut", "CCL", "one-north", new TimeOnly(23, 34)),
            Verified(db, "Dhoby Ghaut", "CCL", "Caldecott", new TimeOnly(0, 2)),
            Verified(db, "Dhoby Ghaut", "CCL", "Tai Seng", new TimeOnly(0, 10)),

            // Pasir Ris, East-West Line
            Verified(db, "Pasir Ris", "EWL", "Tuas Link", new TimeOnly(23, 23)),
            Verified(db, "Pasir Ris", "EWL", "Tanah Merah", new TimeOnly(23, 23)), // "Changi Airport via Tanah Merah"

            // Jurong East, North-South + East-West Line
            Verified(db, "Jurong East", "NSL", "Marina South Pier", new TimeOnly(22, 46)),
            Verified(db, "Jurong East", "NSL", "Toa Payoh", new TimeOnly(23, 28)),
            Verified(db, "Jurong East", "NSL", "Ang Mo Kio", new TimeOnly(0, 17)),
            Verified(db, "Jurong East", "EWL", "Pasir Ris", new TimeOnly(23, 42)),
            Verified(db, "Jurong East", "EWL", "Tanah Merah", new TimeOnly(22, 57)), // "Changi Airport via Tanah Merah"
            Verified(db, "Jurong East", "EWL", "Tuas Link", new TimeOnly(0, 25)),

            // Botanic Gardens, Circle Line + Downtown Line
            Verified(db, "Botanic Gardens", "CCL", "Dhoby Ghaut", new TimeOnly(23, 25)),
            Verified(db, "Botanic Gardens", "CCL", "Mountbatten", new TimeOnly(0, 2)),
            Verified(db, "Botanic Gardens", "CCL", "Bartley", new TimeOnly(0, 26)),
            Verified(db, "Botanic Gardens", "CCL", "HarbourFront", new TimeOnly(23, 27)),
            Verified(db, "Botanic Gardens", "CCL", "Pasir Panjang", new TimeOnly(23, 57)),
            Verified(db, "Botanic Gardens", "CCL", "one-north", new TimeOnly(0, 13)),
            Verified(db, "Botanic Gardens", "DTL", "Bukit Panjang", new TimeOnly(0, 32)),
            Verified(db, "Botanic Gardens", "DTL", "Expo", new TimeOnly(23, 50)),

            // Paya Lebar, East-West Line + Circle Line
            Verified(db, "Paya Lebar", "EWL", "Pasir Ris", new TimeOnly(0, 22)),
            Verified(db, "Paya Lebar", "EWL", "Tanah Merah", new TimeOnly(23, 38)), // "Changi Airport via Tanah Merah"
            Verified(db, "Paya Lebar", "EWL", "Tuas Link", new TimeOnly(23, 43)),
            Verified(db, "Paya Lebar", "CCL", "Dhoby Ghaut", new TimeOnly(23, 49)),
            Verified(db, "Paya Lebar", "CCL", "Mountbatten", new TimeOnly(0, 26)),
            Verified(db, "Paya Lebar", "CCL", "HarbourFront", new TimeOnly(23, 4)),
            Verified(db, "Paya Lebar", "CCL", "Pasir Panjang", new TimeOnly(23, 34)),
            Verified(db, "Paya Lebar", "CCL", "one-north", new TimeOnly(23, 50)),
            Verified(db, "Paya Lebar", "CCL", "Caldecott", new TimeOnly(0, 18)),
            Verified(db, "Paya Lebar", "CCL", "Tai Seng", new TimeOnly(0, 26)),

            // Marina Bay, North-South + Circle + Thomson-East Coast Line
            Verified(db, "Marina Bay", "NSL", "Jurong East", new TimeOnly(23, 50)),
            Verified(db, "Marina Bay", "NSL", "Kranji", new TimeOnly(0, 6)),
            Verified(db, "Marina Bay", "NSL", "Marina South Pier", new TimeOnly(23, 58)),
            Verified(db, "Marina Bay", "CCL", "Stadium", new TimeOnly(23, 55)),
            Verified(db, "Marina Bay", "TEL", "Woodlands North", new TimeOnly(0, 2)),
            Verified(db, "Marina Bay", "TEL", "Caldecott", new TimeOnly(0, 14)),
            Verified(db, "Marina Bay", "TEL", "Orchard", new TimeOnly(0, 20)),
            Verified(db, "Marina Bay", "TEL", "Bayshore", new TimeOnly(0, 14)),
            Verified(db, "Marina Bay", "TEL", "Marine Terrace", new TimeOnly(0, 19)),
            Verified(db, "Marina Bay", "TEL", "Gardens by the Bay", new TimeOnly(0, 25)),

            // HarbourFront, North East Line + Circle Line
            Verified(db, "HarbourFront", "NEL", "Punggol Coast", new TimeOnly(23, 55)),
            Verified(db, "HarbourFront", "CCL", "Dhoby Ghaut", new TimeOnly(23, 3)),
            Verified(db, "HarbourFront", "CCL", "Mountbatten", new TimeOnly(23, 40)),
            Verified(db, "HarbourFront", "CCL", "Bartley", new TimeOnly(0, 4)),

            // Serangoon, North East Line + Circle Line
            Verified(db, "Serangoon", "NEL", "HarbourFront", new TimeOnly(23, 40)),
            Verified(db, "Serangoon", "NEL", "Punggol Coast", new TimeOnly(0, 17)),
            Verified(db, "Serangoon", "CCL", "Bayfront", new TimeOnly(23, 13)),   // "Anticlockwise Loop"
            Verified(db, "Serangoon", "CCL", "Dhoby Ghaut", new TimeOnly(23, 25)), // "Clockwise Loop"

            // Choa Chu Kang, North-South Line
            // (Bukit Panjang LRT also interchanges here — not modeled yet, see README.)
            Verified(db, "Choa Chu Kang", "NSL", "Jurong East", new TimeOnly(0, 53)),
            Verified(db, "Choa Chu Kang", "NSL", "Marina South Pier", new TimeOnly(22, 56)),
            Verified(db, "Choa Chu Kang", "NSL", "Toa Payoh", new TimeOnly(23, 37)),
            Verified(db, "Choa Chu Kang", "NSL", "Ang Mo Kio", new TimeOnly(0, 26)),

            // Orchard, North-South Line + Thomson-East Coast Line
            Verified(db, "Orchard", "NSL", "Jurong East", new TimeOnly(0, 2)),
            Verified(db, "Orchard", "NSL", "Kranji", new TimeOnly(0, 17)),
            Verified(db, "Orchard", "NSL", "Marina South Pier", new TimeOnly(23, 46)),
            Verified(db, "Orchard", "TEL", "Woodlands North", new TimeOnly(0, 13)),
            Verified(db, "Orchard", "TEL", "Caldecott", new TimeOnly(0, 25)),
            Verified(db, "Orchard", "TEL", "Bayshore", new TimeOnly(0, 3)),
            Verified(db, "Orchard", "TEL", "Marine Terrace", new TimeOnly(0, 8)),
            Verified(db, "Orchard", "TEL", "Gardens by the Bay", new TimeOnly(0, 14)),
            Verified(db, "Orchard", "TEL", "Outram Park", new TimeOnly(0, 20)),

            // Outram Park, East-West + North East + Thomson-East Coast Line
            Verified(db, "Outram Park", "EWL", "Pasir Ris", new TimeOnly(0, 3)),
            Verified(db, "Outram Park", "EWL", "Tanah Merah", new TimeOnly(23, 19)), // "Changi Airport via Tanah Merah"
            Verified(db, "Outram Park", "EWL", "Tuas Link", new TimeOnly(0, 2)),
            Verified(db, "Outram Park", "NEL", "HarbourFront", new TimeOnly(23, 58)),
            Verified(db, "Outram Park", "NEL", "Punggol Coast", new TimeOnly(23, 59)),
            Verified(db, "Outram Park", "TEL", "Woodlands North", new TimeOnly(0, 7)),
            Verified(db, "Outram Park", "TEL", "Caldecott", new TimeOnly(0, 19)),
            Verified(db, "Outram Park", "TEL", "Orchard", new TimeOnly(0, 25)),
            Verified(db, "Outram Park", "TEL", "Bayshore", new TimeOnly(0, 9)),
            Verified(db, "Outram Park", "TEL", "Marine Terrace", new TimeOnly(0, 14)),
            Verified(db, "Outram Park", "TEL", "Gardens by the Bay", new TimeOnly(0, 20)),

            // Redhill, East-West Line
            Verified(db, "Redhill", "EWL", "Pasir Ris", new TimeOnly(23, 58)),
            Verified(db, "Redhill", "EWL", "Tanah Merah", new TimeOnly(23, 14)), // "Changi Airport via Tanah Merah"
            Verified(db, "Redhill", "EWL", "Tuas Link", new TimeOnly(0, 8)),

            // Queenstown, East-West Line
            Verified(db, "Queenstown", "EWL", "Pasir Ris", new TimeOnly(23, 55)),
            Verified(db, "Queenstown", "EWL", "Tanah Merah", new TimeOnly(23, 11)), // "Changi Airport via Tanah Merah"
            Verified(db, "Queenstown", "EWL", "Tuas Link", new TimeOnly(0, 10)),

            // Clementi, East-West Line
            Verified(db, "Clementi", "EWL", "Pasir Ris", new TimeOnly(23, 46)),
            Verified(db, "Clementi", "EWL", "Tanah Merah", new TimeOnly(23, 2)), // "Changi Airport via Tanah Merah"
            Verified(db, "Clementi", "EWL", "Tuas Link", new TimeOnly(0, 20)),

            // Stevens, Downtown Line + Thomson-East Coast Line
            Verified(db, "Stevens", "DTL", "Bukit Panjang", new TimeOnly(0, 30)),
            Verified(db, "Stevens", "DTL", "Expo", new TimeOnly(23, 53)),
            Verified(db, "Stevens", "TEL", "Woodlands North", new TimeOnly(0, 19)),
            Verified(db, "Stevens", "TEL", "Caldecott", new TimeOnly(0, 31)),
            Verified(db, "Stevens", "TEL", "Bayshore", new TimeOnly(23, 56)),
            Verified(db, "Stevens", "TEL", "Marine Terrace", new TimeOnly(0, 2)),
            Verified(db, "Stevens", "TEL", "Gardens by the Bay", new TimeOnly(0, 8)),
            Verified(db, "Stevens", "TEL", "Outram Park", new TimeOnly(0, 14)),
            Verified(db, "Stevens", "TEL", "Orchard", new TimeOnly(0, 26)),

            // Rochor, Downtown Line
            Verified(db, "Rochor", "DTL", "Bukit Panjang", new TimeOnly(0, 24)),
            Verified(db, "Rochor", "DTL", "Expo", new TimeOnly(23, 59)),

            // Bencoolen, Downtown Line
            Verified(db, "Bencoolen", "DTL", "Bukit Panjang", new TimeOnly(0, 10)),
            Verified(db, "Bencoolen", "DTL", "Expo", new TimeOnly(0, 13)),

            // Telok Ayer, Downtown Line
            Verified(db, "Telok Ayer", "DTL", "Bukit Panjang", new TimeOnly(0, 15)),
            Verified(db, "Telok Ayer", "DTL", "Expo", new TimeOnly(0, 8)),

            // Marymount, Circle Line
            Verified(db, "Marymount", "CCL", "Dhoby Ghaut", new TimeOnly(23, 32)),
            Verified(db, "Marymount", "CCL", "Mountbatten", new TimeOnly(0, 9)),
            Verified(db, "Marymount", "CCL", "Bartley", new TimeOnly(0, 33)),
            Verified(db, "Marymount", "CCL", "HarbourFront", new TimeOnly(23, 20)),
            Verified(db, "Marymount", "CCL", "Pasir Panjang", new TimeOnly(23, 50)),
            Verified(db, "Marymount", "CCL", "one-north", new TimeOnly(0, 6)),
            Verified(db, "Marymount", "CCL", "Caldecott", new TimeOnly(0, 34)),

            // Holland Village, Circle Line
            Verified(db, "Holland Village", "CCL", "Dhoby Ghaut", new TimeOnly(23, 21)),
            Verified(db, "Holland Village", "CCL", "Mountbatten", new TimeOnly(23, 58)),
            Verified(db, "Holland Village", "CCL", "Bartley", new TimeOnly(0, 21)),
            Verified(db, "Holland Village", "CCL", "HarbourFront", new TimeOnly(23, 31)),
            Verified(db, "Holland Village", "CCL", "Pasir Panjang", new TimeOnly(0, 2)),
            Verified(db, "Holland Village", "CCL", "one-north", new TimeOnly(0, 18)),

            // Kovan, North East Line
            Verified(db, "Kovan", "NEL", "HarbourFront", new TimeOnly(23, 37)),
            Verified(db, "Kovan", "NEL", "Punggol Coast", new TimeOnly(0, 20)),

            // Commonwealth, East-West Line
            Verified(db, "Commonwealth", "EWL", "Pasir Ris", new TimeOnly(23, 53)),
            Verified(db, "Commonwealth", "EWL", "Tanah Merah", new TimeOnly(23, 9)), // "Changi Airport via Tanah Merah"
            Verified(db, "Commonwealth", "EWL", "Tuas Link", new TimeOnly(0, 12)),

            // Dover, East-West Line
            Verified(db, "Dover", "EWL", "Pasir Ris", new TimeOnly(23, 49)),
            Verified(db, "Dover", "EWL", "Tanah Merah", new TimeOnly(23, 4)), // "Changi Airport via Tanah Merah"
            Verified(db, "Dover", "EWL", "Tuas Link", new TimeOnly(0, 17)),

            // Tanjong Pagar, East-West Line
            Verified(db, "Tanjong Pagar", "EWL", "Pasir Ris", new TimeOnly(0, 5)),
            Verified(db, "Tanjong Pagar", "EWL", "Tanah Merah", new TimeOnly(23, 21)), // "Changi Airport via Tanah Merah"
            Verified(db, "Tanjong Pagar", "EWL", "Tuas Link", new TimeOnly(0, 0)),

            // Boon Keng, North East Line
            Verified(db, "Boon Keng", "NEL", "HarbourFront", new TimeOnly(23, 46)),
            Verified(db, "Boon Keng", "NEL", "Punggol Coast", new TimeOnly(0, 11)),

            // Potong Pasir, North East Line
            Verified(db, "Potong Pasir", "NEL", "HarbourFront", new TimeOnly(23, 44)),
            Verified(db, "Potong Pasir", "NEL", "Punggol Coast", new TimeOnly(0, 13)),

            // Woodleigh, North East Line
            Verified(db, "Woodleigh", "NEL", "HarbourFront", new TimeOnly(23, 42)),
            Verified(db, "Woodleigh", "NEL", "Punggol Coast", new TimeOnly(0, 15)),

            // Promenade, Circle Line + Downtown Line
            Verified(db, "Promenade", "CCL", "Bayfront", new TimeOnly(22, 52)),   // "Anticlockwise Loop"
            Verified(db, "Promenade", "CCL", "Pasir Panjang", new TimeOnly(23, 21)),
            Verified(db, "Promenade", "CCL", "one-north", new TimeOnly(23, 38)),
            Verified(db, "Promenade", "CCL", "Caldecott", new TimeOnly(0, 9)),
            Verified(db, "Promenade", "CCL", "Tai Seng", new TimeOnly(0, 14)),
            Verified(db, "Promenade", "CCL", "Stadium", new TimeOnly(0, 2)),
            Verified(db, "Promenade", "CCL", "Dhoby Ghaut", new TimeOnly(23, 56)),
            Verified(db, "Promenade", "CCL", "Marina Bay", new TimeOnly(0, 0)),
            Verified(db, "Promenade", "DTL", "Bukit Panjang", new TimeOnly(0, 20)),
            Verified(db, "Promenade", "DTL", "Expo", new TimeOnly(0, 3)),

            // Esplanade, Circle Line
            Verified(db, "Esplanade", "CCL", "Stadium", new TimeOnly(0, 0)),
            Verified(db, "Esplanade", "CCL", "Dhoby Ghaut", new TimeOnly(23, 59)),

            // Fort Canning, Downtown Line
            Verified(db, "Fort Canning", "DTL", "Bukit Panjang", new TimeOnly(0, 11)),
            Verified(db, "Fort Canning", "DTL", "Expo", new TimeOnly(0, 12)),

            // Tai Seng, Circle Line
            Verified(db, "Tai Seng", "CCL", "Bayfront", new TimeOnly(23, 7)),   // "Anticlockwise Loop"
            Verified(db, "Tai Seng", "CCL", "Pasir Panjang", new TimeOnly(23, 36)),
            Verified(db, "Tai Seng", "CCL", "one-north", new TimeOnly(23, 53)),
            Verified(db, "Tai Seng", "CCL", "Caldecott", new TimeOnly(0, 24)),
            Verified(db, "Tai Seng", "CCL", "Dhoby Ghaut", new TimeOnly(23, 30)), // "Clockwise Loop"
            Verified(db, "Tai Seng", "CCL", "Marina Bay", new TimeOnly(23, 44)),
            Verified(db, "Tai Seng", "CCL", "Mountbatten", new TimeOnly(0, 2)),

            // one-north, Circle Line
            Verified(db, "one-north", "CCL", "Bayfront", new TimeOnly(23, 37)),   // "Anticlockwise Loop"
            Verified(db, "one-north", "CCL", "Pasir Panjang", new TimeOnly(0, 4)),
            Verified(db, "one-north", "CCL", "Dhoby Ghaut", new TimeOnly(23, 1)), // "Clockwise Loop"
            Verified(db, "one-north", "CCL", "Marina Bay", new TimeOnly(23, 15)),
            Verified(db, "one-north", "CCL", "Mountbatten", new TimeOnly(23, 54)),
            Verified(db, "one-north", "CCL", "Bartley", new TimeOnly(0, 15)),

            // Mountbatten, Circle Line
            Verified(db, "Mountbatten", "CCL", "Bayfront", new TimeOnly(22, 58)),   // "Anticlockwise Loop"
            Verified(db, "Mountbatten", "CCL", "Pasir Panjang", new TimeOnly(23, 27)),
            Verified(db, "Mountbatten", "CCL", "one-north", new TimeOnly(23, 44)),
            Verified(db, "Mountbatten", "CCL", "Caldecott", new TimeOnly(0, 15)),
            Verified(db, "Mountbatten", "CCL", "Tai Seng", new TimeOnly(0, 21)),
            Verified(db, "Mountbatten", "CCL", "Dhoby Ghaut", new TimeOnly(23, 39)), // "Clockwise Loop"
            Verified(db, "Mountbatten", "CCL", "Marina Bay", new TimeOnly(23, 53)),

            // Bayfront, Circle Line + Downtown Line
            // (Bayfront is the last stop in this app's linear CCL model, so the
            // only real direction from here is back towards Dhoby Ghaut.)
            Verified(db, "Bayfront", "CCL", "Dhoby Ghaut", new TimeOnly(22, 50)),   // "Anticlockwise Loop"
            Verified(db, "Bayfront", "DTL", "Bukit Panjang", new TimeOnly(0, 18)),
            Verified(db, "Bayfront", "DTL", "Expo", new TimeOnly(0, 5)),

            // Bartley, Circle Line
            Verified(db, "Bartley", "CCL", "Bayfront", new TimeOnly(23, 10)),   // "Anticlockwise Loop"
            Verified(db, "Bartley", "CCL", "Pasir Panjang", new TimeOnly(23, 38)),
            Verified(db, "Bartley", "CCL", "one-north", new TimeOnly(23, 55)),
            Verified(db, "Bartley", "CCL", "Caldecott", new TimeOnly(0, 26)),
            Verified(db, "Bartley", "CCL", "Dhoby Ghaut", new TimeOnly(23, 28)), // "Clockwise Loop"
            Verified(db, "Bartley", "CCL", "Marina Bay", new TimeOnly(23, 42)),
            Verified(db, "Bartley", "CCL", "Mountbatten", new TimeOnly(0, 20)),

            // Pasir Panjang, Circle Line
            Verified(db, "Pasir Panjang", "CCL", "Bayfront", new TimeOnly(23, 43)),   // "Anticlockwise Loop"
            Verified(db, "Pasir Panjang", "CCL", "Dhoby Ghaut", new TimeOnly(22, 54)), // "Clockwise Loop"
            Verified(db, "Pasir Panjang", "CCL", "Marina Bay", new TimeOnly(23, 8)),
            Verified(db, "Pasir Panjang", "CCL", "Mountbatten", new TimeOnly(23, 47)),
            Verified(db, "Pasir Panjang", "CCL", "Bartley", new TimeOnly(0, 9)),

            // Stadium, Circle Line
            Verified(db, "Stadium", "CCL", "Bayfront", new TimeOnly(22, 56)),   // "Anticlockwise Loop"
            Verified(db, "Stadium", "CCL", "Pasir Panjang", new TimeOnly(23, 25)),
            Verified(db, "Stadium", "CCL", "one-north", new TimeOnly(23, 42)),
            Verified(db, "Stadium", "CCL", "Caldecott", new TimeOnly(0, 13)),
            Verified(db, "Stadium", "CCL", "Tai Seng", new TimeOnly(0, 19)),
            Verified(db, "Stadium", "CCL", "Dhoby Ghaut", new TimeOnly(23, 52)), // "Clockwise Loop"
            Verified(db, "Stadium", "CCL", "Marina Bay", new TimeOnly(23, 55)),

            // Dakota, Circle Line
            Verified(db, "Dakota", "CCL", "Bayfront", new TimeOnly(23, 0)),   // "Anticlockwise Loop"
            Verified(db, "Dakota", "CCL", "Pasir Panjang", new TimeOnly(23, 29)),
            Verified(db, "Dakota", "CCL", "one-north", new TimeOnly(23, 46)),
            Verified(db, "Dakota", "CCL", "Caldecott", new TimeOnly(0, 17)),
            Verified(db, "Dakota", "CCL", "Tai Seng", new TimeOnly(0, 23)),
            Verified(db, "Dakota", "CCL", "Dhoby Ghaut", new TimeOnly(23, 37)), // "Clockwise Loop"
            Verified(db, "Dakota", "CCL", "Marina Bay", new TimeOnly(23, 52)),
            Verified(db, "Dakota", "CCL", "Mountbatten", new TimeOnly(0, 29)),

            // Nicoll Highway, Circle Line
            Verified(db, "Nicoll Highway", "CCL", "Bayfront", new TimeOnly(22, 54)),   // "Anticlockwise Loop"
            Verified(db, "Nicoll Highway", "CCL", "Pasir Panjang", new TimeOnly(23, 23)),
            Verified(db, "Nicoll Highway", "CCL", "one-north", new TimeOnly(23, 40)),
            Verified(db, "Nicoll Highway", "CCL", "Caldecott", new TimeOnly(0, 11)),
            Verified(db, "Nicoll Highway", "CCL", "Tai Seng", new TimeOnly(0, 16)),
            Verified(db, "Nicoll Highway", "CCL", "Stadium", new TimeOnly(0, 4)),
            Verified(db, "Nicoll Highway", "CCL", "Dhoby Ghaut", new TimeOnly(23, 54)), // "Clockwise Loop"
            Verified(db, "Nicoll Highway", "CCL", "Marina Bay", new TimeOnly(23, 58)),

            // Caldecott, Circle Line + Thomson-East Coast Line
            Verified(db, "Caldecott", "CCL", "Dhoby Ghaut", new TimeOnly(23, 30)),
            Verified(db, "Caldecott", "CCL", "Mountbatten", new TimeOnly(0, 7)),
            Verified(db, "Caldecott", "CCL", "Bartley", new TimeOnly(0, 30)),
            Verified(db, "Caldecott", "CCL", "HarbourFront", new TimeOnly(23, 23)),
            Verified(db, "Caldecott", "CCL", "Pasir Panjang", new TimeOnly(23, 53)),
            Verified(db, "Caldecott", "CCL", "one-north", new TimeOnly(0, 9)),
            Verified(db, "Caldecott", "TEL", "Woodlands North", new TimeOnly(0, 23)),
            Verified(db, "Caldecott", "TEL", "Bayshore", new TimeOnly(23, 52)),
            Verified(db, "Caldecott", "TEL", "Marine Terrace", new TimeOnly(23, 58)),
            Verified(db, "Caldecott", "TEL", "Gardens by the Bay", new TimeOnly(0, 4)),
            Verified(db, "Caldecott", "TEL", "Outram Park", new TimeOnly(0, 10)),
            Verified(db, "Caldecott", "TEL", "Orchard", new TimeOnly(0, 22)),

            // Lorong Chuan, Circle Line
            Verified(db, "Lorong Chuan", "CCL", "Bayfront", new TimeOnly(23, 15)),   // "Anticlockwise Loop"
            Verified(db, "Lorong Chuan", "CCL", "Pasir Panjang", new TimeOnly(23, 43)),
            Verified(db, "Lorong Chuan", "CCL", "one-north", new TimeOnly(0, 0)),
            Verified(db, "Lorong Chuan", "CCL", "Caldecott", new TimeOnly(0, 31)),
            Verified(db, "Lorong Chuan", "CCL", "Dhoby Ghaut", new TimeOnly(23, 23)), // "Clockwise Loop"
            Verified(db, "Lorong Chuan", "CCL", "Marina Bay", new TimeOnly(23, 37)),
            Verified(db, "Lorong Chuan", "CCL", "Mountbatten", new TimeOnly(0, 15)),
            Verified(db, "Lorong Chuan", "CCL", "Bartley", new TimeOnly(0, 37)),

            // Toa Payoh, North-South Line
            Verified(db, "Toa Payoh", "NSL", "Jurong East", new TimeOnly(0, 9)),
            Verified(db, "Toa Payoh", "NSL", "Kranji", new TimeOnly(0, 25)),
            Verified(db, "Toa Payoh", "NSL", "Marina South Pier", new TimeOnly(23, 39)),

            // Braddell, North-South Line
            Verified(db, "Braddell", "NSL", "Jurong East", new TimeOnly(0, 12)),
            Verified(db, "Braddell", "NSL", "Kranji", new TimeOnly(0, 27)),
            Verified(db, "Braddell", "NSL", "Marina South Pier", new TimeOnly(23, 36)),
            Verified(db, "Braddell", "NSL", "Toa Payoh", new TimeOnly(0, 18)),

            // Yio Chu Kang, North-South Line
            Verified(db, "Yio Chu Kang", "NSL", "Jurong East", new TimeOnly(0, 20)),
            Verified(db, "Yio Chu Kang", "NSL", "Kranji", new TimeOnly(0, 36)),
            Verified(db, "Yio Chu Kang", "NSL", "Marina South Pier", new TimeOnly(23, 28)),
            Verified(db, "Yio Chu Kang", "NSL", "Toa Payoh", new TimeOnly(0, 10)),
            Verified(db, "Yio Chu Kang", "NSL", "Ang Mo Kio", new TimeOnly(0, 59)),

            // Kembangan, East-West Line
            Verified(db, "Kembangan", "EWL", "Pasir Ris", new TimeOnly(0, 27)),
            Verified(db, "Kembangan", "EWL", "Tanah Merah", new TimeOnly(23, 43)), // "Changi Airport via Tanah Merah"
            Verified(db, "Kembangan", "EWL", "Tuas Link", new TimeOnly(23, 39)),

            // Eunos, East-West Line
            Verified(db, "Eunos", "EWL", "Pasir Ris", new TimeOnly(0, 25)),
            Verified(db, "Eunos", "EWL", "Tanah Merah", new TimeOnly(23, 41)), // "Changi Airport via Tanah Merah"
            Verified(db, "Eunos", "EWL", "Tuas Link", new TimeOnly(23, 41)),

            // Aljunied, East-West Line
            Verified(db, "Aljunied", "EWL", "Pasir Ris", new TimeOnly(0, 20)),
            Verified(db, "Aljunied", "EWL", "Tanah Merah", new TimeOnly(23, 36)), // "Changi Airport via Tanah Merah"
            Verified(db, "Aljunied", "EWL", "Tuas Link", new TimeOnly(23, 46)),

            // Lavender, East-West Line
            Verified(db, "Lavender", "EWL", "Pasir Ris", new TimeOnly(0, 15)),
            Verified(db, "Lavender", "EWL", "Tanah Merah", new TimeOnly(23, 31)), // "Changi Airport via Tanah Merah"
            Verified(db, "Lavender", "EWL", "Tuas Link", new TimeOnly(23, 50)),

            // Kallang, East-West Line
            Verified(db, "Kallang", "EWL", "Pasir Ris", new TimeOnly(0, 17)),
            Verified(db, "Kallang", "EWL", "Tanah Merah", new TimeOnly(23, 33)), // "Changi Airport via Tanah Merah"
            Verified(db, "Kallang", "EWL", "Tuas Link", new TimeOnly(23, 48)),

            // Tiong Bahru, East-West Line
            Verified(db, "Tiong Bahru", "EWL", "Pasir Ris", new TimeOnly(0, 0)),
            Verified(db, "Tiong Bahru", "EWL", "Tanah Merah", new TimeOnly(23, 16)), // "Changi Airport via Tanah Merah"
            Verified(db, "Tiong Bahru", "EWL", "Tuas Link", new TimeOnly(0, 5)),

            // Ubi, Downtown Line
            Verified(db, "Ubi", "DTL", "Bukit Panjang", new TimeOnly(23, 58)),
            Verified(db, "Ubi", "DTL", "Expo", new TimeOnly(0, 25)),

            // Bukit Panjang, Downtown Line
            // (Bukit Panjang LRT also interchanges here — not modeled with
            // real timing data yet, same caveat as other LRT stations.)
            Verified(db, "Bukit Panjang", "DTL", "Expo", new TimeOnly(23, 35)),

            // Bukit Gombak, North-South Line
            Verified(db, "Bukit Gombak", "NSL", "Jurong East", new TimeOnly(0, 57)),
            Verified(db, "Bukit Gombak", "NSL", "Marina South Pier", new TimeOnly(22, 51)),
            Verified(db, "Bukit Gombak", "NSL", "Toa Payoh", new TimeOnly(23, 33)),
            Verified(db, "Bukit Gombak", "NSL", "Ang Mo Kio", new TimeOnly(0, 22)),

            // Bukit Batok, North-South Line
            Verified(db, "Bukit Batok", "NSL", "Jurong East", new TimeOnly(1, 0)),
            Verified(db, "Bukit Batok", "NSL", "Marina South Pier", new TimeOnly(22, 49)),
            Verified(db, "Bukit Batok", "NSL", "Toa Payoh", new TimeOnly(23, 31)),
            Verified(db, "Bukit Batok", "NSL", "Ang Mo Kio", new TimeOnly(0, 20)),

            // Chinese Garden, East-West Line
            Verified(db, "Chinese Garden", "EWL", "Pasir Ris", new TimeOnly(23, 39)),
            Verified(db, "Chinese Garden", "EWL", "Tanah Merah", new TimeOnly(22, 54)), // "Changi Airport via Tanah Merah"
            Verified(db, "Chinese Garden", "EWL", "Tuas Link", new TimeOnly(0, 27)),

            // Lakeside, East-West Line
            Verified(db, "Lakeside", "EWL", "Pasir Ris", new TimeOnly(23, 37)),
            Verified(db, "Lakeside", "EWL", "Tanah Merah", new TimeOnly(22, 52)), // "Changi Airport via Tanah Merah"
            Verified(db, "Lakeside", "EWL", "Tuas Link", new TimeOnly(0, 29)),

            // Boon Lay, East-West Line
            Verified(db, "Boon Lay", "EWL", "Pasir Ris", new TimeOnly(23, 34)),
            Verified(db, "Boon Lay", "EWL", "Tanah Merah", new TimeOnly(22, 49)), // "Changi Airport via Tanah Merah"
            Verified(db, "Boon Lay", "EWL", "Tuas Link", new TimeOnly(0, 32)),

            // Marsiling, North-South Line
            Verified(db, "Marsiling", "NSL", "Jurong East", new TimeOnly(0, 43)),
            Verified(db, "Marsiling", "NSL", "Kranji", new TimeOnly(0, 58)),
            Verified(db, "Marsiling", "NSL", "Marina South Pier", new TimeOnly(23, 6)),
            Verified(db, "Marsiling", "NSL", "Toa Payoh", new TimeOnly(23, 48)),
            Verified(db, "Marsiling", "NSL", "Ang Mo Kio", new TimeOnly(0, 37)),

            // Kranji, North-South Line
            Verified(db, "Kranji", "NSL", "Jurong East", new TimeOnly(0, 45)),
            Verified(db, "Kranji", "NSL", "Marina South Pier", new TimeOnly(23, 3)),
            Verified(db, "Kranji", "NSL", "Toa Payoh", new TimeOnly(23, 45)),
            Verified(db, "Kranji", "NSL", "Ang Mo Kio", new TimeOnly(0, 34)),

            // Yew Tee, North-South Line
            Verified(db, "Yew Tee", "NSL", "Jurong East", new TimeOnly(0, 50)),
            Verified(db, "Yew Tee", "NSL", "Marina South Pier", new TimeOnly(22, 58)),
            Verified(db, "Yew Tee", "NSL", "Toa Payoh", new TimeOnly(23, 40)),
            Verified(db, "Yew Tee", "NSL", "Ang Mo Kio", new TimeOnly(0, 29)),

            // Admiralty, North-South Line
            Verified(db, "Admiralty", "NSL", "Jurong East", new TimeOnly(0, 37)),
            Verified(db, "Admiralty", "NSL", "Kranji", new TimeOnly(0, 53)),
            Verified(db, "Admiralty", "NSL", "Marina South Pier", new TimeOnly(23, 11)),
            Verified(db, "Admiralty", "NSL", "Toa Payoh", new TimeOnly(23, 53)),
            Verified(db, "Admiralty", "NSL", "Ang Mo Kio", new TimeOnly(0, 42)),

            // Sixth Avenue, Downtown Line
            Verified(db, "Sixth Avenue", "DTL", "Bukit Panjang", new TimeOnly(0, 36)),
            Verified(db, "Sixth Avenue", "DTL", "Expo", new TimeOnly(23, 46)),

            // Tan Kah Kee, Downtown Line
            Verified(db, "Tan Kah Kee", "DTL", "Bukit Panjang", new TimeOnly(0, 34)),
            Verified(db, "Tan Kah Kee", "DTL", "Expo", new TimeOnly(23, 48)),

            // King Albert Park, Downtown Line
            Verified(db, "King Albert Park", "DTL", "Bukit Panjang", new TimeOnly(0, 38)),
            Verified(db, "King Albert Park", "DTL", "Expo", new TimeOnly(23, 44)),

            // Beauty World, Downtown Line
            Verified(db, "Beauty World", "DTL", "Bukit Panjang", new TimeOnly(0, 40)),
            Verified(db, "Beauty World", "DTL", "Expo", new TimeOnly(23, 42)),

            // Hillview, Downtown Line
            Verified(db, "Hillview", "DTL", "Bukit Panjang", new TimeOnly(0, 45)),
            Verified(db, "Hillview", "DTL", "Expo", new TimeOnly(23, 38)),

            // Cashew, Downtown Line
            Verified(db, "Cashew", "DTL", "Bukit Panjang", new TimeOnly(0, 46)),
            Verified(db, "Cashew", "DTL", "Expo", new TimeOnly(23, 37)),

            // Joo Koon, East-West Line
            Verified(db, "Joo Koon", "EWL", "Pasir Ris", new TimeOnly(23, 29)),
            Verified(db, "Joo Koon", "EWL", "Tanah Merah", new TimeOnly(22, 44)), // "Changi Airport via Tanah Merah"
            Verified(db, "Joo Koon", "EWL", "Tuas Link", new TimeOnly(0, 38)),

            // Gul Circle, East-West Line
            Verified(db, "Gul Circle", "EWL", "Pasir Ris", new TimeOnly(23, 26)),
            Verified(db, "Gul Circle", "EWL", "Tanah Merah", new TimeOnly(22, 41)), // "Changi Airport via Tanah Merah"
            Verified(db, "Gul Circle", "EWL", "Tuas Link", new TimeOnly(0, 41)),

            // Tuas Crescent, East-West Line
            Verified(db, "Tuas Crescent", "EWL", "Pasir Ris", new TimeOnly(23, 23)),
            Verified(db, "Tuas Crescent", "EWL", "Tanah Merah", new TimeOnly(22, 38)), // "Changi Airport via Tanah Merah"
            Verified(db, "Tuas Crescent", "EWL", "Tuas Link", new TimeOnly(0, 44)),

            // Tuas West Road, East-West Line
            Verified(db, "Tuas West Road", "EWL", "Pasir Ris", new TimeOnly(23, 22)),
            Verified(db, "Tuas West Road", "EWL", "Tanah Merah", new TimeOnly(22, 36)), // "Changi Airport via Tanah Merah"
            Verified(db, "Tuas West Road", "EWL", "Tuas Link", new TimeOnly(0, 46)),

            // Sembawang, North-South Line
            Verified(db, "Sembawang", "NSL", "Jurong East", new TimeOnly(0, 34)),
            Verified(db, "Sembawang", "NSL", "Kranji", new TimeOnly(0, 49)),
            Verified(db, "Sembawang", "NSL", "Marina South Pier", new TimeOnly(23, 19)),
            Verified(db, "Sembawang", "NSL", "Toa Payoh", new TimeOnly(23, 56)),
            Verified(db, "Sembawang", "NSL", "Ang Mo Kio", new TimeOnly(0, 45)),

            // Canberra, North-South Line
            Verified(db, "Canberra", "NSL", "Jurong East", new TimeOnly(0, 31)),
            Verified(db, "Canberra", "NSL", "Kranji", new TimeOnly(0, 47)),
            Verified(db, "Canberra", "NSL", "Marina South Pier", new TimeOnly(23, 17)),
            Verified(db, "Canberra", "NSL", "Toa Payoh", new TimeOnly(23, 59)),
            Verified(db, "Canberra", "NSL", "Ang Mo Kio", new TimeOnly(0, 48)),

            // Khatib, North-South Line
            Verified(db, "Khatib", "NSL", "Jurong East", new TimeOnly(0, 26)),
            Verified(db, "Khatib", "NSL", "Kranji", new TimeOnly(0, 41)),
            Verified(db, "Khatib", "NSL", "Marina South Pier", new TimeOnly(23, 22)),
            Verified(db, "Khatib", "NSL", "Toa Payoh", new TimeOnly(0, 4)),
            Verified(db, "Khatib", "NSL", "Ang Mo Kio", new TimeOnly(0, 53)),

            // Kent Ridge, Circle Line
            Verified(db, "Kent Ridge", "CCL", "Bayfront", new TimeOnly(23, 39)),   // "Anticlockwise Loop"
            Verified(db, "Kent Ridge", "CCL", "Pasir Panjang", new TimeOnly(0, 6)),
            Verified(db, "Kent Ridge", "CCL", "Dhoby Ghaut", new TimeOnly(22, 59)), // "Clockwise Loop"
            Verified(db, "Kent Ridge", "CCL", "Marina Bay", new TimeOnly(23, 13)),
            Verified(db, "Kent Ridge", "CCL", "Mountbatten", new TimeOnly(23, 52)),
            Verified(db, "Kent Ridge", "CCL", "Bartley", new TimeOnly(0, 14)),

            // Haw Par Villa, Circle Line
            Verified(db, "Haw Par Villa", "CCL", "Bayfront", new TimeOnly(23, 41)),   // "Anticlockwise Loop"
            Verified(db, "Haw Par Villa", "CCL", "Pasir Panjang", new TimeOnly(0, 9)),
            Verified(db, "Haw Par Villa", "CCL", "Dhoby Ghaut", new TimeOnly(22, 56)), // "Clockwise Loop"
            Verified(db, "Haw Par Villa", "CCL", "Marina Bay", new TimeOnly(23, 10)),
            Verified(db, "Haw Par Villa", "CCL", "Mountbatten", new TimeOnly(23, 49)),
            Verified(db, "Haw Par Villa", "CCL", "Bartley", new TimeOnly(0, 11)),

            // Labrador Park, Circle Line
            Verified(db, "Labrador Park", "CCL", "Bayfront", new TimeOnly(23, 46)),   // "Anticlockwise Loop"
            Verified(db, "Labrador Park", "CCL", "Dhoby Ghaut", new TimeOnly(22, 52)), // "Clockwise Loop"
            Verified(db, "Labrador Park", "CCL", "Marina Bay", new TimeOnly(23, 6)),
            Verified(db, "Labrador Park", "CCL", "Mountbatten", new TimeOnly(23, 45)),
            Verified(db, "Labrador Park", "CCL", "Bartley", new TimeOnly(0, 6)),

            // Telok Blangah, Circle Line
            Verified(db, "Telok Blangah", "CCL", "Bayfront", new TimeOnly(23, 47)),   // "Anticlockwise Loop"
            Verified(db, "Telok Blangah", "CCL", "Dhoby Ghaut", new TimeOnly(22, 50)), // "Clockwise Loop"
            Verified(db, "Telok Blangah", "CCL", "Marina Bay", new TimeOnly(23, 4)),
            Verified(db, "Telok Blangah", "CCL", "Mountbatten", new TimeOnly(23, 43)),
            Verified(db, "Telok Blangah", "CCL", "Bartley", new TimeOnly(0, 4)),

            // Keppel, Circle Line
            Verified(db, "Keppel", "CCL", "Bayfront", new TimeOnly(23, 53)),   // "Anticlockwise Loop"
            Verified(db, "Keppel", "CCL", "Dhoby Ghaut", new TimeOnly(22, 44)), // "Clockwise Loop"
            Verified(db, "Keppel", "CCL", "Marina Bay", new TimeOnly(22, 57)),
            Verified(db, "Keppel", "CCL", "Mountbatten", new TimeOnly(23, 37)),
            Verified(db, "Keppel", "CCL", "Bartley", new TimeOnly(23, 59)),

            // Cantonment, Circle Line
            Verified(db, "Cantonment", "CCL", "Bayfront", new TimeOnly(23, 55)),   // "Anticlockwise Loop"
            Verified(db, "Cantonment", "CCL", "Dhoby Ghaut", new TimeOnly(22, 43)), // "Clockwise Loop"
            Verified(db, "Cantonment", "CCL", "Marina Bay", new TimeOnly(22, 55)),
            Verified(db, "Cantonment", "CCL", "Mountbatten", new TimeOnly(23, 35)),
            Verified(db, "Cantonment", "CCL", "Bartley", new TimeOnly(23, 57)),

            // Prince Edward Road, Circle Line
            Verified(db, "Prince Edward Road", "CCL", "Caldecott", new TimeOnly(23, 58)),   // "Anticlockwise Loop"
            Verified(db, "Prince Edward Road", "CCL", "Dhoby Ghaut", new TimeOnly(22, 40)), // "Clockwise Loop"
            Verified(db, "Prince Edward Road", "CCL", "Marina Bay", new TimeOnly(22, 53)),
            Verified(db, "Prince Edward Road", "CCL", "Mountbatten", new TimeOnly(23, 33)),
            Verified(db, "Prince Edward Road", "CCL", "Bartley", new TimeOnly(23, 55)),

            // Hougang, North East Line
            Verified(db, "Hougang", "NEL", "HarbourFront", new TimeOnly(23, 35)),
            Verified(db, "Hougang", "NEL", "Punggol Coast", new TimeOnly(0, 23)),

            // Buangkok, North East Line
            Verified(db, "Buangkok", "NEL", "HarbourFront", new TimeOnly(23, 32)),
            Verified(db, "Buangkok", "NEL", "Punggol Coast", new TimeOnly(0, 25)),

            // Clarke Quay, North East Line
            Verified(db, "Clarke Quay", "NEL", "HarbourFront", new TimeOnly(23, 55)),
            Verified(db, "Clarke Quay", "NEL", "Punggol Coast", new TimeOnly(0, 2)),

            // Great World, Thomson-East Coast Line
            Verified(db, "Great World", "TEL", "Woodlands North", new TimeOnly(0, 10)),
            Verified(db, "Great World", "TEL", "Caldecott", new TimeOnly(0, 22)),
            Verified(db, "Great World", "TEL", "Orchard", new TimeOnly(0, 28)),
            Verified(db, "Great World", "TEL", "Bayshore", new TimeOnly(0, 5)),
            Verified(db, "Great World", "TEL", "Marine Terrace", new TimeOnly(0, 10)),
            Verified(db, "Great World", "TEL", "Gardens by the Bay", new TimeOnly(0, 16)),
            Verified(db, "Great World", "TEL", "Outram Park", new TimeOnly(0, 22)),

            // Havelock, Thomson-East Coast Line
            Verified(db, "Havelock", "TEL", "Woodlands North", new TimeOnly(0, 9)),
            Verified(db, "Havelock", "TEL", "Caldecott", new TimeOnly(0, 21)),
            Verified(db, "Havelock", "TEL", "Orchard", new TimeOnly(0, 27)),
            Verified(db, "Havelock", "TEL", "Bayshore", new TimeOnly(0, 7)),
            Verified(db, "Havelock", "TEL", "Marine Terrace", new TimeOnly(0, 12)),
            Verified(db, "Havelock", "TEL", "Gardens by the Bay", new TimeOnly(0, 18)),
            Verified(db, "Havelock", "TEL", "Outram Park", new TimeOnly(0, 24)),

            // Maxwell, Thomson-East Coast Line
            Verified(db, "Maxwell", "TEL", "Woodlands North", new TimeOnly(0, 5)),
            Verified(db, "Maxwell", "TEL", "Caldecott", new TimeOnly(0, 17)),
            Verified(db, "Maxwell", "TEL", "Orchard", new TimeOnly(0, 23)),
            Verified(db, "Maxwell", "TEL", "Bayshore", new TimeOnly(0, 10)),
            Verified(db, "Maxwell", "TEL", "Marine Terrace", new TimeOnly(0, 15)),
            Verified(db, "Maxwell", "TEL", "Gardens by the Bay", new TimeOnly(0, 21)),

            // Shenton Way, Thomson-East Coast Line
            Verified(db, "Shenton Way", "TEL", "Woodlands North", new TimeOnly(0, 3)),
            Verified(db, "Shenton Way", "TEL", "Caldecott", new TimeOnly(0, 15)),
            Verified(db, "Shenton Way", "TEL", "Orchard", new TimeOnly(0, 21)),
            Verified(db, "Shenton Way", "TEL", "Bayshore", new TimeOnly(0, 12)),
            Verified(db, "Shenton Way", "TEL", "Marine Terrace", new TimeOnly(0, 17)),
            Verified(db, "Shenton Way", "TEL", "Gardens by the Bay", new TimeOnly(0, 23)),

            // Napier, Thomson-East Coast Line
            Verified(db, "Napier", "TEL", "Woodlands North", new TimeOnly(0, 16)),
            Verified(db, "Napier", "TEL", "Caldecott", new TimeOnly(0, 28)),
            Verified(db, "Napier", "TEL", "Bayshore", new TimeOnly(23, 59)),
            Verified(db, "Napier", "TEL", "Marine Terrace", new TimeOnly(0, 4)),
            Verified(db, "Napier", "TEL", "Gardens by the Bay", new TimeOnly(0, 10)),
            Verified(db, "Napier", "TEL", "Outram Park", new TimeOnly(0, 16)),
            Verified(db, "Napier", "TEL", "Orchard", new TimeOnly(0, 28)),

            // Orchard Boulevard, Thomson-East Coast Line
            Verified(db, "Orchard Boulevard", "TEL", "Woodlands North", new TimeOnly(0, 14)),
            Verified(db, "Orchard Boulevard", "TEL", "Caldecott", new TimeOnly(0, 26)),
            Verified(db, "Orchard Boulevard", "TEL", "Bayshore", new TimeOnly(0, 1)),
            Verified(db, "Orchard Boulevard", "TEL", "Marine Terrace", new TimeOnly(0, 6)),
            Verified(db, "Orchard Boulevard", "TEL", "Gardens by the Bay", new TimeOnly(0, 12)),
            Verified(db, "Orchard Boulevard", "TEL", "Outram Park", new TimeOnly(0, 18)),
            Verified(db, "Orchard Boulevard", "TEL", "Orchard", new TimeOnly(0, 30)),

            // Tanjong Rhu, Thomson-East Coast Line
            Verified(db, "Tanjong Rhu", "TEL", "Woodlands North", new TimeOnly(23, 55)),
            Verified(db, "Tanjong Rhu", "TEL", "Caldecott", new TimeOnly(0, 7)),
            Verified(db, "Tanjong Rhu", "TEL", "Orchard", new TimeOnly(0, 13)),
            Verified(db, "Tanjong Rhu", "TEL", "Gardens by the Bay", new TimeOnly(0, 19)),
            Verified(db, "Tanjong Rhu", "TEL", "Bayshore", new TimeOnly(0, 21)),
            Verified(db, "Tanjong Rhu", "TEL", "Marine Terrace", new TimeOnly(0, 26)),

            // Woodlands North, Thomson-East Coast Line (terminus — one direction only)
            Verified(db, "Woodlands North", "TEL", "Bayshore", new TimeOnly(23, 30)),
            Verified(db, "Woodlands North", "TEL", "Marine Terrace", new TimeOnly(23, 36)),
            Verified(db, "Woodlands North", "TEL", "Gardens by the Bay", new TimeOnly(23, 42)),
            Verified(db, "Woodlands North", "TEL", "Outram Park", new TimeOnly(23, 48)),
            Verified(db, "Woodlands North", "TEL", "Orchard", new TimeOnly(0, 0)),
            Verified(db, "Woodlands North", "TEL", "Caldecott", new TimeOnly(0, 6)),

            // Woodlands South, Thomson-East Coast Line
            Verified(db, "Woodlands South", "TEL", "Woodlands North", new TimeOnly(0, 40)),
            Verified(db, "Woodlands South", "TEL", "Bayshore", new TimeOnly(23, 35)),
            Verified(db, "Woodlands South", "TEL", "Marine Terrace", new TimeOnly(23, 41)),
            Verified(db, "Woodlands South", "TEL", "Gardens by the Bay", new TimeOnly(23, 47)),
            Verified(db, "Woodlands South", "TEL", "Outram Park", new TimeOnly(23, 53)),
            Verified(db, "Woodlands South", "TEL", "Orchard", new TimeOnly(0, 5)),
            Verified(db, "Woodlands South", "TEL", "Caldecott", new TimeOnly(0, 11)),

            // Springleaf, Thomson-East Coast Line
            Verified(db, "Springleaf", "TEL", "Woodlands North", new TimeOnly(0, 35)),
            Verified(db, "Springleaf", "TEL", "Bayshore", new TimeOnly(23, 39)),
            Verified(db, "Springleaf", "TEL", "Marine Terrace", new TimeOnly(23, 45)),
            Verified(db, "Springleaf", "TEL", "Gardens by the Bay", new TimeOnly(23, 51)),
            Verified(db, "Springleaf", "TEL", "Outram Park", new TimeOnly(23, 57)),
            Verified(db, "Springleaf", "TEL", "Orchard", new TimeOnly(0, 9)),
            Verified(db, "Springleaf", "TEL", "Caldecott", new TimeOnly(0, 15)),

            // Lentor, Thomson-East Coast Line
            Verified(db, "Lentor", "TEL", "Woodlands North", new TimeOnly(0, 32)),
            Verified(db, "Lentor", "TEL", "Bayshore", new TimeOnly(23, 42)),
            Verified(db, "Lentor", "TEL", "Marine Terrace", new TimeOnly(23, 48)),
            Verified(db, "Lentor", "TEL", "Gardens by the Bay", new TimeOnly(23, 54)),
            Verified(db, "Lentor", "TEL", "Outram Park", new TimeOnly(0, 0)),
            Verified(db, "Lentor", "TEL", "Orchard", new TimeOnly(0, 12)),
            Verified(db, "Lentor", "TEL", "Caldecott", new TimeOnly(0, 18)),

            // Mayflower, Thomson-East Coast Line
            Verified(db, "Mayflower", "TEL", "Woodlands North", new TimeOnly(0, 30)),
            Verified(db, "Mayflower", "TEL", "Bayshore", new TimeOnly(23, 45)),
            Verified(db, "Mayflower", "TEL", "Marine Terrace", new TimeOnly(23, 51)),
            Verified(db, "Mayflower", "TEL", "Gardens by the Bay", new TimeOnly(23, 57)),
            Verified(db, "Mayflower", "TEL", "Outram Park", new TimeOnly(0, 3)),
            Verified(db, "Mayflower", "TEL", "Orchard", new TimeOnly(0, 15)),
            Verified(db, "Mayflower", "TEL", "Caldecott", new TimeOnly(0, 21)),

            // Bright Hill, Thomson-East Coast Line
            Verified(db, "Bright Hill", "TEL", "Woodlands North", new TimeOnly(0, 28)),
            Verified(db, "Bright Hill", "TEL", "Bayshore", new TimeOnly(23, 47)),
            Verified(db, "Bright Hill", "TEL", "Marine Terrace", new TimeOnly(23, 53)),
            Verified(db, "Bright Hill", "TEL", "Gardens by the Bay", new TimeOnly(23, 59)),
            Verified(db, "Bright Hill", "TEL", "Outram Park", new TimeOnly(0, 5)),
            Verified(db, "Bright Hill", "TEL", "Orchard", new TimeOnly(0, 17)),
            Verified(db, "Bright Hill", "TEL", "Caldecott", new TimeOnly(0, 23)),

            // Upper Thomson, Thomson-East Coast Line
            Verified(db, "Upper Thomson", "TEL", "Woodlands North", new TimeOnly(0, 26)),
            Verified(db, "Upper Thomson", "TEL", "Bayshore", new TimeOnly(23, 49)),
            Verified(db, "Upper Thomson", "TEL", "Marine Terrace", new TimeOnly(23, 55)),
            Verified(db, "Upper Thomson", "TEL", "Gardens by the Bay", new TimeOnly(0, 1)),
            Verified(db, "Upper Thomson", "TEL", "Outram Park", new TimeOnly(0, 7)),
            Verified(db, "Upper Thomson", "TEL", "Orchard", new TimeOnly(0, 19)),
            Verified(db, "Upper Thomson", "TEL", "Caldecott", new TimeOnly(0, 25)),

            // Mattar, Downtown Line
            Verified(db, "Mattar", "DTL", "Bukit Panjang", new TimeOnly(0, 2)),
            Verified(db, "Mattar", "DTL", "Expo", new TimeOnly(0, 22)),

            // Kaki Bukit, Downtown Line
            Verified(db, "Kaki Bukit", "DTL", "Bukit Panjang", new TimeOnly(23, 56)),
            Verified(db, "Kaki Bukit", "DTL", "Expo", new TimeOnly(0, 27)),

            // Bedok North, Downtown Line
            Verified(db, "Bedok North", "DTL", "Bukit Panjang", new TimeOnly(23, 54)),
            Verified(db, "Bedok North", "DTL", "Expo", new TimeOnly(0, 29)),

            // Katong Park, Thomson-East Coast Line
            Verified(db, "Katong Park", "TEL", "Woodlands North", new TimeOnly(23, 52)),
            Verified(db, "Katong Park", "TEL", "Caldecott", new TimeOnly(0, 4)),
            Verified(db, "Katong Park", "TEL", "Orchard", new TimeOnly(0, 10)),
            Verified(db, "Katong Park", "TEL", "Gardens by the Bay", new TimeOnly(0, 16)),
            Verified(db, "Katong Park", "TEL", "Bayshore", new TimeOnly(0, 23)),
            Verified(db, "Katong Park", "TEL", "Marine Terrace", new TimeOnly(0, 28)),

            // Tanjong Katong, Thomson-East Coast Line
            Verified(db, "Tanjong Katong", "TEL", "Woodlands North", new TimeOnly(23, 50)),
            Verified(db, "Tanjong Katong", "TEL", "Caldecott", new TimeOnly(0, 2)),
            Verified(db, "Tanjong Katong", "TEL", "Orchard", new TimeOnly(0, 8)),
            Verified(db, "Tanjong Katong", "TEL", "Gardens by the Bay", new TimeOnly(0, 14)),
            Verified(db, "Tanjong Katong", "TEL", "Bayshore", new TimeOnly(0, 25)),
            Verified(db, "Tanjong Katong", "TEL", "Marine Terrace", new TimeOnly(0, 30)),

            // Marine Parade, Thomson-East Coast Line
            Verified(db, "Marine Parade", "TEL", "Woodlands North", new TimeOnly(23, 48)),
            Verified(db, "Marine Parade", "TEL", "Caldecott", new TimeOnly(0, 0)),
            Verified(db, "Marine Parade", "TEL", "Orchard", new TimeOnly(0, 6)),
            Verified(db, "Marine Parade", "TEL", "Gardens by the Bay", new TimeOnly(0, 12)),
            Verified(db, "Marine Parade", "TEL", "Bayshore", new TimeOnly(0, 27)),
            Verified(db, "Marine Parade", "TEL", "Marine Terrace", new TimeOnly(0, 32)),

            // Siglap, Thomson-East Coast Line
            Verified(db, "Siglap", "TEL", "Woodlands North", new TimeOnly(23, 44)),
            Verified(db, "Siglap", "TEL", "Caldecott", new TimeOnly(23, 56)),
            Verified(db, "Siglap", "TEL", "Orchard", new TimeOnly(0, 2)),
            Verified(db, "Siglap", "TEL", "Gardens by the Bay", new TimeOnly(0, 8)),
            Verified(db, "Siglap", "TEL", "Marine Terrace", new TimeOnly(0, 22)),
            Verified(db, "Siglap", "TEL", "Bayshore", new TimeOnly(0, 31)),

            // Bedok Reservoir, Downtown Line
            Verified(db, "Bedok Reservoir", "DTL", "Bukit Panjang", new TimeOnly(23, 51)),
            Verified(db, "Bedok Reservoir", "DTL", "Expo", new TimeOnly(0, 31)),

            // Tampines West, Downtown Line
            Verified(db, "Tampines West", "DTL", "Bukit Panjang", new TimeOnly(23, 49)),
            Verified(db, "Tampines West", "DTL", "Expo", new TimeOnly(0, 34)),

            // Tampines East, Downtown Line
            Verified(db, "Tampines East", "DTL", "Bukit Panjang", new TimeOnly(23, 45)),
            Verified(db, "Tampines East", "DTL", "Expo", new TimeOnly(0, 38)),

            // Jalan Besar, Downtown Line
            Verified(db, "Jalan Besar", "DTL", "Bukit Panjang", new TimeOnly(0, 8)),
            Verified(db, "Jalan Besar", "DTL", "Expo", new TimeOnly(0, 15)),

            // Bendemeer, Downtown Line
            Verified(db, "Bendemeer", "DTL", "Bukit Panjang", new TimeOnly(0, 6)),
            Verified(db, "Bendemeer", "DTL", "Expo", new TimeOnly(0, 17)),

            // Geylang Bahru, Downtown Line
            Verified(db, "Geylang Bahru", "DTL", "Bukit Panjang", new TimeOnly(0, 4)),
            Verified(db, "Geylang Bahru", "DTL", "Expo", new TimeOnly(0, 19)),

            // Downtown, Downtown Line
            Verified(db, "Downtown", "DTL", "Bukit Panjang", new TimeOnly(0, 16)),
            Verified(db, "Downtown", "DTL", "Expo", new TimeOnly(0, 7)),

            // Raffles Place, North-South Line + East-West Line
            Verified(db, "Raffles Place", "NSL", "Jurong East", new TimeOnly(23, 53)),
            Verified(db, "Raffles Place", "NSL", "Kranji", new TimeOnly(0, 8)),
            Verified(db, "Raffles Place", "NSL", "Marina South Pier", new TimeOnly(23, 55)),
            Verified(db, "Raffles Place", "EWL", "Pasir Ris", new TimeOnly(0, 8)),
            Verified(db, "Raffles Place", "EWL", "Tanah Merah", new TimeOnly(23, 24)), // "Changi Airport via Tanah Merah"
            Verified(db, "Raffles Place", "EWL", "Tuas Link", new TimeOnly(23, 58)),

            // Novena, North-South Line
            Verified(db, "Novena", "NSL", "Jurong East", new TimeOnly(0, 7)),
            Verified(db, "Novena", "NSL", "Kranji", new TimeOnly(0, 22)),
            Verified(db, "Novena", "NSL", "Marina South Pier", new TimeOnly(23, 41)),

            // Somerset, North-South Line
            Verified(db, "Somerset", "NSL", "Jurong East", new TimeOnly(23, 59)),
            Verified(db, "Somerset", "NSL", "Kranji", new TimeOnly(0, 15)),
            Verified(db, "Somerset", "NSL", "Marina South Pier", new TimeOnly(23, 48)),

            // Chinatown, North East Line + Downtown Line
            Verified(db, "Chinatown", "NEL", "HarbourFront", new TimeOnly(23, 56)),
            Verified(db, "Chinatown", "NEL", "Punggol Coast", new TimeOnly(0, 1)),
            Verified(db, "Chinatown", "DTL", "Bukit Panjang", new TimeOnly(0, 14)),
            Verified(db, "Chinatown", "DTL", "Expo", new TimeOnly(0, 10)),

            // Little India, North East Line + Downtown Line
            Verified(db, "Little India", "NEL", "HarbourFront", new TimeOnly(23, 50)),
            Verified(db, "Little India", "NEL", "Punggol Coast", new TimeOnly(0, 7)),
            Verified(db, "Little India", "DTL", "Bukit Panjang", new TimeOnly(0, 26)),
            Verified(db, "Little India", "DTL", "Expo", new TimeOnly(23, 58)),

            // Woodlands, North-South Line + Thomson-East Coast Line
            Verified(db, "Woodlands", "NSL", "Jurong East", new TimeOnly(0, 40)),
            Verified(db, "Woodlands", "NSL", "Kranji", new TimeOnly(0, 55)),
            Verified(db, "Woodlands", "NSL", "Marina South Pier", new TimeOnly(23, 8)),
            Verified(db, "Woodlands", "NSL", "Toa Payoh", new TimeOnly(23, 50)),
            Verified(db, "Woodlands", "NSL", "Ang Mo Kio", new TimeOnly(0, 39)),
            Verified(db, "Woodlands", "TEL", "Woodlands North", new TimeOnly(0, 42)),
            Verified(db, "Woodlands", "TEL", "Bayshore", new TimeOnly(23, 33)),
            Verified(db, "Woodlands", "TEL", "Marine Terrace", new TimeOnly(23, 38)),
            Verified(db, "Woodlands", "TEL", "Gardens by the Bay", new TimeOnly(23, 44)),
            Verified(db, "Woodlands", "TEL", "Outram Park", new TimeOnly(23, 50)),
            Verified(db, "Woodlands", "TEL", "Orchard", new TimeOnly(0, 2)),
            Verified(db, "Woodlands", "TEL", "Caldecott", new TimeOnly(0, 8)),

            // Newton, North-South Line + Downtown Line
            Verified(db, "Newton", "NSL", "Jurong East", new TimeOnly(0, 4)),
            Verified(db, "Newton", "NSL", "Kranji", new TimeOnly(0, 20)),
            Verified(db, "Newton", "NSL", "Marina South Pier", new TimeOnly(23, 44)),
            Verified(db, "Newton", "DTL", "Bukit Panjang", new TimeOnly(0, 28)),
            Verified(db, "Newton", "DTL", "Expo", new TimeOnly(23, 55)),

            // Sengkang, North East Line
            // (Sengkang LRT East/West Loop also interchange here — the live
            // site labels these by "Clockwise/Anti-Clockwise via <station>"
            // rather than a single towards-station, which doesn't map cleanly
            // onto this app's simplified linear LRT model, so left as placeholder.)
            Verified(db, "Sengkang", "NEL", "HarbourFront", new TimeOnly(23, 30)),
            Verified(db, "Sengkang", "NEL", "Punggol Coast", new TimeOnly(0, 27)),

            // Punggol, North East Line
            // (Punggol LRT East/West Loop — same LRT-labeling caveat as Sengkang above.)
            Verified(db, "Punggol", "NEL", "HarbourFront", new TimeOnly(23, 28)),
            Verified(db, "Punggol", "NEL", "Punggol Coast", new TimeOnly(0, 30)),

            // Farrer Park, North East Line
            Verified(db, "Farrer Park", "NEL", "HarbourFront", new TimeOnly(23, 48)),
            Verified(db, "Farrer Park", "NEL", "Punggol Coast", new TimeOnly(0, 9)),

            // Upper Changi, Downtown Line
            Verified(db, "Upper Changi", "DTL", "Bukit Panjang", new TimeOnly(23, 41)),
            Verified(db, "Upper Changi", "DTL", "Expo", new TimeOnly(0, 41)),

            // City Hall, North-South Line
            Verified(db, "City Hall", "NSL", "Marina South Pier", new TimeOnly(23, 53)),
            Verified(db, "City Hall", "NSL", "Jurong East", new TimeOnly(23, 55)),
            // City Hall, North-South Line — was missing; added after re-checking the live site.
            Verified(db, "City Hall", "NSL", "Kranji", new TimeOnly(0, 11)),
            // City Hall, East-West Line — the critical one: last THROUGH train
            // via Tanah Merah to the Changi Airport branch
            Verified(db, "City Hall", "EWL", "Tanah Merah", new TimeOnly(23, 26)),
            Verified(db, "City Hall", "EWL", "Pasir Ris", new TimeOnly(0, 10)),
            Verified(db, "City Hall", "EWL", "Tuas Link", new TimeOnly(23, 55)),

            // Tanah Merah, Changi Airport branch — last train to Expo/Changi Airport
            Verified(db, "Tanah Merah", "CGL", "Changi Airport", new TimeOnly(23, 50)),
            // Tanah Merah, East-West Line, other directions
            Verified(db, "Tanah Merah", "EWL", "Pasir Ris", new TimeOnly(0, 33)),
            Verified(db, "Tanah Merah", "EWL", "Tuas Link", new TimeOnly(23, 33))
        );
    }

    /// Everything not covered above gets a flat, clearly-fake 23:30 placeholder
    /// per direction so the routing engine always returns *something* — but
    /// callers get IsVerified=false back and the API/UI must surface that.
    private static void SeedPlaceholders(AppDbContext db)
    {
        var placeholder = new TimeOnly(23, 30);
        var existing = db.LastTrainServices
            .Select(x => new { x.StationId, x.LineId })
            .ToHashSet();

        foreach (var line in db.Lines.ToList())
        {
            var stops = db.StationLines.Where(sl => sl.LineId == line.Id)
                .OrderBy(sl => sl.SequenceIndex).ToList();
            if (stops.Count == 0) continue;

            var firstTerminus = stops.First().StationId;
            var lastTerminus = stops.Last().StationId;

            foreach (var stop in stops)
            {
                if (existing.Contains(new { stop.StationId, LineId = line.Id })) continue;

                if (stop.StationId != lastTerminus)
                    db.LastTrainServices.Add(new LastTrainService
                    {
                        StationId = stop.StationId,
                        LineId = line.Id,
                        TowardsStationId = lastTerminus,
                        LastTrainTime = placeholder,
                        IsVerified = false
                    });

                if (stop.StationId != firstTerminus)
                    db.LastTrainServices.Add(new LastTrainService
                    {
                        StationId = stop.StationId,
                        LineId = line.Id,
                        TowardsStationId = firstTerminus,
                        LastTrainTime = placeholder,
                        IsVerified = false
                    });
            }
        }
    }

    private static void SeedPublicHolidays(AppDbContext db)
    {
        // 2026 SG public holidays — not used by the last-train calc itself
        // (last train is day-invariant) but here for a future first-train feature.
        var ph2026 = new (string, string)[]
        {
            ("2026-01-01","New Year's Day"), ("2026-02-17","Chinese New Year"),
            ("2026-02-18","Chinese New Year"), ("2026-03-21","Hari Raya Puasa"),
            ("2026-04-03","Good Friday"), ("2026-05-01","Labour Day"),
            ("2026-05-27","Hari Raya Haji"), ("2026-05-31","Vesak Day"),
            ("2026-08-09","National Day"), ("2026-11-08","Deepavali"),
            ("2026-12-25","Christmas Day"),
        };
        foreach (var (date, name) in ph2026)
            db.PublicHolidays.Add(new PublicHoliday { Date = DateOnly.Parse(date), Name = name });
    }
}
