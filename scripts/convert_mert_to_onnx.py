"""
将 m-a-p/MERT-v1-95M 模型转换为 ONNX 格式的脚本。

MERT 基于 HuBERT 架构，使用自定义模型类型 mert_model，
optimum-cli 无法直接识别，因此使用 torch.onnx.export 手动转换。
"""

import sys
import os

import torch
from transformers import AutoModel

# 模型本地路径
MODEL_DIR = os.path.join(os.path.dirname(__file__), "models", "MERT-v1-95M")
# ONNX 输出路径
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "models", "MERT-v1-95M-onnx")
ONNX_FILE = os.path.join(OUTPUT_DIR, "model.onnx")


def convert():
    """加载 MERT 模型并导出为 ONNX 格式"""
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    print(f"[1/4] 从本地加载模型: {MODEL_DIR}")
    model = AutoModel.from_pretrained(MODEL_DIR, trust_remote_code=True)
    model.eval()

    # MERT 采样率为 24000Hz，5 秒音频对应 120000 个采样点
    sample_rate = 24000
    duration_seconds = 5
    num_samples = sample_rate * duration_seconds

    # 创建 dummy 输入用于 tracing
    dummy_input = {
        "input_values": torch.randn(1, num_samples),
    }

    print(f"[2/4] 准备导出，输入形状: input_values={dummy_input['input_values'].shape}")

    # 导出 ONNX
    print(f"[3/4] 导出 ONNX 到: {ONNX_FILE}")
    with torch.no_grad():
        torch.onnx.export(
            model,
            (dummy_input["input_values"],),
            ONNX_FILE,
            input_names=["input_values"],
            output_names=["last_hidden_state"],
            dynamic_axes={
                "input_values": {0: "batch_size", 1: "sequence_length"},
                "last_hidden_state": {0: "batch_size", 1: "frame_length"},
            },
            opset_version=17,
            do_constant_folding=True,
        )

    # 合并外部数据为单文件
    print(f"[4/5] 合并外部数据为单文件...")
    import onnx

    onnx_model = onnx.load(ONNX_FILE, load_external_data=True)
    onnx_file_merged = os.path.join(OUTPUT_DIR, "model_merged.onnx")
    onnx.save_model(onnx_model, onnx_file_merged, save_as_external_data=False)

    # 删除旧的两文件方案
    data_file = ONNX_FILE + ".data"
    if os.path.exists(data_file):
        os.remove(data_file)
    os.remove(ONNX_FILE)

    # 重命名为 model.onnx
    os.rename(onnx_file_merged, ONNX_FILE)

    # 验证
    print(f"[5/5] 验证 ONNX 模型...")
    onnx_model = onnx.load(ONNX_FILE)
    onnx.checker.check_model(onnx_model)
    print("ONNX 模型验证通过！")

    # 打印模型输入输出信息
    print(f"\n--- 模型信息 ---")
    for inp in onnx_model.graph.input:
        print(f"输入: {inp.name}")
    for out in onnx_model.graph.output:
        print(f"输出: {out.name}")
    print(f"文件大小: {os.path.getsize(ONNX_FILE) / (1024 * 1024):.1f} MB")
    print(f"保存路径: {ONNX_FILE}")


if __name__ == "__main__":
    convert()
