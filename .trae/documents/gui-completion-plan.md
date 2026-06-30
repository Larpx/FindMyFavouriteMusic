# GUI 层完善与项目收尾计划

## 当前状态

- **Core / Services / Tests**: 编译通过，13 个测试全绿
- **GUI 层**: 4 个编译错误阻塞构建
  1. `MusicLibraryViewModel.cs:64` — `result.Value` 可能为 null 传给 `ObservableCollection`
  2. `App.axaml.cs:33` — `DataValidators` 在 Avalonia 12 中不存在
  3. `PredictionViewModel.cs:79` — `result.Value` 可能空引用解引用
  4. `MusicLibraryViewModel.cs:112` — 同 #1
- **缺失功能**: 文件对话框交互、值转换器、样式系统、全局异常处理完善

## 修复步骤

### 步骤 1: 修复 4 个编译错误

**1.1 MusicLibraryViewModel.cs — 两处 nullable 警告**
- 第 64 行: `Songs = new ObservableCollection<SongDto>(result.Value);`
  → 改为 `Songs = new ObservableCollection<SongDto>(result.Value ?? []);`
- 第 112 行: 同上处理

**1.2 App.axaml.cs — DataValidators 不存在**
- Avalonia 12 移除了 `DataValidators` 静态类
- 改用 `BindingPlugins.DataValidators.RemoveAt(0)` 或直接删除此行（CommunityToolkit.Mvvm 不需要移除验证插件）
- 检查 Avalonia 12 的实际 API 确认正确做法

**1.3 PredictionViewModel.cs — 空引用解引用**
- 第 79 行: `var prediction = result.Value;` 后面直接访问属性
- 改为 `var prediction = result.Value!;` 或在 IsSuccess 后 Value 不为 null

### 步骤 2: 实现文件对话框交互

**2.1 MusicLibraryView.axaml.cs — 扫描目录**
- 使用 Avalonia 的 `IStorageProvider` 打开文件夹对话框
- 在 View code-behind 中处理对话框结果，调用 ViewModel 的 `ScanDirectoryAsync(path)`
- 通过 `TopLevel.GetTopLevel(this)?.StorageProvider` 获取

**2.2 PredictionView.axaml.cs — 选择文件**
- 使用 `IStorageProvider` 打开文件对话框（筛选音频格式）
- 选中后设置 ViewModel 的 `SelectedFilePath`

### 步骤 3: 实现值转换器

**3.1 BoolToLikeIconConverter**
- `true` → "♥", `false` → "♡"
- 实现 `IValueConverter`

**3.2 ScoreToColorConverter**
- 0-30 → 红色(#ef4444), 30-70 → 黄色(#f59e0b), 70-100 → 绿色(#10b981)
- 实现 `IValueConverter`

### 步骤 4: 完善 XAML 绑定

**4.1 MusicLibraryView.axaml**
- 喜欢按钮使用 `BoolToLikeIconConverter` 显示图标
- 添加列宽优化

**4.2 SettingsView.axaml**
- `IsModelLoaded` 显示使用 `BoolToLikeIconConverter` 或自定义文本

### 步骤 5: 添加样式系统

- 创建 `Styles/AppStyles.axaml`
- 定义设计令牌（颜色、间距、字体）为 StaticResource
- 在 App.axaml 中引用

### 步骤 6: 完善全局异常处理

- Program.cs 中已有 AppDomain + TaskScheduler 处理
- 添加 Avalonia 的 `WithExceptionHandler` 或自定义错误窗口
- 确保异常不会导致静默崩溃

### 步骤 7: 构建验证与测试

- `dotnet build` 零错误零警告
- `dotnet test` 全部通过
- 启动 GUI 验证 Avalonia 窗口弹出

## 关键文件清单

| 文件 | 操作 |
|------|------|
| `src/FindMyFavouriteMusic.GUI/ViewModels/MusicLibraryViewModel.cs` | 修复 nullable |
| `src/FindMyFavouriteMusic.GUI/ViewModels/PredictionViewModel.cs` | 修复 nullable |
| `src/FindMyFavouriteMusic.GUI/App.axaml.cs` | 修复 DataValidators |
| `src/FindMyFavouriteMusic.GUI/Views/MusicLibraryView.axaml.cs` | 添加文件对话框 |
| `src/FindMyFavouriteMusic.GUI/Views/MusicLibraryView.axaml` | 绑定转换器 |
| `src/FindMyFavouriteMusic.GUI/Views/PredictionView.axaml.cs` | 添加文件对话框 |
| `src/FindMyFavouriteMusic.GUI/Converters/BoolToLikeIconConverter.cs` | 新建 |
| `src/FindMyFavouriteMusic.GUI/Converters/ScoreToColorConverter.cs` | 新建 |
| `src/FindMyFavouriteMusic.GUI/Styles/AppStyles.axaml` | 新建 |
| `src/FindMyFavouriteMusic.GUI/App.axaml` | 引用样式 |
| `src/FindMyFavouriteMusic.GUI/Program.cs` | 完善异常处理 |

## 验证标准

1. `dotnet build` 零错误零警告
2. `dotnet test` 全部通过
3. GUI 启动后窗口正常显示
4. 音乐库页面可点击"扫描目录"打开文件夹对话框
5. 预测页面可点击"浏览"选择文件
6. 喜欢按钮正确显示 ♥/♡ 图标
