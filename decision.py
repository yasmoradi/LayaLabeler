import os
import sys

# CSnakes bug #653: on Windows, torch fails to import in embedded Python unless sys.prefix is absolute.
if not os.path.isabs(sys.exec_prefix):
    sys.prefix = sys.exec_prefix = sys.base_exec_prefix = sys.executable

import torch  # noqa: E402,F401  (load PyTorch's DLLs into this process now, not on the first decision)
from laya import Router  # noqa: E402

_router = Router()
_router.preload(["english"])  # load the weights now, so no decision pays for them

def device() -> str:
    if torch.cuda.is_available():
        return f"CUDA ({torch.cuda.get_device_name(0)}), torch {torch.__version__}"
    return f"CPU, {torch.get_num_threads()} threads, torch {torch.__version__}"

def choose(state: str, question: str, options: dict[str, str]) -> dict[str, float]:
    result = _router.predict(state, {"q": {"type": "choice", "instructions": question, "criteria": options}}, model="english")
    return {name: float(p) for name, p in result["answers"]["q"]["probabilities"].items()}
