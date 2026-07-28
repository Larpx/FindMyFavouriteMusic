# Find My Favourite Music

A music taste prediction system that analyzes your music library and predicts songs you'll likely enjoy based on your listening preferences.

## Overview

Find My Favourite Music is a .NET-based application that uses acoustic and deep learning features to analyze your music collection and build a personalized taste profile. The system extracts audio features from your liked songs, compares them with other tracks in your library, and predicts which songs match your musical preferences.

系统采用**双特征体系**对音频进行建模：

- **声学特征**：基于 NWaves 提取，输出 52 维向量（MFCC + 频谱质心 + 色度，各取均值与方差）
- **深度特征**：基于 ONNX Runtime + VGGish（128 维）或 MERT（768 维），可选；缺失时优雅降级为仅声学

用户画像采用 **Welford 在线增量更新算法**，可在 O(1) 时间复杂度内随新增喜欢歌曲实时更新，无需全量重算。最终预测通过余弦相似度配合**加权评分**（默认 0.4 声学 + 0.6 深度）得出，同时支持仅声学模式与声学+深度双模式。

## Features

- **音频格式支持**：WAV、MP3（跨平台），FLAC、M4A（仅 Windows，依赖 Media Foundation）
- **声学特征提取**：基于 NWaves，输出 52 维向量（MFCC + 频谱质心 + 色度，各取均值+方差）
- **深度特征提取**：ONNX Runtime + VGGish / MERT（可选，优雅降级）
- **音乐库管理**：扫描目录、MD5 特征缓存、喜欢标记、右侧详情编辑标签/封面（直接写回原文件）
- **用户画像构建**：全量重建 + Welford 增量更新（O(1) 时间复杂度）；空喜欢自动清空画像
- **品味预测**：余弦相似度 + 加权评分，支持仅声学/声学+深度双模式
- **跨平台 UI**：基于 Avalonia 12 的桌面应用，支持拖拽上传
- **配置系统**：`appsettings.json`（默认）+ `%AppData%/FindMyFavouriteMusic/usersettings.json`（用户运行时）+ 环境变量
- **资源护栏**：单文件 ≤ 200MB；模型热切换 Dispose 旧 Session；加载与扫描/预测互斥

## Technology Stack

- **.NET 10**：核心运行时
- **Avalonia UI 12.0.5**：跨平台桌面 UI
- **CommunityToolkit.Mvvm 8.4.1**：MVVM 源生成器
- **NAudio 2.3.0**：音频解码，WAV/MP3/FLAC/M4A
- **TagLibSharp 2.3.0**：音频标签与内嵌封面读写
- **NWaves 0.9.6**：声学特征提取，MFCC/色度/频谱质心
- **ONNX Runtime 1.22.0**：深度学习推理，VGGish / MERT
- **Microsoft.Data.Sqlite 9.0.5 + Dapper 2.1.66**：本地存储（`PRAGMA user_version` 迁移）
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
2. The system will scan and extract features（二次扫描若 MD5 未变则跳过重算）
3. Browse your library；单击歌曲打开右侧详情，可编辑标题/艺术家/专辑/封面等并**直接写回原文件**
4. Click the heart icon to like songs（重复喜欢为 no-op；取消全部喜欢会清空画像）

### Prediction

1. Ensure you have liked some songs to build your profile
2. Go to the Prediction page
3. Select a song file to predict, or **drag and drop audio files directly into the prediction area**
4. View the prediction score and detailed breakdown

### Settings

- Adjust acoustic / deep / acoustic-only weights
- Configure ONNX model path、EP、OpenVINO 设备与缓存目录
- Configure scan concurrency
- Load ONNX model / rebuild taste profile

所有用户设置持久化到 `%AppData%/FindMyFavouriteMusic/usersettings.json`（原子写入），下次启动自动恢复；首次会尝试从应用目录旧版 `usersettings.json` 迁移。

## Configuration

Configuration is stored in `appsettings.json`（默认）与用户配置：

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
    "EnableDeepFeatures": true,
    "ModelType": "MERT",
    "VggishModelPath": null,
    "MertModelPath": "Models/MERT-v1-95M.onnx",
    "ExecutionProvider": "OpenVINO",
    "OpenVinoDevice": "GPU",
    "OpenVinoCacheDir": "./openvino-cache"
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
    "SupportedExtensions": [".wav", ".mp3", ".flac", ".ogg", ".m4a"],
    "MaxConcurrentProcessing": 2,
    "LastScanDirectory": null
  }
}
```

**配置优先级**（从高到低）：

1. 环境变量（前缀 `FINDMYFAVOURITEMUSIC_`）
2. `%AppData%/FindMyFavouriteMusic/usersettings.json`
3. 应用目录旧版 `usersettings.json`（兼容）
4. `appsettings.json`

单文件硬限制 **200MB**（解码前拒绝）。

## Wiki

项目提供完整的 Wiki 文档，位于仓库 [`wiki/`](wiki) 目录，包含以下章节：

| 章节 | 内容 |
|------|------|
| 📖 [01-项目介绍](wiki/01-项目介绍) | 项目背景、核心能力、技术选型 |
| 🚀 [02-快速开始](wiki/02-快速开始) | 环境准备、构建、运行、首次使用 |
| 🏗️ [03-架构设计](wiki/03-架构设计) | 分层架构、MVVM、依赖注入、数据流 |
| 🔬 [04-算法原理](wiki/04-算法原理) | 解码、特征提取、画像、相似度的数学原理 |
| 💡 [05-功能使用](wiki/05-功能使用) | 音乐库、预测、设置三大功能详解 |
| ⚙️ [06-配置说明](wiki/06-配置说明) | appsettings.json 全字段说明与优先级 |
| 🧩 [07-扩展开发](wiki/07-扩展开发) | 新增特征、格式、相似度算法的扩展指南 |
| ❓ [08-常见问题](wiki/08-常见问题) | FAQ 与故障排查 |

从 [Wiki 首页](wiki/Home) 开始浏览。

## Documentation

- [整改计划](docs/整改计划.md)：A–D 阶段目标、验收与提交记录
- [测试与覆盖说明](docs/测试与覆盖说明.md)：测试命令与覆盖范围约定
- [需求与设计文档](docs/需求与设计文档.md)：完整的需求规格和架构设计
- [算法说明](docs/算法说明.md)：音频解码、特征提取、画像构建、相似度计算等核心算法的详细原理
- [使用说明](docs/使用说明.md)：环境搭建、构建运行、功能使用、配置说明、FAQ

## Namespace

项目统一使用命名空间 `Larpx.PersonalTools.FindMyFavouriteMusic.*`，各层对应：

- `Larpx.PersonalTools.FindMyFavouriteMusic.Core`
- `Larpx.PersonalTools.FindMyFavouriteMusic.Services`
- `Larpx.PersonalTools.FindMyFavouriteMusic.Models`
- `Larpx.PersonalTools.FindMyFavouriteMusic.GUI`

## 推理加速（Execution Provider）

应用启动时会自动检测当前设备是否存在 NPU（通过 WMI 查询 `Intel(R) AI Boost` / `NPU` / `Neural Processing` 等设备名），用于在设置页提示用户 NPU 是否可用。

v2.0 起仅保留 **OpenVINO + CPU** 双 EP 架构（DirectML 已移除，性能对比与选型结论详见 `docs/算法说明.md` 第 10 章）：

- **OpenVINO EP**：Intel 官方为 Core Ultra NPU/GPU 提供的最优 EP，算子覆盖率与性能均优于 DirectML。支持三种目标设备：
  - `GPU`（默认）：实测对 MERT 加速 2.24x（16.3s vs CPU 36.6s）；VGGish 因模型小 CPU 已最快，GPU 反而慢 2.13 倍
  - `NPU`：Intel AI Boost NPU 专用
  - `AUTO`：OpenVINO 运行时自动选择最佳设备
- **CPU EP**：纯 CPU 推理，兼容性最佳，作为 OpenVINO 不可用或推理失败时的回退。

### 用户可配置

设置页"推理设备"卡片提供两项选择：

- **EP 模式**：CPU 或 OpenVINO（切换后需重启应用生效，native 库在启动时加载，无法运行时切换）
- **OpenVINO 目标设备**：GPU / NPU / AUTO（仅 OpenVINO 模式下可选）

选择会通过 `IUserSettingsService.SaveOnnxModelSettingsAsync` 持久化到 `usersettings.json` 的 `OnnxModel.ExecutionProvider` 与 `OnnxModel.OpenVinoDevice` 字段。

### 配置文件示例

```json
"OnnxModel": {
  "ModelType": "MERT",
  "VggishModelPath": null,
  "MertModelPath": "Models/MERT-v1-95M.onnx",
  "EnableDeepFeatures": true,
  "ExecutionProvider": "OpenVINO",
  "OpenVinoDevice": "GPU",
  "OpenVinoCacheDir": "./openvino-cache"
}
```

### 优雅降级

- OpenVINO EP 注册失败（如 native 库缺失、设备不可用）时，`HardwareAccelerator.ConfigureSessionOptions` 返回 `Result.Failure`，提取器自动回退到 CPU EP 创建会话。
- 推理过程中 OpenVINO 算子不兼容抛出异常时，提取器重建 CPU EP 会话并重试一次（通过 `_hasAttemptedCpuFallback` 标志避免循环）。

### OpenVINO 编译缓存

设置 `OpenVinoCacheDir`（如 `./openvino-cache`）后，`HardwareAccelerator` 通过 `SessionOptions.AddSessionConfigEntry` 设置 `session.openvino.cache_dir`，将首次编译结果缓存到磁盘，显著加速二次启动后的会话创建。留空则不启用缓存。

### 参考链接

- [OpenVINO Execution Provider](https://onnxruntime.ai/docs/execution-providers/OpenVINO-ExecutionProvider.html)
- [Windows ML Execution Providers](https://learn.microsoft.com/windows/ai/new-windows-ml/supported-execution-providers)

## License

See the LICENSE file for details.
