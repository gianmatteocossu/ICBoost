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
    # START_CONFIG:
    # - 0/false/no/off  -> skip hw.start_config (read-only mode)
    # - 1/true          -> always run hw.start_config (writes config)
    # - auto            -> try to detect if chip is already configured; if yes, do read-only refresh
    # Default to AUTO to preserve current chip state unless explicitly requested.
    _sc_raw = os.environ.get("START_CONFIG", "auto").strip().lower()
    print(f"[gui_monitor] START_CONFIG env={os.environ.get('START_CONFIG', '')!r} parsed={_sc_raw!r}", flush=True)
    if _sc_raw in {"auto"}:
        start_config = "auto"
    else:
        start_config = _sc_raw not in {"0", "false", "no", "off", ""}
    print(f"[gui_monitor] start_config arg -> {start_config!r}", flush=True)
    base_cfg = os.environ.get("BASE_CONFIG_FILE", "").strip() or None
    si_cfg = os.environ.get("SI5340_CONFIG_FILE", "").strip() or None
    run_gui(
        start_config=start_config,
        default_quad=os.environ.get("QUAD", "SW"),
        base_config_file=base_cfg,
        si5340_config_file=si_cfg,
    )

