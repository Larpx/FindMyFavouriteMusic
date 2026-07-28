# Find My Favourite Music

A desktop app that learns your taste from songs you mark as liked, then scores how well new tracks match that taste.

[中文](README.md) · [User guide](docs/用户使用说明.md) · [Developer guide](docs/开发说明.md)

---

## What it does

- Scan and browse your local music library
- Build a taste profile from liked songs
- Predict a 0–100 match score for a track (file picker or drag-and-drop)
- Edit tags and cover art in a detail panel and write them back to the file
- Optionally use a deep model for richer features (works without it too)

---

## How it works (briefly)

1. **Scan** a folder; the app analyzes each track and stores results  
2. You **like** songs; the app updates your taste profile  
3. On **Prediction**, pick or drop a file to get a **match score**

See the [user guide](docs/用户使用说明.md) (Chinese) for day-to-day use, and [算法说明](docs/算法说明.md) for algorithms.

---

## Quick start

**Requires**: Windows 10+ (recommended), [.NET 10 SDK](https://dotnet.microsoft.com/download) when running from source

```bash
cd src
dotnet build
dotnet run --project FindMyFavouriteMusic.GUI
```

1. **Library** → scan a folder → like a few songs  
2. **Prediction** → choose or drop a file → read the score  
3. **Settings** → adjust weights or load an optional deep model when needed  

---

## Project layout

| Path | Purpose |
|------|---------|
| `src/` | Solution and source |
| `docs/` | User, developer, and algorithm docs |
| `scripts/` | Benchmarks and helper scripts |
| `LICENSE` | MIT |

For contributing and internals, see [开发说明](docs/开发说明.md) (Chinese).

---

## License

[MIT](LICENSE) © 2026 Larpx
