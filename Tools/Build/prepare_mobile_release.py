"""Make the isolated Unity build copy lean; retain original assets and manifests."""
from pathlib import Path
import runpy
import shutil

root = Path(__file__).resolve().parents[2]
runpy.run_path(str(root / "Tools/Build/prepare_unity_6000_6.py"), run_name="__main__")
streaming = root / ".build/unity-check/Assets/StreamingAssets"
removed = 0
demo = streaming / "MaiCharts/Original"
if demo.exists():
    if not demo.resolve().is_relative_to((root / ".build/unity-check").resolve()):
        raise RuntimeError("Demo directory is outside the isolated build copy.")
    removed += sum(p.stat().st_size for p in demo.rglob("*") if p.is_file())
    shutil.rmtree(demo)
    demo.with_suffix(".meta").unlink(missing_ok=True)
for path in list(streaming.rglob("*")):
    if path.is_file() and (path.suffix.lower() == ".md" or path.name == "SOURCE.json"):
        removed += path.stat().st_size
        path.unlink()
        path.with_suffix(path.suffix + ".meta").unlink(missing_ok=True)
print(f"MOBILE PAYLOAD: omitted {removed / 1024**2:.2f} MiB of demo charts and import documentation; runtime JSON retained.")
