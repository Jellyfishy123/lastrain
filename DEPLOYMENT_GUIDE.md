# Deploying "Last Train Home" — Step-by-Step Guide

This app has **two parts** that both need to be deployed:

1. **Backend API** (`backend/LastTrain.Api`, .NET 8 + SQLite) → runs on **Railway**, gives you a public `https://...` URL.
2. **Flutter app** (`lib/main.dart`) → built into **Android** and **iOS** apps and published to the **Play Store** and **App Store**.

The phone apps talk to the backend over the internet, so **the backend must go live first**, then you point the apps at its URL, then you build and publish.

---

## What I already fixed for you

Before this could deploy, a few things were broken or missing. These are done:

- **Backend wouldn't compile** — `Program.cs` had a duplicated/broken database line. Fixed; it now builds with 0 errors.
- **Wouldn't run on Railway** — the app now reads Railway's `PORT` variable, and `DockerFile` was renamed to `Dockerfile` so Railway detects it.
- **Store-blocking app ID** — the app used `com.example.hello`, which Google Play and Apple both reject. Changed to `com.lasttrainhome.app` (Android + iOS). Change it to your own domain later if you own one.
- **App name** — display name is now "Last Train Home" instead of "hello".
- **API URL was hardcoded to `localhost`** — a phone can't reach your laptop. You now pass the real URL at build time with `--dart-define=API_BASE_URL=...` (shown below). It still defaults to localhost for local testing.
- **Added `appsettings.json`** so the first boot on Railway is fast and quiet instead of dumping thousands of SQL log lines.

---

## Prerequisites (install once)

- **Flutter SDK** — https://docs.flutter.dev/get-started/install. After installing, run `flutter doctor` and fix anything it flags.
- **A phone or emulator** to test on.
- **Accounts / costs:**
  - **Railway** — free trial credit; a hobby plan is a few dollars/month. Sign up with GitHub.
  - **Google Play Console** — **US$25, one-time** (never renews).
  - **Apple Developer Program** — **US$99/year**.
  - **A Mac** — required to build and submit the iOS app (Apple only allows iOS builds on macOS). Android can be built on any OS.

---

## Part 1 — Deploy the backend to Railway

**Easiest path is to deploy from GitHub.**

1. **Put the code on GitHub** (if it isn't already). From the project root:
   ```bash
   git add .
   git commit -m "Prep for deployment"
   git push
   ```
   If you don't have a GitHub repo yet, create an empty one at github.com, then follow its "push an existing repository" instructions.

2. **Create the Railway project.** Go to https://railway.app → sign in with GitHub → **New Project** → **Deploy from GitHub repo** → pick this repo.

3. **Point Railway at the backend folder.** The backend isn't at the repo root, so tell Railway where it is:
   - Open the service → **Settings** → **Root Directory** → set it to:
     ```
     lastrain/backend/LastTrain.Api
     ```
   - Railway will find the `Dockerfile` there and build it automatically.

4. **Add environment variables** (service → **Variables**):
   - `DB_PATH` = `/data/lastrain.db`  *(so the database lives on a persistent disk — see next step)*
   - You do **not** need to set `PORT`; Railway sets it automatically and the app now reads it.

5. **Add a volume so data survives restarts** (optional but recommended). Service → **Volumes** → New Volume, mount path `/data`. Without this, the SQLite database resets on every redeploy (it re-seeds itself, so the app still works — you just lose any runtime changes).

6. **Generate the public URL.** Service → **Settings** → **Networking** → **Generate Domain**. You'll get something like:
   ```
   https://lasttrain-production-xxxx.up.railway.app
   ```

7. **Test it.** Open these in a browser — you should get data, not an error:
   - `https://YOUR-URL/api/daycontext`
   - `https://YOUR-URL/api/stations`
   - `https://YOUR-URL/swagger` (interactive API explorer)

   **Copy your Railway URL** — you'll paste it into every app build below.

---

## Part 2 — Point the app at your backend

You don't edit any file for this — you pass the URL when building. Everywhere below you'll see:

```
--dart-define=API_BASE_URL=https://YOUR-URL
```

Replace `https://YOUR-URL` with your actual Railway domain (no trailing slash).

**Quick check before building for stores** — plug in a phone (or start an emulator) and run:

```bash
cd lastrain
flutter pub get
flutter run --dart-define=API_BASE_URL=https://YOUR-URL
```

If the station list loads and journeys plan correctly, your app + backend are talking. Now you can package for the stores.

---

## Part 3 — Android (Google Play)

### 3a. Create a signing key (once)

Google requires release apps to be signed with your own key.

```bash
keytool -genkey -v -keystore ~/lasttrain-upload.jks -keyalg RSA -keysize 2048 -validity 10000 -alias upload
```

Keep this file **safe and backed up** — if you lose it you can't update your app later. Then create `lastrain/android/key.properties` (do **not** commit it to git):

```
storePassword=THE_PASSWORD_YOU_CHOSE
keyPassword=THE_PASSWORD_YOU_CHOSE
keyAlias=upload
storeFile=/absolute/path/to/lasttrain-upload.jks
```

Then wire it into `lastrain/android/app/build.gradle.kts` (replace the `signingConfig = signingConfigs.getByName("debug")` line under `buildTypes { release { ... } }` with your release signing config). Flutter's official guide has the exact snippet: https://docs.flutter.dev/deployment/android#signing-the-app

### 3b. Build the release bundle

```bash
cd lastrain
flutter build appbundle --release --dart-define=API_BASE_URL=https://YOUR-URL
```

Output: `build/app/outputs/bundle/release/app-release.aab`

### 3c. Publish

1. Register at https://play.google.com/console (**US$25 one-time**).
2. **Create app** → fill in name ("Last Train Home"), language, free/paid, and the declarations.
3. Upload the `.aab` under a release.
4. Complete the required sections: privacy policy URL, data safety form, content rating, screenshots, and store listing.
5. **Heads-up for new personal accounts:** if your Play account is a *personal* account created after 13 Nov 2023, Google requires a **closed test with at least 12 testers opted in for 14 continuous days** before you can go to production. Plan for this ~2-week window. (Organization accounts are exempt.)
6. Submit for review.

---

## Part 4 — iOS (App Store) — requires a Mac

### 4a. Open the project in Xcode

```bash
cd lastrain
open ios/Runner.xcworkspace
```

In Xcode: select the **Runner** target → **Signing & Capabilities** → check **Automatically manage signing** → pick your **Team** (your Apple Developer account). Confirm the Bundle Identifier reads `com.lasttrainhome.app`.

### 4b. Build the app

```bash
flutter build ipa --release --dart-define=API_BASE_URL=https://YOUR-URL
```

Output: `build/ios/ipa/*.ipa`

### 4c. Publish

1. Join the **Apple Developer Program** at https://developer.apple.com/programs (**US$99/year**).
2. In https://appstoreconnect.apple.com → **My Apps** → **+** → **New App**. Set the bundle ID to `com.lasttrainhome.app`.
3. Upload the build — either open the `.ipa`/archive in Xcode's **Organizer** and click **Distribute App**, or use the **Transporter** app from the Mac App Store.
4. Fill in the listing: screenshots, description, privacy details, and the App Privacy questionnaire.
5. Optionally test via **TestFlight** first, then **Submit for Review**.

---

## Troubleshooting

- **App shows no stations / network error** → the `API_BASE_URL` was wrong or missing at build time, or the Railway backend is down. Re-open `https://YOUR-URL/api/stations` in a browser to confirm the backend responds.
- **Android build fails on signing** → double-check the `storeFile` path in `key.properties` is absolute and the passwords match the keystore.
- **iOS "no signing certificate"** → your Apple Developer membership must be active and the Team selected in Xcode.
- **Railway build fails** → confirm the Root Directory is exactly `lastrain/backend/LastTrain.Api` and that `Dockerfile` (capital D, lowercase rest) is present there.

---

## Recap

1. Push to GitHub → deploy backend on Railway → get the `https` URL.
2. Test the app locally against that URL with `flutter run --dart-define=API_BASE_URL=...`.
3. Build Android (`flutter build appbundle`) and iOS (`flutter build ipa`), each with the same `--dart-define`.
4. Publish to Play Console ($25 once) and App Store ($99/yr). Budget ~2 weeks for Google's tester requirement if you're on a new personal account.
