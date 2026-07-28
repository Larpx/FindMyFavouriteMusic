# 4 种 EP 配置（CPU / OpenVINO AUTO/NPU/GPU）性能对比脚本
# 分别测试 VGGish 与 MERT 处理 Models/ナナツカゼ,PIKASONIC,なこたんまる - 再生.flac 的耗时
# 使用 trx 日志（UTF-8 XML）解析耗时，避免中文控制台编码问题
#
# v2.0 起移除 DirectML EP，仅对比 CPU 与 OpenVINO 三种设备（AUTO/NPU/GPU）。
#
# 用法：
#   .\Run-Ep-Benchmark.ps1
#
# 输出：
#   - 控制台汇总表格
#   - TestResults/ep-benchmark-summary.csv（UTF-8 BOM，Excel 友好）
#   - src/FindMyFavouriteMusic.Tests/TestResults/<配置名>.trx（详细测试日志）

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$testProject = Join-Path $repoRoot 'src\FindMyFavouriteMusic.Tests\FindMyFavouriteMusic.Tests.csproj'
# trx 文件由 dotnet test 保存在 <测试项目目录>\TestResults\ 下
$trxDir = Join-Path (Split-Path -Parent $testProject) 'TestResults'
$resultDir = Join-Path $repoRoot 'TestResults'

New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
New-Item -ItemType Directory -Force -Path $trxDir | Out-Null

# 4 种 EP 测试配置（v2.0 起移除 DirectML）
$configs = @(
    @{ Name = 'CPU';            Ep = 'CPU';      Device = '' }
    @{ Name = 'OpenVINO-AUTO';  Ep = 'OpenVINO'; Device = 'AUTO' }
    @{ Name = 'OpenVINO-NPU';   Ep = 'OpenVINO'; Device = 'NPU' }
    @{ Name = 'OpenVINO-GPU';   Ep = 'OpenVINO'; Device = 'GPU' }
)

# 1. 构建项目（一次构建，多次测试复用）
Write-Host '===== 构建项目（Release） =====' -ForegroundColor Cyan
& dotnet build $testProject -c Release --nologo 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "构建失败，退出码: $LASTEXITCODE"
}

# 解析函数：从 trx 文件内容提取 VGGish/MERT 耗时
# trx 文件中换行是 &#xD; 实体，正则需要匹配该实体或行尾
function Parse-TrxResult {
    param([string]$TrxPath)

    $result = @{
        VggishLoad = $null; VggishInfer = $null; VggishEp = 'N/A'
        MertLoad = $null; MertInfer = $null; MertEp = 'N/A'
        Error = $null
    }

    if (-not (Test-Path $TrxPath)) {
        $result.Error = "trx 文件不存在: $TrxPath"
        return $result
    }

    try {
        $content = Get-Content $TrxPath -Encoding UTF8 -Raw
        # 正则：VGGish: 加载=584ms, 推理=2560ms, 实际EP=CPU
        # trx 中行尾是 &#xD; 实体，匹配到该实体或 <（标签结尾）停止
        $vggishMatch = [regex]::Match($content, 'VGGish:\s*加载=(\d+)ms,\s*推理=(\d+)ms,\s*实际EP=([^&<\r\n]+)')
        if ($vggishMatch.Success) {
            $result.VggishLoad = [int]$vggishMatch.Groups[1].Value
            $result.VggishInfer = [int]$vggishMatch.Groups[2].Value
            $result.VggishEp = $vggishMatch.Groups[3].Value.Trim()
        }
        $mertMatch = [regex]::Match($content, 'MERT:\s*加载=(\d+)ms,\s*推理=(\d+)ms,\s*实际EP=([^&<\r\n]+)')
        if ($mertMatch.Success) {
            $result.MertLoad = [int]$mertMatch.Groups[1].Value
            $result.MertInfer = [int]$mertMatch.Groups[2].Value
            $result.MertEp = $mertMatch.Groups[3].Value.Trim()
        }
        if ($null -eq $result.VggishInfer -and $null -eq $result.MertInfer) {
            $result.Error = '未匹配到 VGGish/MERT 耗时数据'
        }
    } catch {
        $result.Error = "解析异常: $_"
    }

    return $result
}

$results = @()

# 2. 依次运行 5 种 EP 配置
for ($i = 0; $i -lt $configs.Count; $i++) {
    $cfg = $configs[$i]
    $cfgName = $cfg.Name
    Write-Host "`n===== [$($i + 1)/$($configs.Count)] 运行测试: $cfgName =====" -ForegroundColor Cyan

    # 清理上一次的环境变量
    Remove-Item Env:FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider -ErrorAction SilentlyContinue
    Remove-Item Env:FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice -ErrorAction SilentlyContinue

    # 设置本次环境变量
    $env:FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider = $cfg.Ep
    if ($cfg.Device -ne '') {
        $env:FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice = $cfg.Device
    }

    # trx 文件路径（dotnet test 会保存到 <测试项目目录>\TestResults\<LogFileName>）
    $trxFileName = "$cfgName.trx"
    $trxFile = Join-Path $trxDir $trxFileName
    if (Test-Path $trxFile) { Remove-Item $trxFile -Force }

    # 运行测试（不重定向到文件，避免中文编码问题；用 trx 保存输出）
    $testFilter = 'FullyQualifiedName~MusicInferenceBenchmarkTests.Benchmark_MusicInference_CurrentEp_Only'
    & dotnet test $testProject -c Release --no-build --nologo `
        --filter $testFilter `
        --logger "trx;LogFileName=$trxFileName" `
        --logger "console;verbosity=minimal" `
        2>&1 | Out-Host

    $exitCode = $LASTEXITCODE
    Write-Host "  测试退出码: $exitCode"

    # 3. 解析 trx 文件提取耗时
    $parsed = Parse-TrxResult -TrxPath $trxFile

    $expectedEp = if ($cfg.Device -ne '') { "$($cfg.Ep)($($cfg.Device))" } else { $cfg.Ep }
    $result = [PSCustomObject]@{
        Configuration  = $cfgName
        ExpectedEP     = $expectedEp
        VGGish_LoadMs  = $parsed.VggishLoad
        VGGish_InferMs = $parsed.VggishInfer
        VGGish_ActualEP = $parsed.VggishEp
        MERT_LoadMs    = $parsed.MertLoad
        MERT_InferMs   = $parsed.MertInfer
        MERT_ActualEP  = $parsed.MertEp
        ExitCode       = $exitCode
        ParseError     = $parsed.Error
    }
    $results += $result

    Write-Host ("  VGGish: 加载={0} ms, 推理={1} ms, EP={2}" -f $parsed.VggishLoad, $parsed.VggishInfer, $parsed.VggishEp)
    Write-Host ("  MERT:   加载={0} ms, 推理={1} ms, EP={2}" -f $parsed.MertLoad, $parsed.MertInfer, $parsed.MertEp)
    if ($parsed.Error) {
        Write-Host "  解析告警: $($parsed.Error)" -ForegroundColor Yellow
    }

    # 清理本次环境变量
    Remove-Item Env:FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider -ErrorAction SilentlyContinue
    Remove-Item Env:FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice -ErrorAction SilentlyContinue
}

# 4. 汇总输出
Write-Host "`n`n========== EP 性能对比汇总 ==========" -ForegroundColor Green
Write-Host "音频文件: Models\ナナツカゼ,PIKASONIC,なこたんまる - 再生.flac`n"

$results | Format-Table -AutoSize

# 5. 保存 CSV（UTF-8 BOM，Excel 友好）
$csvFile = Join-Path $resultDir 'ep-benchmark-summary.csv'
$results | Export-Csv -Path $csvFile -NoTypeInformation -Encoding UTF8
Write-Host "`n汇总 CSV: $csvFile" -ForegroundColor Green
Write-Host "详细 trx 日志: $trxDir\" -ForegroundColor Green

# 6. 输出关键结论
Write-Host "`n========== 关键结论 ==========" -ForegroundColor Green
$validVggish = $results | Where-Object { $_.VGGish_InferMs -ne $null }
$validMert = $results | Where-Object { $_.MERT_InferMs -ne $null }
if ($validVggish.Count -gt 0) {
    $fastest = $validVggish | Sort-Object VGGish_InferMs | Select-Object -First 1
    Write-Host ("VGGish 最快: {0}（推理 {1} ms, 实际 EP={2}）" -f $fastest.Configuration, $fastest.VGGish_InferMs, $fastest.VGGish_ActualEP)
}
if ($validMert.Count -gt 0) {
    $fastest = $validMert | Sort-Object MERT_InferMs | Select-Object -First 1
    Write-Host ("MERT   最快: {0}（推理 {1} ms, 实际 EP={2}）" -f $fastest.Configuration, $fastest.MERT_InferMs, $fastest.MERT_ActualEP)
}

# 7. 检查是否有失败的配置
$failed = $results | Where-Object { $_.ExitCode -ne 0 -or $_.ParseError -ne $null }
if ($failed.Count -gt 0) {
    Write-Host "`n========== 失败/告警配置 ==========" -ForegroundColor Yellow
    $failed | ForEach-Object {
        Write-Host ("  {0}: ExitCode={1}, Error={2}" -f $_.Configuration, $_.ExitCode, $_.ParseError)
    }
}
