# Find My Favourite Music

A music taste prediction system that analyzes your music library and predicts songs you'll likely enjoy based on your listening preferences.

## Overview

Find My Favourite Music is a .NET-based application that uses acoustic and deep learning features to analyze your music collection and build a personalized taste profile. The system extracts audio features from your liked songs, compares them with other tracks in your library, and predicts which songs match your musical preferences.

## Features

- **Audio Format Support**: Decode and process WAV and MP3 files
- **Acoustic Feature Extraction**: Extract MFCC and other acoustic features from audio
- **Deep Feature Extraction**: ONNX-based deep learning feature extraction (VGGish model)
- **Music Library Management**: Scan directories, manage songs, and track favorites
- **User Profile Building**: Build a personalized taste profile from liked songs
- **Taste Prediction**: Predict which songs you'll likely enjoy based on your profile
- **Cross-Platform UI**: Avalonia-based cross-platform desktop application

## Technology Stack

- **.NET 8**: Core runtime
- **Avalonia UI**: Cross-platform desktop UI
- **NAudio**: Audio decoding and processing
- **ONNX Runtime**: Deep learning inference
- **SQLite**: Local data storage

## Project Structure

```
src/
├── FindMyFavouriteMusic.Core/       # Core audio processing and feature extraction
├── FindMyFavouriteMusic.Services/ # Business services
├── FindMyFavouriteMusic.Models/   # DTOs, entities, and models
├── FindMyFavouriteMusic.GUI/     # Avalonia UI application
└── FindMyFavouriteMusic.Tests/   # Unit tests
```

## Installation

### Prerequisites

- .NET 8 SDK
- For deep features: VGGish ONNX model (optional)

### Build

```bash
cd src
dotnet build
```

### Run

```bash
cd src/FindMyFavouriteMusic.GUI
dotnet run
```

## Usage

### Music Library

1. Click "Scan Directory" to select a folder containing your music files
2. The system will scan and extract features from all supported audio files
3. Browse your library and click the heart icon to like songs

### Prediction

1. Ensure you have liked some songs to build your profile
2. Go to the Prediction page
3. Select a song file to predict
4. View the prediction score and detailed breakdown

### Settings

- Adjust acoustic vs. deep feature weights
- Load ONNX model for deep feature extraction
- Rebuild your taste profile

## Configuration

Configuration is stored in `appsettings.json`:

```json
{
  "FeatureExtraction": {
    "MfccCoefficientCount": 13,
    "MelFilterBankSize": 26,
    "TargetSampleRate": 16000
  },
  "OnnxModel": {
    "EnableDeepFeatures": false
  },
  "Prediction": {
    "AcousticWeight": 0.4,
    "DeepWeight": 0.6
  }
}
```

## License

See the LICENSE file for details.