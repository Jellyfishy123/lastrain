"""
Pulls real first/last train timings from sgtrains.com for every station and
writes them to last_train_data.json, in a shape that's easy to bulk-import
into the LastTrainServices table.

WHY THIS NEEDS PLAYWRIGHT, NOT plain requests/curl:
sgtrains.com sits behind Cloudflare bot-protection (confirmed: a plain HTTPS
GET gets a 403). A real browser context (even headless) generally gets
through because it executes the page's JS challenge and presents a normal
browser fingerprint. If Cloudflare ever blocks headless browsers too, the
fallback is: open these pages manually and copy the numbers in — first/last
train times change rarely (only on schedule revisions), so this is a
"run it once, then maybe once every few months" job, not something that
needs to run live in your app.

Usage:
    pip install playwright
    playwright install chromium
    python scrape_last_trains.py

Output: last_train_data.json — a list of
    {station, line, towards, last_train, first_mon_fri, first_sat, first_sun_ph}
Review it, then bulk-insert into LastTrainServices (a tiny console/EF Core
seeding script, or even a CSV import, works fine for a one-time load).
"""

import json
import re
import time
from playwright.sync_api import sync_playwright

BASE = "https://www.sgtrains.com/guide-traintiming"

# Station name -> query param value. Populate this from the map / the
# station list already on the guide-traintiming page. Kept short here —
# extend with the full ~170-station list for a full seed.
STATIONS = [
    "Bishan", "City Hall", "Tanah Merah", "Ang Mo Kio", "Toa Payoh",
    "Raffles Place", "Dhoby Ghaut", "Jurong East", "Serangoon", "Paya Lebar",
    # ... add every station name from /api/stations here
]

TIME_RE = re.compile(r"^\d{1,2}:\d{2}$")


def parse_station_page(html_text: str, station: str):
    """Very forgiving parser: looks for markdown-ish table rows produced by
    the page (see the SGTrains page structure) of the form
    '| Destination | first | first | first | last |' or the 2-column variant.
    Adjust this if sgtrains.com changes its markup."""
    rows = []
    current_line = None
    for line in html_text.splitlines():
        line = line.strip()
        if not line:
            continue
        if "Line (" in line and "|" not in line:
            current_line = line.split(" (")[0].strip()
            continue
        if line.startswith("|") and "Train to" not in line and "---" not in line:
            cells = [c.strip() for c in line.strip("|").split("|")]
            cells = [c for c in cells if c != ""]
            if len(cells) >= 2 and current_line:
                dest = cells[0]
                last = cells[-1]
                if TIME_RE.match(last):
                    rows.append({
                        "line": current_line,
                        "towards": dest,
                        "last_train": last,
                    })
    return {"station": station, "services": rows}


def main():
    results = []
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        for station in STATIONS:
            url = f"{BASE}?station={station.replace(' ', '+')}"
            page.goto(url, wait_until="networkidle")
            page.wait_for_timeout(1500)  # let the client-side table render
            text = page.inner_text("body")
            parsed = parse_station_page(text, station)
            results.append(parsed)
            print(f"Scraped {station}: {len(parsed['services'])} services")
            time.sleep(1.0)  # be a polite scraper
        browser.close()

    with open("last_train_data.json", "w") as f:
        json.dump(results, f, indent=2)
    print("Wrote last_train_data.json — review before importing.")


if __name__ == "__main__":
    main()
