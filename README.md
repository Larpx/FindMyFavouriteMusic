# Find My Favourite Music

A music taste prediction system that analyzes your music library and predicts songs you'll likely enjoy based on your listening preferences.

## Overview

Find My Favourite Music is a .NET-based application that uses acoustic and deep learning features to analyze your music collection and build a personalized taste profile. The system extracts audio features from your liked songs, compares them with other tracks in your library, and predicts which songs match your musical preferences.

系统采用**双特征体系**对音频进行建模：

- **声学特征**：基于 NWaves 提取，输出 52 维向量（MFCC + 频谱质心 + 色度，各取均值与方差）
- **深度特征**：基于 ONNX Runtime + VGGish 模型提取，输出 128 维向量（可选，缺失时优雅降级）

用户画像采用 **Welford 在线增量更新算法**，可在 O(1) 时间复杂度内随新增喜欢歌曲实时更新，无需全量重算。最终预测通过余弦相似度配合**加权评分**（默认 0.4 声学 + 0.6 深度）得出，同时支持仅声学模式与声学+深度双模式。

## Features

- **音频格式支持**：WAV、MP3（跨平台），FLAC、M4A（仅 Windows，依赖 Media Foundation）
- **声学特征提取**：基于 NWaves，输出 52 维向量（MFCC + 频谱质心 + 色度，各取均值+方差）
- **深度特征提取**：基于 ONNX Runtime + VGGish 模型，输出 128 维向量（可选，优雅降级）
- **音乐库管理**：扫描目录、并发处理、喜欢标记、SQLite 持久化
- **用户画像构建**：全量重建 + Welford 增量更新（O(1) 时间复杂度）
- **品味预测**：余弦相似度 + 加权评分，支持仅声学/声学+深度双模式
- **跨平台 UI**：基于 Avalonia 12 的桌面应用，支持拖拽上传
- **配置系统**：appsettings.json（默认）+ usersettings.json（用户运行时）+ 环境变量

## Technology Stack

- **.NET 10**：核心运行时
- **Avalonia UI 12.0.5**：跨平台桌面 UI
- **CommunityToolkit.Mvvm 8.4.1**：MVVM 源生成器
- **NAudio 2.3.0**：音频解码，WAV/MP3/FLAC/M4A
- **NWaves 0.9.6**：声学特征提取，MFCC/色度/频谱质心
- **ONNX Runtime 1.22.0**：深度学习推理，VGGish
- **Microsoft.Data.Sqlite 9.0.5 + Dapper 2.1.66**：本地存储
- **Microsoft.Extensions.Hosting 9.0.5**：依赖注入 + 配置 + 日志

## Project Structure

```
src/
├── FindMyFavouriteMusic.Core/       # 核心算法：音频解码、特征提取、相似度计算、预测引擎
├── FindMyFavouriteMusic.Services/   # 业务服务：音乐库管理、画像构建、预测编排、数据访问
├── FindMyFavouriteMusic.Models/     # 数据模型：实体、DTO、Result 模式、枚举
├── FindMyFavouriteMusic.GUI/        # Avalonia UI：ViewModel、View、转换器、样式
└── FindMyFavouriteMusic.Tests/      # 单元测试：Core 算法测试 + Services 业务测试
```

## Installation

### Prerequisites

- .NET 10 SDK
- （可选）VGGish ONNX 模型文件，用于深度特征提取
- （Windows）FLAC/M4A 解码依赖系统 Media Foundation

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
3. Select a song file to predict, or **drag and drop audio files directly into the prediction area**
4. View the prediction score and detailed breakdown

### Settings

- Adjust acoustic vs. deep feature weights
- Load ONNX model for deep feature extraction
- Rebuild your taste profile

所有设置会持久化到 `usersettings.json`，下次启动时自动恢复。

## Configuration

Configuration is stored in `appsettings.json`:

```json
{
  "FeatureExtraction": {
    "MfccCoefficientCount": 13,
    "MelFilterBankSize": 26,
    "TargetSampleRate": 16000,
    "FrameDurationSeconds": 0.025,
    "HopDurationSeconds": 0.01,
    "EnableNormalization": false
  },
  "OnnxModel": {
    "EnableDeepFeatures": false,
    "VggishModelPath": null
  },
  "Prediction": {
    "AcousticWeight": 0.4,
    "DeepWeight": 0.6,
    "AcousticOnlyWeight": 1.0
  },
  "Database": {
    "ConnectionString": "Data Source=findmyfavouritemusic.db"
  },
  "Scan": {
    "SupportedExtensions": [".wav", ".mp3", ".flac", ".m4a"],
    "MaxConcurrentProcessing": 4
  }
}
```

**配置优先级**（从高到低）：

1. 环境变量（前缀 `FINDMYFAVOURITEMUSIC_`，例如 `FINDMYFAVOURITEMUSIC_Prediction__AcousticWeight`）
2. `usersettings.json`（用户运行时配置，由设置页写入）
3. `appsettings.json`（默认配置）

## Documentation

- [需求与设计文档](docs/需求与设计文档.md)：完整的需求规格和架构设计
- [算法说明](docs/算法说明.md)：音频解码、特征提取、画像构建、相似度计算等核心算法的详细原理
- [使用说明](docs/使用说明.md)：环境搭建、构建运行、功能使用、配置说明、FAQ

## Namespace

项目统一使用命名空间 `Larpx.PersonalTools.FindMyFavouriteMusic.*`，各层对应：

- `Larpx.PersonalTools.FindMyFavouriteMusic.Core`
- `Larpx.PersonalTools.FindMyFavouriteMusic.Services`
- `Larpx.PersonalTools.FindMyFavouriteMusic.Models`
- `Larpx.PersonalTools.FindMyFavouriteMusic.GUI`

## License

See the LICENSE file for details.
