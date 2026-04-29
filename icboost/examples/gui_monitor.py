import os
import sys
from pathlib import Path

# Force this repository's package before any older icboost on PYTHONPATH/site-packages
# (otherwise assets/ignite64.jpg next to gui_tk.py is never seen).
_REPO = Path(__file__).resolve().parent.parent
if str(_REPO) not in sys.path:
    sys.path.insert(0, str(_REPO))

try:
    from PIL import Image as _PIL_die_photo_requires_pillow  # noqa: F401
except ImportError:
    print(
        "\n*** ICBoost GUI: serve Pillow per caricare ignite64.jpg (foto del die). ***\n"
        f"    Interprete usato ora: {sys.executable}\n"
        "    Installazione (stesso Python con cui lanci questo script):\n"
        "      python -m pip install pillow\n"
        "    Oppure: pip install -e .   dalla cartella progetto icboost (installa le dipendenze del progetto).\n",
        file=sys.stderr,
    )

# Point IGNITE_DIE_PHOTO at repo assets even when an older icboost on sys.path shadows sources
# (site-packages copy often has no ignite64.jpg → gray quadrant fallback).
def _resolve_repo_assets() -> Path:
    for parts in (
        ("icboost", "icboost", "assets"),
        ("ignite64py", "icboost", "assets"),
        ("ignite64py", "ignite64py", "assets"),
    ):
        p = _REPO.joinpath(*parts)
        if p.is_dir():
            return p
    return _REPO / "icboost" / "icboost" / "assets"


_repo_assets = _resolve_repo_assets()
_die_raw = os.environ.get("IGNITE_DIE_PHOTO", "").strip().strip('"').strip("'")
_need_die = True
if _die_raw:
    try:
        _need_die = not Path(_die_raw).expanduser().is_file()
    except Exception:
        _need_die = True
if _need_die:
    for _name in ("ignite64.jpg", "ignite64.jpeg", "ignite64_die_photo.png"):
        _p = _repo_assets / _name
        if _p.is_file():
            os.environ["IGNITE_DIE_PHOTO"] = str(_p.resolve())
            break

# Each new terminal starts without variables you exported elsewhere — use safe defaults
# unless you already set them for this session (e.g. OFFLINE=0 + DLL path for real HW).
if "OFFLINE" not in os.environ:
    os.environ["OFFLINE"] = "1"

from icboost.gui_tk import run_gui


if __name__ == "__main__":
    # START_CONFIG=0 skips hw.start_config even when OFFLINE=0 (optional).
    start_config = os.environ.get("START_CONFIG", "1").strip() not in {"0", "false", "no", "off", ""}
    run_gui(start_config=start_config, default_quad=os.environ.get("QUAD", "SW"))

