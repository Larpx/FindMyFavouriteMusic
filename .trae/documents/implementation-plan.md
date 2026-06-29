# 音乐品味预测系统 - 实施计划

## Context

根据需求与设计文档，从零构建一个基于 .NET 10 + Avalonia 的跨平台桌面应用。系统能扫描本地音乐库、提取声学与深度学习特征、构建用户品味画像、预测对新歌的喜好程度。项目当前无任何代码，仅有 docs 文件夹和 .gitignore。

***

## 项目结构

```
src/
├── FindMyFavouriteMusic.sln
├── Directory.Build.props
├── Directory.Packages.props
├── FindMyFavouriteMusic.Models/         # 数据模型（零依赖）
│   ├── Entities/  (Song, UserProfile)
│   ├── Enums/     (AudioFormat, FeatureType)
│   ├── Dtos/      (SongDto, PredictionResult, ProfileDto)
│   └── Results/   (Result, Result<T>)
├── FindMyFavouriteMusic.Core/          # 核心逻辑
│   ├── Interfaces/ (IAudioDecoder, IAcousticFeatureExtractor, IDeepFeatureExtractor, ISimilarityCalculator, IVectorSerializer, IFeatureAggregator)
│   ├── Audio/      (AudioDecoder, AudioPreprocessor, AudioFormatDetector)
│   ├── Features/   (AcousticFeatureExtractor, DeepFeatureExtractor, FeatureAggregator, FeatureNormalizer)
│   ├── Prediction/ (CosineSimilarityCalculator, PredictionEngine, VectorSerializer)
│   └── Configuration/ (FeatureExtractionOptions, PredictionOptions, OnnxModelOptions)
├── FindMyFavouriteMusic.Services/      # 业务服务
│   ├── Interfaces/ (IMusicLibraryService, IProfileService, IPredictionService, ISongRepository)
│   ├── MusicLibraryService.cs
│   ├── ProfileService.cs
│   ├── PredictionService.cs
│   └── Database/   (DatabaseInitializer, SongRepository, ProfileRepository, DatabaseOptions, ScanOptions)
├── FindMyFavouriteMusic.GUI/           # Avalonia UI
│   ├── App.axaml(.cs)  (DI/配置/日志组装)
│   ├── Program.cs
│   ├── ViewModels/  (MainWindowVM, MusicLibraryVM, PredictionVM, SettingsVM)
│   ├── Views/       (MainWindow, MusicLibraryView, PredictionView, SettingsView)
│   ├── Converters/  (BoolToLikeIconConverter, ScoreToColorConverter)
│   ├── Styles/      (AppStyles.axaml)
│   └── Assets/
└── FindMyFavouriteMusic.Tests/         # 单元测试
    ├── Core/    (AcousticFeature, CosineSimilarity, VectorSerializer, PredictionEngine)
    ├── Services/(MusicLibrary, Profile, Prediction)
    └── Helpers/ (TestDataGenerator)
```

**项目引用关系**: Models ← Core ← Services ← GUI, Tests → 各层

***

## 分阶段实施

### 阶段 1: 项目脚手架与基础设施

**目标**: 解决方案可编译运行，Avalonia 窗口可显示，DI/配置/日志就绪

1. 创建 sln 和 5 个 csproj（net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors）
2. 创建 Directory.Build.props（统一构建属性）和 Directory.Packages.props（统一包版本）
3. 实现 Result 模式（Result.cs, Result<T>.cs）
4. 实现所有 Options 配置类 + appsettings.json
5. Avalonia GUI 项目搭建（App.axaml, MainWindow, Program.cs）
6. App.axaml.cs 中组装 DI 容器（Microsoft.Extensions.Hosting + M.E.DI）
7. 注册日志（ILogger）、配置（IOptions<T>）

**关键 NuGet**: Avalonia 11.3+, Avalonia.Desktop, Avalonia.Themes.Fluent, CommunityToolkit.Mvvm 8.4+, Microsoft.Extensions.Hosting, Microsoft.Data.Sqlite, Dapper

**验收**: `dotnet build` 零错误零警告，Avalonia 窗口弹出，将代码提交并并推送到git上去

### 阶段 2: 数据持久化层

**目标**: SQLite 数据库可创建，CRUD 可执行

1. DatabaseInitializer（IHostedService）启动时建表
2. 数据库 Schema（Songs, UserProfile 两张表）
3. SongRepository -- Dapper CRUD（Insert, GetByFilePath, GetLiked, UpdateLikeStatus, UpdateVectors）
4. ProfileRepository -- 画像读写
5. VectorSerializer -- float\[] ↔ byte\[] 双向转换
6. Song / UserProfile 实体类
7. Repository 单元测试（内存 SQLite）

**验收**: 数据库建表成功，Song CRUD 测试全绿，将代码提交并并推送到git上去

### 阶段 3: 音频解码与预处理

**目标**: 支持 WAV/MP3 解码，输出单声道 16kHz float\[]

1. AudioFormatDetector -- 扩展名+魔数判断格式
2. AudioDecoder（IAudioDecoder）-- WAV/MP3 解码（NAudio.Core）
3. AudioPreprocessor -- 重采样 + 立体声转单声道 + \[-1,1] 归一化
4. 注册 DI
5. 解码器测试

**关键 NuGet**: NAudio.Core 2.2.1

**验收**: 给定 MP3/WAV 文件路径，正确解码为 float\[]，将代码提交并并推送到git上去

### 阶段 4: 声学特征提取

**目标**: NWaves 提取 MFCC + 频谱质心 + 色度特征，聚合为 52 维固定向量

1. AcousticFeatureExtractor -- MFCC(26维) + 频谱质心(2维) + 色度(24维) = 52维
2. FeatureNormalizer -- Z-Score 归一化
3. FeatureAggregator -- 帧级→歌曲级聚合（均值+方差）
4. 注册 DI
5. 特征提取测试

**关键 NuGet**: NWaves 0.9.6

**验收**: 给定音频 float\[]，输出 52 维声学特征向量，将代码提交并并推送到git上去

### 阶段 5: 深度特征提取（ONNX 集成）

**目标**: 集成 ONNX Runtime，支持 VGGish，优雅降级

1. DeepFeatureExtractor -- LoadModel/IsModelLoaded/ExtractAsync
2. VGGish 推理流水线: 分帧→梅尔频谱→构造张量→推理→时间平均
3. 降级策略: EnableDeepFeatures=false 或无模型文件时返回 Failure
4. PredictionEngine 降级: 仅声学时 score = sim\_acoustic
5. 注册 DI（配置驱动：有模型注册真实实现，无模型注册 NullObject）
6. 降级行为测试

**关键 NuGet**: Microsoft.ML.OnnxRuntime 1.24+

**验收**: 无模型时系统正常运行，有模型时提取 128 维向量，将代码提交并并推送到git上去

### 阶段 6: 画像构建与相似度计算

**目标**: 构建用户画像，实现余弦相似度预测

1. CosineSimilarityCalculator -- MathNet.Numerics（零向量/维度不匹配处理）
2. PredictionEngine -- 加权评分 + 降级模式 + \[-1,1]→\[0,100] 映射
3. ProfileService -- RebuildProfile（全量）+ UpdateProfileIncremental（Welford 在线算法）
4. PredictionService -- 端到端编排（解码→提取→相似度→分数）
5. 注册 DI
6. 相似度/画像测试

**关键 NuGet**: MathNet.Numerics 6.0+

**验收**: 预测分数在合理范围，画像增量更新与全量重建等价，将代码提交并并推送到git上去

### 阶段 7: MusicLibraryService 业务服务

**目标**: 音乐库扫描、标记、处理编排

1. ScanDirectoryAsync -- 遍历目录，扩展名筛选，IProgress 进度报告
2. ProcessSongAsync -- 解码→提取→存储
3. ToggleLikeAsync -- 更新状态 + 触发画像增量更新
4. 并发控制（SemaphoreSlim + MaxConcurrentProcessing 配置）
5. 已存在文件去重

**验收**: 扫描指定目录，处理所有音乐文件，进度回调正常，将代码提交并并推送到git上去

### 阶段 8: Avalonia UI - 主界面与音乐库

**目标**: 可交互的音乐库列表界面

1. MainWindowViewModel -- 导航管理（ContentControl 切换子视图）
2. MusicLibraryViewModel -- Songs 集合 + ScanCommand + ToggleLikeCommand + 进度
3. MusicLibraryView\.axaml -- DataGrid + 喜欢按钮 + 扫描按钮 + 进度条
4. ViewModelBase -- 继承 ObservableObject
5. 文件对话框（IStorageProvider）
6. 样式系统（StaticResource 定义设计令牌）

**验收**: UI 中可选择目录、扫描、查看列表、标记喜欢，将代码提交并并推送到git上去

### 阶段 9: Avalonia UI - 预测与设置

**目标**: 完整的预测界面和设置界面

1. PredictionViewModel -- PredictCommand + 分数显示 + 分项得分 + 当前模式
2. PredictionView\.axaml -- 文件选择 + 预测按钮 + 分数进度条 + 分项面板
3. SettingsViewModel -- 目录配置 + ONNX 路径 + 权重滑块 + 深度特征开关
4. SettingsView\.axaml
5. 值转换器（BoolToLikeIconConverter, ScoreToColorConverter）

**验收**: 可选择新歌获取预测分数，可配置模型路径切换模式，将代码提交并并推送到git上去

### 阶段 10: 全局异常处理、集成测试与优化

1. 全局异常处理（AppDomain, TaskScheduler, Avalonia WithExceptionHandler）
2. 后台任务优化（Task.Run + IProgress + Channel 流式处理）
3. 集成测试（端到端 + 降级场景）
4. 性能优化（跳过重复提取、数据库连接池）
5. 各项目 README 文档

**验收**: 0 错误 0 警告，所有测试通过，全局异常不崩溃，将代码提交并并推送到git上去

<br />

***

## ONNX 优雅降级策略

1. `OnnxModelOptions.EnableDeepFeatures` 默认 false，`VggishModelPath` 默认 null
2. DeepFeatureExtractor: `IsModelLoaded` 反映真实状态，未加载时 ExtractAsync 返回 Failure
3. PredictionEngine: 模型可用时加权评分，不可用时仅声学评分
4. UI 显示当前模式（"声学模式" / "深度增强模式"）

## 测试策略

- 框架: xUnit + Moq + FluentAssertions
- 命名: Method\_Scenario\_ExpectedResult
- 关键场景: 向量序列化往返、余弦相似度边界、画像增量/全量等价、模型缺失降级、空目录、损坏文件

## 验证方式

1. `dotnet build` 零错误零警告
2. `dotnet test` 所有测试通过
3. 运行 GUI 应用，执行端到端流程：扫描目录→标记喜欢→构建画像→预测新歌
4. 将代码提交并并推送到git上去

