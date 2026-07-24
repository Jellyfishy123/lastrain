# Last Train Home

Answers one question: "I'm at station A, right now, heading to station B —
what's the latest I can leave to make it home, and where do I need to change?"

## Why a database, and why this isn't a simple lookup

Last train times are per-station, per-line, **per direction/branch**
("Train to Marina South Pier" vs "Train to Jurong East"), and — this is the
important bit — **the very last train from your station can miss a later
connection**. Real example, pulled straight from sgtrains.com:

- Bishan's last NS-line train (towards Marina South Pier) leaves at **23:34**.
- But City Hall's last through-train towards the Changi Airport branch
  (via Tanah Merah) leaves at **23:26** — which the 23:34 Bishan train
  arrives too late to catch.

So the real deadline to leave Bishan for Expo is set by working **backwards**
from the last leg, not by checking "last train from Bishan" in isolation.
That's what `RoutingService` does: find the route, split it into per-line
legs, then walk backwards computing the true latest-departure at each leg.

Also good news: last train times don't vary by weekday/Sat/Sun-PH (only
*first* train does) — confirmed from the live site — so the day-type logic
you were dreading mostly isn't needed for this specific feature.

## Structure

```
backend/LastTrain.Api/     ASP.NET Core 8 minimal API + EF Core (SQLite)
  Models/Models.cs         Station, Line, StationLine, LastTrainService, PublicHoliday
  Data/AppDbContext.cs     EF Core context
  Data/SeedData.cs         Full NSL/EWL/CCL/NEL/DTL/TEL network + real demo data
  Services/RoutingService.cs   Graph search + backward deadline propagation
  Program.cs               /api/stations, /api/plan?from=X&to=Y

scraper/scrape_last_trains.py   Playwright scraper for bulk-seeding real data

frontend/lastrain_app/     Flutter web app (station pickers -> call the API)
```

## Running it

```bash
# Backend
cd backend/LastTrain.Api
dotnet restore
dotnet run
# note the printed URL, e.g. http://localhost:5000 — update apiBaseUrl in
# frontend/lastrain_app/lib/main.dart if it differs

# Frontend
cd frontend/lastrain_app
flutter pub get
flutter run -d chrome
```

Try From: `Bishan`, To: `Expo` — that route has real seeded data end to end.
Any other pair will still return an answer, but legs flagged with an
orange ⚠ icon are using a flat **23:30 placeholder** — real but unverified
network topology, fake timing — so don't rely on those until you've filled
in the real numbers.

## Filling in real data for the rest of the network (~170 stations)

I checked: a plain HTTP request to sgtrains.com gets blocked by Cloudflare
(403), but the data itself isn't hidden behind anything else. Two ways to
get it in:

1. **Playwright scraper** (`scraper/scrape_last_trains.py`) — a real browser
   context generally gets through Cloudflare's challenge where curl/requests
   won't. Run it once, review the JSON, bulk-import. Since these timings
   barely change, this is a "run every few months" task, not a live
   dependency — don't call it from the app itself.
2. **Manual entry** — first/last train timings change rarely (mostly on
   service revisions), so a spreadsheet -> CSV import is a perfectly
   reasonable one-time job too, and safer than trusting a scraper you can't
   easily verify.

Either way: only flip `IsVerified = true` once you've actually checked the
number against the source — that flag is what lets the UI warn you when
it's relying on a guess.

## What's simplified (so you can decide if/when to invest more)

- **Travel time between stops** is a flat per-line estimate (~2-2.3 min),
  not real scheduled running times. Fine for "can I make this connection"
  math; not exact-to-the-minute. If you want more precision, LTA's DataMall
  has real-time arrival data you could layer in later.
- **LRT lines** (Bukit Panjang, Sengkang, Punggol) aren't seeded yet — same
  `AddLine(...)` pattern in `SeedData.cs` extends to them directly.
- **Transfer buffer** is a flat 4 minutes for every interchange — some
  (cross-platform) are faster, some (long underground walks, e.g. City Hall)
  are slower. Worth tuning per-interchange once you're using this for real.
- The routing engine picks the **fastest** route by travel time; it doesn't
  yet consider "which route has the latest last train" as a ranking factor
  — for a night out, you might sometimes prefer a slightly slower route that
  lets you leave 20 minutes later. Easy follow-up: compute a couple of
  candidate paths and show whichever has the latest feasible departure.
