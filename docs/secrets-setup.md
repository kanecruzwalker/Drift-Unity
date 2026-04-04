# UGS & Secrets Setup — Drift

> This file documents what external configuration is required to run a working build.
> No actual credentials are stored here — this is a setup guide only.
> Treat your Unity Project ID and any API keys as secrets. Never commit them.

---

## Unity Gaming Services (UGS)

Drift uses two UGS services: **Relay** and optionally **Lobby**.

### 1. Create or Use an Existing UGS Project

1. Go to [dashboard.unity3d.com](https://dashboard.unity3d.com)
2. Create a new project or use an existing one
3. Copy your **Project ID** (format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`)

### 2. Link the Project in Unity Editor

1. Open the project in Unity 6 LTS
2. Edit → Project Settings → Services
3. Sign in with your Unity account
4. Link to your UGS project using the Project ID

### 3. Enable Required Services

In the UGS dashboard under your project:
- **Relay** → Enable (free tier is sufficient — supports up to 10 concurrent players)
- **Lobby** → Enable (optional, used for join code UI flow)

### 4. Verify in Editor

Window → Services → verify Relay shows as "Enabled"

---

## Android Signing Keystore

For a release APK build you need a signing keystore. For development and assignment submission a debug keystore is fine (Unity generates one automatically). For a release build:

1. In Unity: Edit → Project Settings → Player → Android → Publishing Settings
2. Create a new keystore — save it **outside** the project folder
3. The `.keystore` and `.jks` file extensions are gitignored — never commit these
4. Store the keystore password somewhere secure (password manager)

---

## What Is NOT in This Repo

| Item | Where It Lives |
|------|---------------|
| UGS Project ID | Unity Editor → Project Settings → Services |
| Unity account credentials | Unity Hub login |
| Android keystore file | Local disk, outside project folder |
| Keystore password | Password manager |

---

## Reproducing a Working Build from Scratch

1. Clone the repo
2. Open in Unity 6 LTS
3. Link your own UGS project (Step 2 above) — you need your own free UGS account
4. Enable Relay in your UGS dashboard
5. For Android: connect device with USB debugging, Build and Run
6. For Unity Remote: install Unity Remote 5 on iPhone, connect via USB, hit Play
