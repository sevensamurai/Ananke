"""Writes tiny-bert/: a 4-dimension "model" whose output for each token is a fixed row of a lookup table, with the
inputs and output name of a BERT-style encoder, and its vocab.txt. The embedder's tests check exact vectors against
it. Regenerate with: uv run --with onnx python make_tiny_model.py"""
import os

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

VOCAB = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "core", ".", "identity", "is", "built", "on", "open", "##iddict", "[MASK]"]
# Row i is token i's vector: [CLS] and [SEP] are distinct so CLS and mean pooling differ.
TABLE = np.array([[0, 0, 0, 0]] + [[i, 1, 0, 0] if i in (2, 3) else [0, 0, i, 1] for i in range(1, len(VOCAB))],
                 dtype=np.float32)

os.makedirs("tiny-bert", exist_ok=True)
with open("tiny-bert/vocab.txt", "w") as f:
    f.write("\n".join(VOCAB) + "\n")

inputs = [helper.make_tensor_value_info(n, TensorProto.INT64, ["batch", "sequence"])
          for n in ("input_ids", "attention_mask", "token_type_ids")]
output = helper.make_tensor_value_info("last_hidden_state", TensorProto.FLOAT, ["batch", "sequence", 4])
gather = helper.make_node("Gather", ["table", "input_ids"], ["last_hidden_state"], axis=0)
graph = helper.make_graph([gather], "tiny-bert", inputs, [output], [numpy_helper.from_array(TABLE, "table")])
model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)], ir_version=8)
onnx.checker.check_model(model)
onnx.save(model, "tiny-bert/model.onnx")
print("rows", TABLE.tolist())
