from __future__ import annotations

import io
import os
import pkgutil
import threading
import time
import tkinter as tk
from dataclasses import dataclass
from tkinter import ttk, filedialog
from pathlib import Path
from typing import Optional

from .api import Ignite64
from .calib_dco import CalibDCOParams, raw_fifo_to_fields

# Prefer user-supplied JPEG in assets/, then legacy PNG name.
_DIE_PHOTO_NAMES: tuple[str, ...] = ("ignite64.jpg", "ignite64.jpeg", "ignite64_die_photo.png")
# Banner IGNITE μd / INFN for Quadrants home (PNG).
_BANNER_IMAGE_NAMES: tuple[str, ...] = ("ignite_ud_banner.png",)

# Centered square on bitmap: linear extent ½ × ½ → area = ¼ of photo (NW/NE/SW/SE each = ¹⁄₁₆ of bitmap).
# Hint su bitmap per l’area attiva (può essere rettangolare); in GUI viene inscritto un quadrato = matrice 4 quadranti.
_DEFAULT_DIE_CHIP_BOX: tuple[float, float, float, float] = (0.20, 0.28, 0.80, 0.84)

# Optional dashed outline on the photo for the “TOP / periphery” band (normalized bitmap coords).
_DEFAULT_DIE_TOP_BAND: tuple[float, float, float, float] = (0.03, 0.70, 0.97, 0.97)

# Bold red quadrant labels: outline offsets then fill (matches prior Quadrants view).
_QUAD_LABEL_OUTLINE_OFFSETS: tuple[tuple[int, int], ...] = (
    (-2, -2),
    (-2, 2),
    (2, -2),
    (2, 2),
    (-2, 0),
    (2, 0),
    (0, -2),
    (0, 2),
    (-1, -1),
    (-1, 1),
    (1, -1),
    (1, 1),
)


def _parse_die_chip_box() -> tuple[float, float, float, float]:
    """
    Normalized rectangle on the die bitmap (fractions of width/height): left, top, right, bottom.
    The four quadrants (NW/NE/SW/SE) subdivide this rectangle; each GUI quadrant is **¼ of its area**
    (¼ of the red box — i.e. each matches one physical quadrant when the box hugs the active die).

    Default (omit env): centered square with **¼ of bitmap area** (side length ½), smaller than a full-frame chip.

    Env ``IGNITE_DIE_CHIP_BOX=left,top,right,bottom`` with comma-separated floats in [0, 1].
    Example for a chip slightly above center: ``0.18,0.12,0.82,0.78``.
    Omit or invalid → ``_DEFAULT_DIE_CHIP_BOX``.
    Use ``0,0,1,1`` for the full visible image area (same as splitting the whole canvas).
    """
    raw = os.environ.get("IGNITE_DIE_CHIP_BOX", "").strip()
    if not raw:
        return _DEFAULT_DIE_CHIP_BOX
    parts = [p.strip() for p in raw.replace(";", ",").split(",")]
    if len(parts) != 4:
        return _DEFAULT_DIE_CHIP_BOX
    try:
        a, b, c, d = (float(x) for x in parts)
        bl = max(0.0, min(1.0, a))
        bt = max(0.0, min(1.0, b))
        br = max(0.0, min(1.0, c))
        bb = max(0.0, min(1.0, d))
        if br <= bl or bb <= bt:
            return _DEFAULT_DIE_CHIP_BOX
        return (bl, bt, br, bb)
    except ValueError:
        return _DEFAULT_DIE_CHIP_BOX


def _parse_die_top_band() -> tuple[float, float, float, float]:
    """Band on bitmap for TOP-related outline (``IGNITE_DIE_TOP_BAND`` or default bottom strip)."""
    raw = os.environ.get("IGNITE_DIE_TOP_BAND", "").strip()
    if not raw:
        return _DEFAULT_DIE_TOP_BAND
    parts = [p.strip() for p in raw.replace(";", ",").split(",")]
    if len(parts) != 4:
        return _DEFAULT_DIE_TOP_BAND
    try:
        a, b, c, d = (float(x) for x in parts)
        bl = max(0.0, min(1.0, a))
        bt = max(0.0, min(1.0, b))
        br = max(0.0, min(1.0, c))
        bb = max(0.0, min(1.0, d))
        if br <= bl or bb <= bt:
            return _DEFAULT_DIE_TOP_BAND
        return (bl, bt, br, bb)
    except ValueError:
        return _DEFAULT_DIE_TOP_BAND


def _die_contain_geom(iw: int, ih: int, cw: int, ch: int) -> tuple[int, int, int, int]:
    """
    Uniform scale so the whole bitmap fits inside cw×ch (letterboxing).
    Returns resized width/height (nw, nh) and top-left canvas offsets (x_off, y_off).
    """
    if iw <= 0 or ih <= 0 or cw <= 0 or ch <= 0:
        return max(1, cw), max(1, ch), 0, 0
    scale = min(cw / iw, ch / ih)
    nw = max(1, int(round(iw * scale)))
    nh = max(1, int(round(ih * scale)))
    x_off = max(0, (cw - nw) // 2)
    y_off = max(0, (ch - nh) // 2)
    return nw, nh, x_off, y_off


def _canvas_to_norm_uv_contain(
    px: float, py: float, nw: int, nh: int, x_off: int, y_off: int
) -> tuple[float, float]:
    """Map canvas pixel to normalized coords on the displayed (contain-fit) image."""
    return (px - x_off) / nw, (py - y_off) / nh


def _quad_from_uv_in_chip_box(u: float, v: float, box: tuple[float, float, float, float]) -> Optional[str]:
    bl, bt, br, bb = box
    if u < bl or u > br or v < bt or v > bb:
        return None
    mu = (bl + br) * 0.5
    mv = (bt + bb) * 0.5
    west = u < mu
    north = v < mv
    if north:
        return "NW" if west else "NE"
    return "SW" if west else "SE"


def _die_matrix_box_square(box: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    """
    Inscrive un quadrato nel rettangolo ``IGNITE_DIE_CHIP_BOX`: il riquadro rosso segue la **matrice**
    (4 quadranti), non necessariamente tutto il die/package sul JPEG.
    """
    bl, bt, br, bb = box
    w = float(br - bl)
    h = float(bb - bt)
    if w <= 1e-9 or h <= 1e-9:
        return box
    s = min(w, h)
    cx = (bl + br) * 0.5
    cy = (bt + bb) * 0.5
    hl = s * 0.5
    nl = cx - hl
    nt = cy - hl
    nr = cx + hl
    nb = cy + hl
    return (
        max(0.0, min(1.0, nl)),
        max(0.0, min(1.0, nt)),
        max(0.0, min(1.0, nr)),
        max(0.0, min(1.0, nb)),
    )


def _die_resolve_top_band(matrix_box: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    """
    Fascia TOP (blu): se ``IGNITE_DIE_TOP_BAND`` è impostato → uso legacy.
    Altrimenti rettangolo **alto** sopra la matrice: stessa larghezza del quadrato rosso (bl..br),
    dal margine superiore bitmap fino al **bordo superiore** della matrice (bt), così il lato inferiore
    della fascia coincide col lato superiore del quadrato rosso.
    """
    raw = os.environ.get("IGNITE_DIE_TOP_BAND", "").strip()
    if raw:
        return _parse_die_top_band()
    bl, bt, br, _bb = matrix_box
    tt = 0.03
    if bt <= tt + 0.02:
        tt = max(0.0, bt - 0.12)
    return (bl, tt, br, bt)


def _bootstrap_die_photo_env() -> None:
    """
    If IGNITE_DIE_PHOTO is missing or points nowhere, set it to ignite64.jpg next to *this*
    source tree or typical checkout paths.

    When Python loads ``icboost`` from site-packages, ``Path(__file__).parent/assets`` may be
    empty even though the repo checkout has ignite64.jpg — without this, the GUI stays on the
    gray fallback.
    """
    raw = os.environ.get("IGNITE_DIE_PHOTO", "").strip().strip('"').strip("'")
    if raw:
        try:
            if Path(raw).expanduser().is_file():
                return
        except Exception:
            pass

    gt = Path(__file__).resolve()
    candidates: list[Path] = []
    for name in _DIE_PHOTO_NAMES:
        candidates.append(gt.parent / "assets" / name)
    try:
        cw = Path.cwd()
        for name in _DIE_PHOTO_NAMES:
            for base in (
                cw / "icboost" / "icboost" / "assets",
                cw / "ignite64py" / "icboost" / "assets",
                cw / "ignite64py" / "ignite64py" / "assets",
                cw / "ignite64py" / "assets",
                cw / "assets",
            ):
                candidates.append(base / name)
            candidates.append(cw / name)
    except Exception:
        pass
    for depth in (2, 3, 4):
        try:
            r = gt.parents[depth]
            for name in _DIE_PHOTO_NAMES:
                candidates.append(r / name)
                for base in (
                    r / "icboost" / "icboost" / "assets",
                    r / "ignite64py" / "icboost" / "assets",
                    r / "ignite64py" / "ignite64py" / "assets",
                ):
                    candidates.append(base / name)
        except IndexError:
            break

    seen: set[str] = set()
    for p in candidates:
        try:
            key = str(p.resolve())
        except Exception:
            key = str(p)
        if key in seen:
            continue
        seen.add(key)
        if p.is_file():
            os.environ["IGNITE_DIE_PHOTO"] = str(p.resolve())
            return


def _draw_quad_label(cv: tk.Canvas, cx: float, cy: float, q: str, fz: int) -> None:
    fnt = ("Segoe UI", fz, "bold")
    for dx, dy in _QUAD_LABEL_OUTLINE_OFFSETS:
        cv.create_text(cx + dx, cy + dy, text=q, fill="#000000", font=fnt, tags=("die_lbl",))
    cv.create_text(cx, cy, text=q, fill="#d40000", font=fnt, tags=("die_lbl",))
    cv.tag_raise("die_lbl")


def _gray_monitor_anchor_positions(
    cw: int,
    ch: int,
    *,
    inner_left: float,
    inner_top: float,
    inner_right: float,
    inner_bottom: float,
) -> dict[str, tuple[float, float]]:
    """
    Return (cx, cy) for NW/NE/SW/SE labels strictly in **canvas gray** (outside the inner
    rectangle where the photo or central schematic is drawn).

    Gray regions: top band [y < inner_top], bottom [y > inner_bottom], left strip
    [x < inner_left] between inner_top..inner_bottom, right strip [x > inner_right].
    """
    il, it, ir, ib = inner_left, inner_top, inner_right, inner_bottom
    m = 10.0
    hc = float(max(1, ch))
    wc = float(max(1, cw))
    hin = max(1.0, ib - it)

    def nw() -> tuple[float, float]:
        # Prefer top-left gray corner [0,il]×[0,it]
        if il >= m and it >= m:
            return (il / 2.0, it / 2.0)
        if it >= m:
            return (wc * 0.22, it / 2.0)
        if il >= m:
            return (il / 2.0, it + hin * 0.18)
        return (min(28.0, wc * 0.08), min(28.0, hc * 0.08))

    def ne() -> tuple[float, float]:
        if (wc - ir) >= m and it >= m:
            return ((ir + wc) / 2.0, it / 2.0)
        if it >= m:
            return (wc * 0.78, it / 2.0)
        if (wc - ir) >= m:
            return ((ir + wc) / 2.0, it + hin * 0.18)
        return (wc - min(28.0, wc * 0.08), min(28.0, hc * 0.08))

    def sw() -> tuple[float, float]:
        if il >= m and (hc - ib) >= m:
            return (il / 2.0, (ib + hc) / 2.0)
        if (hc - ib) >= m:
            return (wc * 0.22, (ib + hc) / 2.0)
        if il >= m:
            return (il / 2.0, it + hin * 0.82)
        return (min(28.0, wc * 0.08), hc - min(28.0, hc * 0.08))

    def se() -> tuple[float, float]:
        if (wc - ir) >= m and (hc - ib) >= m:
            return ((ir + wc) / 2.0, (ib + hc) / 2.0)
        if (hc - ib) >= m:
            return (wc * 0.78, (ib + hc) / 2.0)
        if (wc - ir) >= m:
            return ((ir + wc) / 2.0, it + hin * 0.82)
        return (wc - min(28.0, wc * 0.08), hc - min(28.0, hc * 0.08))

    return {"NW": nw(), "NE": ne(), "SW": sw(), "SE": se()}


def _draw_quadrant_monitor_panels_outside(
    cv: tk.Canvas,
    *,
    canvas_w: int,
    canvas_h: int,
    inner_left: float,
    inner_top: float,
    inner_right: float,
    inner_bottom: float,
    qtxt: dict[str, str],
    tag: str = "quad_mon",
) -> None:
    """
    Four monitoring blocks in **letterboxing gray only**: outside ``inner_*`` (photo
    bitmap on die view, or central 2×2 square on fallback view).
    """
    mf = ("Segoe UI", 9)
    iw = max(1.0, float(inner_right - inner_left))
    ih = max(1.0, float(inner_bottom - inner_top))
    tw = max(110, min(320, int(min(iw, ih) * 0.30)))

    pos = _gray_monitor_anchor_positions(
        canvas_w,
        canvas_h,
        inner_left=inner_left,
        inner_top=inner_top,
        inner_right=inner_right,
        inner_bottom=inner_bottom,
    )

    for q in ("NW", "NE", "SW", "SE"):
        t = (qtxt.get(q) or "").strip()
        if not t:
            continue
        cx, cy = pos[q]
        cv.create_text(
            cx,
            cy,
            text=t,
            anchor="center",
            fill="#1a1a1a",
            font=mf,
            width=tw,
            justify="center",
            tags=(tag,),
        )


def _env_truthy(name: str, default: str = "0") -> bool:
    v = os.environ.get(name, default).strip().lower()
    return v not in {"0", "false", "no", "off", ""}


def _dbg(msg: str) -> None:
    # Debug via env var; default ON to make the requested messages visible.
    if not _env_truthy("ICBOOST_GUI_DEBUG", "1"):
        return
    try:
        ts = time.strftime("%H:%M:%S")
    except Exception:
        ts = ""
    print(f"[icboost-gui {ts}] {msg}", flush=True)


def _maybe_load_pil():
    try:
        from PIL import Image, ImageTk  # type: ignore

        return Image, ImageTk
    except Exception:
        return None, None

def _tk_fit_image(photo: tk.PhotoImage, w: int, h: int) -> tk.PhotoImage:
    """
    Best-effort resize without Pillow using integer subsample.
    Keeps aspect ratio, only downscales.
    """
    iw = int(photo.width())
    ih = int(photo.height())
    if w <= 0 or h <= 0 or iw <= 0 or ih <= 0:
        return photo
    fx = max(1, int((iw + w - 1) / w))
    fy = max(1, int((ih + h - 1) / h))
    f = max(fx, fy)
    if f <= 1:
        return photo
    # subsample() mutates the PhotoImage; clone first so each quadrant can scale independently.
    base = photo.copy()
    return base.subsample(f, f)


def _pil_resample_lanczos(Image):
    return getattr(getattr(Image, "Resampling", Image), "LANCZOS", Image.LANCZOS)


@dataclass(frozen=True)
class BlockMapping:
    """
    Map a GUI "block" (0..3 inside a quadrant) to the MatIDs (0..15) it contains.

    MatIDs are assumed arranged in a 4x4 grid:
      mat_id = row*4 + col, row/col in 0..3

    A block is a 2x2 group of MatIDs:
      block_id = (row//2)*2 + (col//2)  -> 0..3

    The analog column configuration is hosted by a single "owner" mat inside the block.
    The analog column configuration is hosted by a single "owner" mat inside the block.
    For IGNITE64 this is the "control" mattonella: {1,3,9,11} (one per 2x2 block).
    """

    analog_owner_by_block: tuple[int, int, int, int] = (1, 3, 9, 11)
    # From datasheet "IGNITE32 with 4 blocks 16x16 with different AFE and analog services":
    # TL=GM NOLDO, TR=LP NOLDO, BL=GM LDO, BR=LP LDO
    block_kind_by_block: tuple[str, str, str, str] = ("GM NOLDO", "LP NOLDO", "GM LDO", "LP LDO")

    def mats_in_block(self, block_id: int) -> list[int]:
        if block_id < 0 or block_id > 3:
            raise ValueError("block_id out of range (expected 0..3)")
        r0 = (block_id // 2) * 2
        c0 = (block_id % 2) * 2
        return [(r0 + dr) * 4 + (c0 + dc) for dr in (0, 1) for dc in (0, 1)]

    def analog_owner_mat(self, block_id: int) -> int:
        if block_id < 0 or block_id > 3:
            raise ValueError("block_id out of range (expected 0..3)")
        return int(self.analog_owner_by_block[block_id])

    def block_kind(self, block_id: int) -> str:
        if block_id < 0 or block_id > 3:
            raise ValueError("block_id out of range (expected 0..3)")
        return str(self.block_kind_by_block[block_id])


class Ignite64Gui(tk.Tk):
    def __init__(
        self,
        hw: Ignite64,
        *,
        default_quad: str = "SW",
        mapping: Optional[BlockMapping] = None,
        offline: bool = False,
    ) -> None:
        super().__init__()
        self.hw = hw
        self.offline = bool(offline)
        self.mapping = mapping or BlockMapping()
        self.title("IGNITE64 ASIC")
        self.geometry("1400x900")

        # Match root window to ttk frame background (avoids default Tk dark fill around widgets on some platforms).
        try:
            _bg = ttk.Style().lookup("TFrame", "background")
        except Exception:
            _bg = ""
        if not _bg:
            _bg = "SystemButtonFace"
        self._window_bg = _bg
        try:
            self.configure(background=_bg)
        except Exception:
            try:
                self.configure(background="#f0f0f0")
                self._window_bg = "#f0f0f0"
            except Exception:
                pass
        self._icon_photo_ref: Optional[object] = None
        # Delay icon setup: it tends to work better after the window exists.
        self.after(50, self._set_app_icon)

        self._nav_stack: list[ttk.Frame] = []

        self.quad_var = tk.StringVar(value=str(default_quad).strip().upper())
        self._top_apply_quad_var = tk.StringVar(value=str(default_quad).strip().upper())
        self.cmd_var = tk.StringVar(value="")
        self._fifo_auto_after_id: Optional[str] = None
        self._quadrants_mon_job: Optional[str] = None
        self._quadrants_mon_seq: int = 0
        self._quad_monitor_canvas: dict[str, str] = {q: "" for q in ("NW", "NE", "SW", "SE")}
        self._quadrants_mon_rr_idx: int = 0
        self._die_canvas_redraw: Optional[object] = None
        self._top_snapshot_texts: dict[str, tk.Text] = {}
        self._quadrants_top_mon_var = tk.StringVar(value="TOP (read): —")
        self._quadrants_top_mon_job: Optional[str] = None
        self._quadrants_top_mon_seq: int = 0
        self._macro_script_paths: list[Path] = []
        self._home_quad_mon_texts: Optional[dict[str, tk.Text]] = None
        self._banner_photo_ref: Optional[object] = None
        # Cache "snapshot" per aggiornare subito la view Block (pix ON + codice FTDAC)
        # dopo la configurazione di base all'apertura GUI.
        self._mat_snapshot_cache: dict[str, dict[int, dict[str, object]]] = {q: {} for q in ("NW", "NE", "SW", "SE")}
        self._mat_snapshot_prefill_active: bool = False
        self._mat_snapshot_prefill_from_file: bool = False
        self._snapshot_capture_in_progress: bool = False
        self._analog_power_btn: Optional[tk.Button] = None
        self._mat_snapshot_disable_after_id: Optional[str] = None
        # Prevent UI-freezing reentry on bulk "ALL" operations (FEON/PIXON/TDCON).
        self._bulk_all_in_progress: bool = False
        # Cache for DCO/TDC calibration results (from Calib DCO dialog).
        # Key: (quad_str, mat_id, pix_id) -> {"dco0_ps": float, "dco1_ps": float, "lsb_ps": float}
        self._dco_calib_cache: dict[tuple[str, int, int], dict[str, float]] = {}
        # External IREF DAC is write-only; keep last value set for UI "Read" (C# NumericUpDown behavior).
        try:
            _iref0 = float(os.environ.get("IGNITE64_IREF_MV", "900").strip())
        except Exception:
            _iref0 = 900.0
        self._iref_last_mv_by_quad: dict[str, float] = {q: float(_iref0) for q in ("SW", "NW", "SE", "NE")}
        self._iref_var = tk.StringVar(value=f"{float(_iref0):.6g}")
        self._iref_meas_var = tk.StringVar(value="")
        # Quadrant overview: persist ALL toggles and allow forced HW refresh.
        self._quad_all_vars: dict[str, dict[str, tk.BooleanVar]] = {}
        self._blocks_view_refresh_cb: Optional[object] = None
        self._blocks_view_force_hw: bool = False
        # Serialize compound HW sequences (select_quadrant + follow-up I2C/FIFO) vs background monitors.
        self._hw_seq_lock = threading.Lock()

        # --- UI scaffold (must be created during __init__) ---
        root = ttk.Frame(self, padding=10)
        root.pack(fill="both", expand=True)

        topbar = ttk.Frame(root)
        topbar.pack(fill="x", pady=(0, 4))

        top_left = ttk.Frame(topbar)
        top_left.pack(side="left")

        self.back_btn = ttk.Button(top_left, text="Back", command=self.nav_back, state="disabled")
        self.back_btn.pack(side="left", padx=(0, 8))

        ttk.Button(top_left, text="Home", command=self.nav_home).pack(side="left", padx=(0, 8))

        ttk.Separator(top_left, orient="vertical").pack(side="left", fill="y", padx=8)
        self.offline_lbl = ttk.Label(top_left, text=("OFFLINE" if self.offline else "HW"))
        self.offline_lbl.pack(side="left")

        ttk.Separator(topbar, orient="vertical").pack(side="left", fill="y", padx=8)

        # Pulsanti HW/calibrazione: frame fisso a destra (non viene schiacciato dalla zona Cmd)
        top_right = ttk.Frame(topbar)
        top_right.pack(side="right")
        ttk.Button(top_right, text="DCO map…", command=self._open_dco_cal_map).pack(side="left", padx=(0, 4))
        ttk.Button(top_right, text="Calib DCO…", command=self._calib_dco_dialog).pack(side="left", padx=(0, 4))
        ttk.Button(top_right, text="Reconnect USB", command=self._reconnect_usb).pack(side="left", padx=(0, 0))

        # Verbose toggle (runtime): enables I2C trace prints in device.py
        try:
            self._verbose_var = tk.BooleanVar(
                value=str(os.environ.get("ICBOOST_I2C_TRACE", "0")).strip().lower() not in {"", "0", "off", "false"}
            )
        except Exception:
            self._verbose_var = tk.BooleanVar(value=False)

        def _toggle_verbose() -> None:
            try:
                on = bool(self._verbose_var.get())
            except Exception:
                on = False
            try:
                # Default mode: errors only (avoids background monitor noise).
                os.environ["ICBOOST_I2C_TRACE"] = "errors" if on else "0"
                if not on:
                    os.environ.pop("ICBOOST_I2C_TRACE_UNTIL", None)
            except Exception:
                pass
            self._set_status(f"Verbose I2C trace: {'ERRORS' if on else 'OFF'}")
            _dbg(f"Verbose I2C trace toggled -> {on}")

        ttk.Checkbutton(top_right, text="Verbose", variable=self._verbose_var, command=_toggle_verbose).pack(
            side="left", padx=(10, 0)
        )

        def _trace_3s() -> None:
            try:
                os.environ["ICBOOST_I2C_TRACE"] = "armed"
                os.environ["ICBOOST_I2C_TRACE_UNTIL"] = str(time.time() + 3.0)
            except Exception:
                pass
            self._set_status("I2C trace armed for 3s (all ops)")
            _dbg("I2C trace armed for 3s")

        ttk.Button(top_right, text="Trace 3s", command=_trace_3s).pack(side="left", padx=(6, 0))

        # Monitor toggle (runtime): reduce I2C traffic/latency during interactive work.
        try:
            self._monitor_var = tk.BooleanVar(value=not _env_truthy("ICBOOST_DISABLE_MONITOR", "0"))
        except Exception:
            self._monitor_var = tk.BooleanVar(value=True)

        def _toggle_monitor() -> None:
            try:
                on = bool(self._monitor_var.get())
            except Exception:
                on = True
            try:
                os.environ["ICBOOST_DISABLE_MONITOR"] = "0" if on else "1"
            except Exception:
                pass
            self._set_status(f"Monitor: {'ON' if on else 'OFF'} (home refresh)")
            _dbg(f"Monitor toggled -> {on}")

        ttk.Checkbutton(top_right, text="Monitor", variable=self._monitor_var, command=_toggle_monitor).pack(
            side="left", padx=(10, 0)
        )

        # Cmd + Macro: due righe, colonna centrale larga
        cmd_macro = ttk.Frame(topbar)
        cmd_macro.pack(side="left", fill="both", expand=True, padx=(0, 12))
        cmd_macro.columnconfigure(1, weight=1, minsize=320)

        ttk.Label(cmd_macro, text="Cmd:").grid(row=0, column=0, sticky="e", padx=(0, 6))
        cmd_entry = ttk.Entry(cmd_macro, textvariable=self.cmd_var, width=72)
        cmd_entry.grid(row=0, column=1, sticky="ew")
        ttk.Button(cmd_macro, text="Run", command=self.run_command).grid(row=0, column=2, padx=(8, 0))
        cmd_entry.bind("<Return>", lambda _ev: self.run_command())

        ttk.Label(cmd_macro, text="Macro:").grid(row=1, column=0, sticky="e", padx=(0, 6), pady=(6, 0))
        self._macro_file_cb = ttk.Combobox(cmd_macro, state="readonly", width=70)
        self._macro_file_cb.grid(row=1, column=1, sticky="ew", pady=(6, 0))
        ttk.Button(cmd_macro, text="Source", command=self._run_macro_source_button).grid(
            row=1, column=2, padx=(8, 0), pady=(6, 0)
        )

        self._refresh_macro_file_list()
        self._macro_file_cb.bind("<<ComboboxSelected>>", self._on_macro_file_selected)

        self.content = ttk.Frame(root)
        self.content.pack(fill="both", expand=True)

        # No background I2C by default: keep the bus idle unless the user presses a button.
        # Set ICBOOST_AUTO_REFRESH=1 to re-enable automatic TOP summary updates on quad change.
        if _env_truthy("ICBOOST_AUTO_REFRESH", "0"):
            self.quad_var.trace_add("write", lambda *_a: self.after_idle(self._touch_quadrants_top_mon_from_trace))

        # Show home immediately (so the window is never blank)
        self.nav_home()

    def _reconnect_usb(self) -> None:
        """
        Best-effort recovery for WDU_Transfer / USB transport wedging.
        Stops FIFO auto loops, runs bus recovery, then re-initializes HW bring-up without rewriting config.
        """
        if self.offline:
            self._set_status("Reconnect USB: offline")
            return

        self._fifo_auto_stop()
        q = str(self.quad_var.get()).strip().upper()

        def work() -> None:
            try:
                try:
                    self.hw.i2c_bus_recovery()
                except Exception:
                    pass
                base_cfg = os.environ.get("BASE_CONFIG_FILE", "").strip() or None
                si_cfg = os.environ.get("SI5340_CONFIG_FILE", "").strip() or None
                self.hw.init_hw(
                    q,
                    full_cfg=base_cfg,
                    si5340_cfg=str(si_cfg or "Si5340-RevD_Crystal-Registers_bis.txt"),
                )
                self.after(0, lambda: self._set_status(f"Reconnect USB: OK (Q={q})"))
            except Exception as e:
                self.after(0, lambda e=e: self._set_status(f"Reconnect USB error: {e}"))

        self._set_status("Reconnect USB: running…")
        threading.Thread(target=work, daemon=True).start()

    def _start_initial_snapshot_prefill(self, default_quad: str, base_config_file: Optional[str]) -> None:
        """
        Kick off the initial "snapshot" prefill without blocking GUI startup.

        Note: this is scheduled via `after()` from `run_gui()` so the mainloop is already running
        and Windows won't show a blank/unresponsive window while we do I2C operations.
        """
        if self.offline:
            return

        qkey = str(default_quad).strip().upper()

        def do_file() -> int:
            cached_mats = 0
            cfg_base = Path(__file__).resolve().parents[1] / "ConfigurationFiles"
            default_full_cfg_name = "IGNITE64_configSW_26.04.29.12.31.45.txt"

            # base_config_file can be either:
            # - a filename inside ConfigurationFiles/
            # - an absolute/relative path
            if base_config_file:
                p = Path(str(base_config_file))
                full_cfg_path = p if (p.is_absolute() or len(p.parts) > 1) else (cfg_base / p)
            else:
                # Pick newest config file if present, else fall back to a known default name.
                candidates = sorted(
                    cfg_base.glob("IGNITE64_configSW_*.txt"),
                    key=lambda x: x.stat().st_mtime,
                    reverse=True,
                )
                full_cfg_path = candidates[0] if candidates else (cfg_base / default_full_cfg_name)

            # Assicura TOP[0]=DC per spegnere il default config (C# usa questo flag in GUI).
            self.hw.unlock_top_default_config(retries=5, delay_s=0.1)
            try:
                self.hw.TopReadout("i2c")
            except Exception:
                pass

            cached_mats = self._prefill_mat_snapshot_from_full_configuration_file(full_cfg_path, qkey)
            return int(cached_mats)

        def on_done_file(cm: int) -> None:
            try:
                cm = int(cm)
            except Exception:
                cm = 0
            self._mat_snapshot_prefill_active = True
            self._mat_snapshot_prefill_from_file = True
            self._snapshot_capture_in_progress = False
            _dbg(
                f"snapshot created (quando ri scarichi la mappa di tutti i registri nuovamente) (file-prefill) "
                f"(quad={qkey}) cached_mats={cm}/16"
            )

        def do_hw() -> int:
            return int(self._capture_mat_snapshot(qkey))

        def on_done_hw(cm: int) -> None:
            try:
                cm = int(cm)
            except Exception:
                cm = 0
            self._mat_snapshot_prefill_active = True
            self._mat_snapshot_prefill_from_file = False
            self._snapshot_capture_in_progress = False
            _dbg(
                f"snapshot created (quando ri scarichi la mappa di tutti i registri nuovamente) (hw) "
                f"(quad={qkey}) cached_mats={cm}/16"
            )

        def on_fail_file(err: Exception) -> None:
            _dbg(f"file-prefill snapshot failed: {err!r}; fallback to hw snapshot")
            self._set_status(f"Snapshot prefill fallback (hw) (Q={qkey})")

            def work_hw() -> None:
                try:
                    cm = int(do_hw())
                except Exception as e:
                    self.after(0, lambda e=e: self._set_status(f"Snapshot prefill (hw) error: {e}"))
                    return
                self.after(0, lambda cm=cm: on_done_hw(cm))
            threading.Thread(target=work_hw, daemon=True).start()

        self._snapshot_capture_in_progress = True
        self._set_status(f"Snapshot prefill (file) (Q={qkey})")

        def work_file() -> None:
            try:
                cm = int(do_file())
            except Exception as e:
                self.after(0, lambda e=e: on_fail_file(e))
                return
            self.after(0, lambda cm=cm: on_done_file(cm))

        threading.Thread(target=work_file, daemon=True).start()

    def _start_initial_snapshot_refresh_from_hw(self, default_quad: str) -> None:
        """
        Read current HW state (snapshot) in background to reflect the *actual* chip state
        without rewriting configuration.
        """
        if self.offline:
            return
        qkey = str(default_quad).strip().upper()
        self._snapshot_capture_in_progress = True
        self._set_status(f"Snapshot refresh (HW) (Q={qkey})")

        def work() -> None:
            try:
                cm = int(self._capture_mat_snapshot(qkey))
            except Exception as e:
                self.after(0, lambda e=e: self._set_status(f"Snapshot refresh (HW) error: {e}"))
                return

            def done() -> None:
                self._mat_snapshot_prefill_active = True
                self._mat_snapshot_prefill_from_file = False
                self._snapshot_capture_in_progress = False
                _dbg(
                    f"snapshot created (quando ri scarichi la mappa di tutti i registri nuovamente) (hw-refresh) "
                    f"(quad={qkey}) cached_mats={int(cm)}/16"
                )

            self.after(0, done)

        threading.Thread(target=work, daemon=True).start()

    def _quadrant_full_refresh(self, quad: str) -> None:
        """
        Refresh snapshot cache for the given quadrant by reading all MAT registers needed by the
        Quadrant→Blocks view: PIXON (bit6), FEON (bit7), and FTDAC (regs 76..107).

        Known limitation: MAT 4..7 cannot be addressed individually (I2C stack issue). For those MATs
        we do not read HW; we keep / synthesize state based on the last quadrant-wide ALL toggles.
        """
        q = str(quad).strip().upper()

        def do() -> None:
            self.hw.select_quadrant(q)
            qc = self._mat_snapshot_cache.setdefault(q, {})
            for mid in range(16):
                if 4 <= int(mid) <= 7:
                    # Best-effort: represent 4..7 using last known quadrant-wide ALL state.
                    ent = qc.get(int(mid))
                    if not isinstance(ent, dict):
                        ent = {}
                        qc[int(mid)] = ent
                    po = ent.get("pix_on")
                    fo = ent.get("fe_on")
                    if not isinstance(po, list) or len(po) < 64:
                        po = [False] * 64
                    if not isinstance(fo, list) or len(fo) < 64:
                        fo = [True] * 64
                    v = self._quad_all_vars.get(q, {})
                    try:
                        if "px" in v and isinstance(v["px"], tk.BooleanVar):
                            po = [bool(v["px"].get())] * 64
                    except Exception:
                        pass
                    try:
                        if "fe" in v and isinstance(v["fe"], tk.BooleanVar):
                            fo = [bool(v["fe"].get())] * 64
                    except Exception:
                        pass
                    ent["pix_on"] = po
                    ent["fe_on"] = fo
                    continue

                r = self.hw.readMatPixelsAndFTDAC(q, mattonella=int(mid))
                if isinstance(r, dict):
                    qc[int(mid)] = r

        self._with_hw(do, busy=f"Refresh quadrant snapshot (Q={q})")
        # Enable cache usage for immediate redraw.
        self._mat_snapshot_prefill_active = True
        self._mat_snapshot_prefill_from_file = False
        # If Blocks view is open, force redraw after cache refresh.
        try:
            cb = self._blocks_view_refresh_cb
            if cb:
                self.after(0, cb)  # type: ignore[misc]
        except Exception:
            pass

    def _iter_die_photo_asset_dirs(self) -> list[Path]:
        """Directories that may contain die photos (order matters)."""
        dirs: list[Path] = []
        here = Path(__file__).resolve().parent
        dirs.append(here / "assets")

        try:
            import icboost as _pkg

            dirs.append(Path(_pkg.__file__).resolve().parent / "assets")
        except Exception:
            pass

        d = here
        for _ in range(10):
            dirs.append(d / "assets")
            for base in (
                d / "icboost" / "icboost" / "assets",
                d / "ignite64py" / "icboost" / "assets",
                d / "ignite64py" / "ignite64py" / "assets",
            ):
                dirs.append(base)
            if d.parent == d:
                break
            d = d.parent

        cw = Path.cwd()
        dirs.extend(
            (
                cw / "icboost" / "icboost" / "assets",
                cw / "ignite64py" / "icboost" / "assets",
                cw / "ignite64py" / "ignite64py" / "assets",
                cw / "ignite64py" / "assets",
                cw / "assets",
            )
        )

        seen: set[str] = set()
        out: list[Path] = []
        for p in dirs:
            try:
                key = str(p.resolve())
            except Exception:
                key = str(p)
            if key not in seen:
                seen.add(key)
                out.append(p)
        return out

    def _iter_die_photo_paths(self) -> list[Path]:
        """Candidate image paths: per directory, try ignite64.jpg then jpeg then PNG."""
        raw: list[Path] = []
        for base in self._iter_die_photo_asset_dirs():
            for name in _DIE_PHOTO_NAMES:
                raw.append(base / name)

        seen: set[str] = set()
        out: list[Path] = []
        for p in raw:
            try:
                key = str(p.resolve())
            except Exception:
                key = str(p)
            if key not in seen:
                seen.add(key)
                out.append(p)
        return out

    def _load_die_photo_pil(self, Image_mod: Optional[object] = None) -> tuple[Optional[object], str]:
        """
        Load the square die photograph for the Quadrants home screen.
        Returns (PIL.Image RGBA, description) or (None, short reason).
        """
        if Image_mod is None:
            Image_mod, _ImageTk = _maybe_load_pil()
        if Image_mod is None:
            return None, "Pillow missing"

        _bootstrap_die_photo_env()

        env = os.environ.get("IGNITE_DIE_PHOTO", "").strip().strip('"').strip("'")
        if env:
            ep = Path(env)
            if not ep.is_file():
                return None, f"IGNITE_DIE_PHOTO not found: {env}"
            try:
                return Image_mod.open(str(ep)).convert("RGBA"), str(ep)
            except Exception as e:
                return None, f"IGNITE_DIE_PHOTO open failed: {e}"

        for fp in self._iter_die_photo_paths():
            if not fp.is_file():
                continue
            try:
                return Image_mod.open(str(fp)).convert("RGBA"), str(fp)
            except Exception:
                continue

        try:
            import importlib.resources as ir

            for fn in _DIE_PHOTO_NAMES:
                ref = ir.files("icboost").joinpath("assets", fn)
                if ref.is_file():
                    im = Image_mod.open(io.BytesIO(ref.read_bytes())).convert("RGBA")
                    return im, f"icboost/assets/{fn} (importlib)"
        except Exception:
            pass

        for fn in _DIE_PHOTO_NAMES:
            blob = pkgutil.get_data("icboost", f"assets/{fn}")
            if blob:
                try:
                    im = Image_mod.open(io.BytesIO(blob)).convert("RGBA")
                    return im, f"icboost/assets/{fn} (pkgutil)"
                except Exception:
                    continue

        return (
            None,
            "no die image — add icboost/assets/ignite64.jpg (or ignite64_die_photo.png) or set IGNITE_DIE_PHOTO",
        )

    def _load_banner_pil(self, Image_mod: Optional[object] = None) -> tuple[Optional[object], str]:
        """Logo / banner principale (home Quadrants). Returns (PIL.Image RGBA, path or reason)."""
        if Image_mod is None:
            Image_mod, _ImageTk = _maybe_load_pil()
        if Image_mod is None:
            return None, "Pillow missing"

        env = os.environ.get("IGNITE64_BANNER_PNG", "").strip().strip('"').strip("'")
        if env:
            ep = Path(env)
            if ep.is_file():
                try:
                    im = Image_mod.open(str(ep)).convert("RGBA")
                    return self._banner_make_transparent(im), str(ep)
                except Exception as e:
                    return None, f"IGNITE64_BANNER_PNG: {e}"
        base = Path(__file__).resolve().parent / "assets"
        for name in _BANNER_IMAGE_NAMES:
            fp = base / name
            if fp.is_file():
                try:
                    im = Image_mod.open(str(fp)).convert("RGBA")
                    return self._banner_make_transparent(im), str(fp)
                except Exception:
                    continue
        try:
            import importlib.resources as ir

            for name in _BANNER_IMAGE_NAMES:
                ref = ir.files("icboost").joinpath("assets", name)
                if ref.is_file():
                    im = Image_mod.open(io.BytesIO(ref.read_bytes())).convert("RGBA")
                    return self._banner_make_transparent(im), f"icboost/assets/{name}"
        except Exception:
            pass
        for name in _BANNER_IMAGE_NAMES:
            blob = pkgutil.get_data("icboost", f"assets/{name}")
            if blob:
                try:
                    im = Image_mod.open(io.BytesIO(blob)).convert("RGBA")
                    return self._banner_make_transparent(im), f"icboost/assets/{name}"
                except Exception:
                    continue
        return None, "no banner — add icboost/assets/ignite_ud_banner.png or set IGNITE64_BANNER_PNG"

    def _banner_make_transparent(self, im: object) -> object:
        """
        Make white-ish banner background transparent so it blends with the ttk window background.
        Best-effort: if Pillow APIs change/missing, returns the original image.
        """
        try:
            try:
                from PIL import ImageChops  # type: ignore[import-not-found]
            except Exception:
                ImageChops = None  # type: ignore[assignment]

            # Use top-left pixel as background reference (usually white).
            px = im.getpixel((0, 0))
            if not (isinstance(px, tuple) and len(px) >= 3):
                return im
            br, bg, bb = int(px[0]), int(px[1]), int(px[2])
            # If the corner isn't bright, fall back to "near-white".
            corner_bright = (br + bg + bb) / 3.0
            if corner_bright < 200:
                br, bg, bb = 255, 255, 255
            tol = 28  # background tolerance

            r, g, b, a = im.split()

            def _mask(v: int, ref: int) -> int:
                return 255 if abs(int(v) - int(ref)) <= tol else 0

            mr = r.point(lambda v: _mask(v, br))
            mg = g.point(lambda v: _mask(v, bg))
            mb = b.point(lambda v: _mask(v, bb))

            if ImageChops is None:
                return im
            # bg_mask = pixels that match background in all channels (0/255 mask)
            bg_mask = ImageChops.multiply(ImageChops.multiply(mr, mg), mb)

            # New alpha: keep original alpha, but set to 0 where bg_mask is 255.
            inv = bg_mask.point(lambda v: 255 - int(v))
            new_a = ImageChops.multiply(a, inv)
            im.putalpha(new_a)
            return im
        except Exception:
            return im

    # -----------------
    # Navigation helpers
    # -----------------
    def _push_view(self, frame: ttk.Frame) -> None:
        if self._nav_stack:
            self._nav_stack[-1].pack_forget()
        self._nav_stack.append(frame)
        frame.pack(fill="both", expand=True)
        self.back_btn.configure(state=("normal" if len(self._nav_stack) > 1 else "disabled"))

    def nav_back(self) -> None:
        if len(self._nav_stack) <= 1:
            return
        old = self._nav_stack.pop()
        old.destroy()
        self._nav_stack[-1].pack(fill="both", expand=True)
        self.back_btn.configure(state=("normal" if len(self._nav_stack) > 1 else "disabled"))

    def nav_home(self) -> None:
        while self._nav_stack:
            f = self._nav_stack.pop()
            f.destroy()
        self._push_view(self._build_quadrants_view(self.content))

    # --------------
    # Status handling
    # --------------
    def _set_status(self, msg: str, *, max_len: int = 220) -> None:
        """API mantenuta per compatibilità; la barra stato sotto la toolbar è stata rimossa."""
        _ = (msg, max_len)

    def _macros_examples_dir(self) -> Path:
        """Cartella ``examples/macros`` del pacchetto (stessa usata da ``source nome.py``)."""
        return Path(__file__).resolve().parents[1] / "examples" / "macros"

    def _refresh_macro_file_list(self) -> None:
        d = self._macros_examples_dir()
        paths = sorted(d.glob("*.py")) if d.is_dir() else []
        self._macro_script_paths = paths
        labels = [p.name for p in paths]
        self._macro_file_cb["values"] = ["— choose —"] + labels
        try:
            self._macro_file_cb.current(0)
        except Exception:
            pass

    def _invoke_macro_source_by_index(self, idx: int) -> None:
        """Esegue lo stesso caricamento della barra ``source nome.py``."""
        if self.offline:
            self._set_status("Macro: offline")
            return
        if idx <= 0 or idx - 1 >= len(self._macro_script_paths):
            self._set_status("Pick a .py file in the Macro list")
            return
        name = self._macro_script_paths[idx - 1].name
        try:
            self._source_macro(name)
        except Exception as e:
            self._set_status(f"Macro: {e}")

    def _on_macro_file_selected(self, _ev: Optional[object] = None) -> None:
        """Selezione nella lista = ``source`` della routine (come da barra comandi)."""
        idx = int(self._macro_file_cb.current())
        if idx <= 0:
            return
        self._invoke_macro_source_by_index(idx)

    def _run_macro_source_button(self) -> None:
        """Rilancia ``source`` sul file attualmente selezionato (es. per rieseguire)."""
        self._invoke_macro_source_by_index(int(self._macro_file_cb.current()))

    def _resolve_macro_path(self, s: str) -> Path:
        p = Path(s.strip().strip('"').strip("'"))
        if p.is_absolute():
            return p
        # Try relative to cwd first, then examples/macros
        cwd = Path.cwd()
        cand1 = cwd / p
        if cand1.exists():
            return cand1
        cand2 = Path(__file__).resolve().parents[1] / "examples" / "macros" / p
        return cand2

    def _source_macro(self, path_s: str) -> None:
        p = self._resolve_macro_path(path_s)
        if not p.exists():
            raise FileNotFoundError(f"Macro not found: {p}")
        code = p.read_text(encoding="utf-8")
        ns: dict[str, object] = {}
        exec(compile(code, str(p), "exec"), ns, ns)
        fn_name = p.stem
        if fn_name in ns and callable(ns[fn_name]):
            quad = self.quad_var.get()
            ns[fn_name](self.hw, quad)  # type: ignore[misc]
            self._set_status(f"source OK: {p.name} (called {fn_name}(hw, {quad}))")
        else:
            self._set_status(f"source OK: {p.name} (loaded)")

    def run_command(self) -> None:
        """
        Execute a simple command while GUI is running.
          - source <path.py>    loads a python file; auto-calls function matching filename (if present)
          - any other text is executed as Python with `hw`, `quad`, `gui` available
        """
        cmd = self.cmd_var.get().strip()
        if not cmd:
            return
        self.cmd_var.set("")
        try:
            if cmd.lower().startswith("source "):
                self._source_macro(cmd[7:].strip())
                return
            ctx = {"hw": self.hw, "quad": self.quad_var.get(), "gui": self}
            try:
                # Try expression first
                out = eval(cmd, ctx, ctx)
                if out is not None:
                    self._set_status(str(out))
                else:
                    self._set_status("OK")
            except SyntaxError:
                exec(cmd, ctx, ctx)
                self._set_status("OK")
        except Exception as e:
            self._set_status(f"Cmd error: {e}")

    def _calib_dco_dialog(self) -> None:
        """
        Dialog equivalent to C# `MultiTestSelForm("CalDCO")` + save file, then runs `run_calib_dco` in a worker thread.
        In modalità offline la finestra si apre comunque (parametri e salvataggio report non eseguono HW).
        """
        win = tk.Toplevel(self)
        win.title("DCO Calibration Settings")
        win.transient(self)
        frm = ttk.Frame(win, padding=12)
        frm.pack(fill="both", expand=True)

        if self.offline:
            ttk.Label(
                frm,
                text="Offline mode: you can set parameters; Start does not run hardware calibration.",
                foreground="#555555",
            ).grid(row=0, column=0, columnspan=2, sticky="w", pady=(0, 8))
            base_row = 1
        else:
            base_row = 0

        quad_labels = (
            "SW (South-West)",
            "NW (North-West)",
            "SE (South-East)",
            "NE (North-East)",
            "ALL Quadrants",
            "BROADCAST",
        )
        br = int(base_row)
        ttk.Label(frm, text="Quadrant").grid(row=br + 0, column=0, sticky="w", pady=2)
        quad_cb = ttk.Combobox(frm, state="readonly", width=26, values=quad_labels)
        qm = {"SW": 0, "NW": 1, "SE": 2, "NE": 3}
        try:
            quad_cb.current(qm.get(str(self.quad_var.get()).strip().upper(), 0))
        except Exception:
            quad_cb.current(0)
        quad_cb.grid(row=br + 0, column=1, sticky="ew", pady=2)

        mat_vals = [f"MAT {i:02d}" for i in range(16)] + ["MAT ALL"]
        ttk.Label(frm, text="MAT").grid(row=br + 1, column=0, sticky="w", pady=2)
        mat_cb = ttk.Combobox(frm, state="readonly", width=14, values=mat_vals)
        mat_cb.current(0)
        mat_cb.grid(row=br + 1, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="PIX min").grid(row=br + 2, column=0, sticky="w", pady=2)
        pix_min_sb = tk.Spinbox(frm, from_=0, to=63, width=8)
        pix_min_sb.delete(0, "end")
        pix_min_sb.insert(0, "0")
        pix_min_sb.grid(row=br + 2, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="PIX max").grid(row=br + 3, column=0, sticky="w", pady=2)
        pix_max_sb = tk.Spinbox(frm, from_=0, to=63, width=8)
        pix_max_sb.delete(0, "end")
        pix_max_sb.insert(0, "63")
        pix_max_sb.grid(row=br + 3, column=1, sticky="w", pady=2)

        all_pix_var = tk.BooleanVar(value=False)

        def _toggle_pix_span(*_a: object) -> None:
            if all_pix_var.get():
                pix_min_sb.configure(state="disabled")
                pix_max_sb.configure(state="disabled")
            else:
                pix_min_sb.configure(state="normal")
                pix_max_sb.configure(state="normal")

        ttk.Checkbutton(frm, text="All PIX (0–63)", variable=all_pix_var, command=_toggle_pix_span).grid(
            row=br + 4, column=1, sticky="w", pady=2
        )

        ttk.Label(frm, text="LSB target (ps)").grid(row=br + 5, column=0, sticky="w", pady=2)
        res_sb = tk.Spinbox(frm, from_=10, to=100, width=8)
        res_sb.delete(0, "end")
        res_sb.insert(0, "30")
        res_sb.grid(row=br + 5, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="Calibration time (reg 0..3)").grid(row=br + 6, column=0, sticky="w", pady=2)
        cal_t_sb = tk.Spinbox(frm, from_=0, to=3, width=8)
        cal_t_sb.delete(0, "end")
        cal_t_sb.insert(0, "3")
        cal_t_sb.grid(row=br + 6, column=1, sticky="w", pady=2)

        de_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(frm, text="Double edge (DE)", variable=de_var).grid(row=br + 7, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="DCO-0 Adj (0..3)").grid(row=br + 8, column=0, sticky="w", pady=2)
        adj_sb = tk.Spinbox(frm, from_=0, to=3, width=8)
        adj_sb.delete(0, "end")
        adj_sb.insert(0, "1")
        adj_sb.grid(row=br + 8, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="DCO-0 Ctrl (0..15)").grid(row=br + 9, column=0, sticky="w", pady=2)
        ctrl_sb = tk.Spinbox(frm, from_=0, to=15, width=8)
        ctrl_sb.delete(0, "end")
        ctrl_sb.insert(0, "0")
        ctrl_sb.grid(row=br + 9, column=1, sticky="w", pady=2)

        cc_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(frm, text="Calibrate MAT 4–7 (broadcast)", variable=cc_var).grid(
            row=br + 10, column=1, sticky="w", pady=2
        )

        err_var = tk.StringVar(value="")

        def do_ok() -> None:
            err_var.set("")
            try:
                qi = int(quad_cb.current())
                mi = int(mat_cb.current())
                if all_pix_var.get():
                    pmi, pma = 0, 63
                else:
                    pmi = int(pix_min_sb.get())
                    pma = int(pix_max_sb.get())
                if pmi > pma:
                    raise ValueError("PIX min > max")
            except Exception as e:
                err_var.set(str(e))
                return

            if qi == 5:
                err_var.set("Select a quadrant other than BROADCAST.")
                return

            if self.offline:
                err_var.set("Hardware calibration cannot run in offline mode. Set OFFLINE=0 for a live setup.")
                self._set_status("Calib DCO: offline — no HW run")
                return

            path = filedialog.asksaveasfilename(
                parent=win,
                title="Save DCO calibration report",
                defaultextension=".txt",
                filetypes=[("Text", "*.txt"), ("All", "*.*")],
            )
            if not path:
                return
            # Touch the output file early so the user immediately sees something created,
            # and so failures before the final write are visible.
            try:
                from datetime import datetime

                with open(path, "w", encoding="utf-8") as _f:
                    _f.write(f"# Calib DCO started: {datetime.now().isoformat(timespec='seconds')}\n")
            except Exception as _e:
                err_var.set(f"Cannot write output file: {_e}")
                return
            try:
                params = CalibDCOParams(
                    quadrant_combo_index=qi,
                    mat_combo_index=mi,
                    pix_min=pmi,
                    pix_max=pma,
                    all_pix=bool(all_pix_var.get()),
                    resolution_target_ps=int(res_sb.get()),
                    calibration_time=int(cal_t_sb.get()),
                    double_edge=bool(de_var.get()),
                    single_adj=int(adj_sb.get()),
                    single_ctrl=int(ctrl_sb.get()),
                    calibrate_mat_4_7=bool(cc_var.get()),
                    output_path=path,
                )
            except Exception as e:
                err_var.set(str(e))
                return

            win.destroy()
            self._set_status("Calib DCO running…")

            def work() -> None:
                def prog(msg: str) -> None:
                    # Always mirror progress to stderr too (terminal survives status overwrites).
                    try:
                        import sys
                        sys.stderr.write(f"[CalibDCO] {msg}\n")
                        sys.stderr.flush()
                    except Exception:
                        pass
                    self.after(0, lambda m=msg: self._set_status(m))

                try:
                    prog(f"start output={path}")
                    loaded = self.hw.run_calib_dco(params, progress=prog)
                except Exception as e:
                    try:
                        import traceback, sys
                        traceback.print_exc(file=sys.stderr)
                    except Exception:
                        pass
                    self.after(0, lambda e=e: self._set_status(f"Calib DCO error: {e}"))
                    return
                # Cache per-pixel results for the FTDAC popup.
                try:
                    def _update_cache() -> None:
                        # Populate only non-zero entries (resolution_matrix is LSB).
                        for q_idx, q_str in enumerate(("SW", "NW", "SE", "NE")):
                            for m in range(16):
                                for pxi in range(64):
                                    lsb = float(getattr(loaded, "resolution_matrix")[q_idx][m][pxi])  # type: ignore[attr-defined]
                                    if lsb == 0.0:
                                        continue
                                    d0 = float(getattr(loaded, "cal_matrix")[q_idx][m][pxi][0])  # type: ignore[attr-defined]
                                    d1 = float(getattr(loaded, "cal_matrix")[q_idx][m][pxi][1])  # type: ignore[attr-defined]
                                    self._dco_calib_cache[(q_str, int(m), int(pxi))] = {
                                        "dco0_ps": float(d0),
                                        "dco1_ps": float(d1),
                                        "lsb_ps": float(lsb),
                                    }

                    self.after(0, _update_cache)
                except Exception:
                    pass
                self.after(0, lambda: self._set_status(f"Calib DCO completed → {path}"))

            threading.Thread(target=work, daemon=True).start()

        def do_cancel() -> None:
            win.destroy()

        bf = ttk.Frame(frm)
        bf.grid(row=br + 12, column=0, columnspan=2, pady=(12, 0))
        ttk.Button(bf, text="Start", command=do_ok).pack(side="left", padx=(0, 8))
        ttk.Button(bf, text="Cancel", command=do_cancel).pack(side="left")
        ttk.Label(frm, textvariable=err_var, foreground="#b00020").grid(row=br + 11, column=0, columnspan=2, sticky="w")

        frm.grid_columnconfigure(1, weight=1)

    def _with_hw(self, fn, *, busy: str) -> Optional[object]:
        if self.offline:
            self._set_status(f"{busy} (offline)")
            return None
        try:
            self._set_status(busy)
            with self._hw_seq_lock:
                return fn()
        except Exception as e:
            _dbg(f"HW call failed busy={busy} err={e!r}")
            self._set_status(f"Error: {e}")
            return None
        finally:
            self.update_idletasks()

    def _save_quadrant_full_config(self) -> None:
        """
        Save current quadrant configuration to a text file compatible with the legacy C# GUI
        (same format as MainForm.saveFullConfigurationToString()).
        """
        if self.offline:
            self._set_status("Save config: offline")
            return
        q = str(self.quad_var.get()).strip().upper()
        if q not in {"SW", "NW", "SE", "NE"}:
            self._set_status(f"Save config: bad quadrant {q!r}")
            return

        try:
            from datetime import datetime

            default_name = f"IGNITE64_config{q}_{datetime.now().strftime('%y.%m.%d.%H.%M.%S')}.txt"
        except Exception:
            default_name = f"IGNITE64_config{q}.txt"

        path = filedialog.asksaveasfilename(
            parent=self,
            title=f"Save full configuration (Q={q})",
            initialfile=default_name,
            defaultextension=".txt",
            filetypes=[("Text", "*.txt"), ("All", "*.*")],
        )
        if not path:
            return

        # Run in background to keep UI responsive (readback is long: TOP + 16×MAT + IOext).
        btn = getattr(self, "_btn_save_quad_cfg", None)
        if btn is not None:
            try:
                btn.configure(state="disabled")
            except Exception:
                pass

        def work() -> None:
            err: Optional[str] = None
            try:
                busy_msg = f"Save full configuration (Q={q})…"
                self.after(0, lambda: self._set_status(busy_msg))

                # Avoid blocking forever if another long sequence is running.
                if not self._hw_seq_lock.acquire(timeout=0.5):
                    err = "HW busy (try again in a moment)"
                    return
                try:
                    self.hw.snapshot_full_configuration(q, path=path)
                finally:
                    try:
                        self._hw_seq_lock.release()
                    except Exception:
                        pass
            except Exception as e:
                err = str(e)

            def done() -> None:
                if err:
                    self._set_status(f"Save config ERROR: {err}")
                else:
                    self._set_status(f"Saved full config (Q={q}) → {path}")
                if btn is not None:
                    try:
                        btn.configure(state="normal")
                    except Exception:
                        pass

            self.after(0, done)

        threading.Thread(target=work, daemon=True).start()

    def _open_dco_cal_map(self) -> None:
        """
        Mappa calibrazione DCO come nel C#: griglia 4×4 di mattonelle (16 MAT),
        ciascuna 8×8 pixel con valore LSB (ps) e colore per qualità LSB.
        Dati da lookup `self._dco_calib_cache` (popolata dopo "Calib DCO…").
        """
        win = tk.Toplevel(self)
        win.title("DCO calibration map — 16 MAT")
        win.transient(self)
        win.resizable(True, True)
        try:
            win.lift(self)
            win.focus_force()
            win.attributes("-topmost", True)
            win.after(150, lambda: win.attributes("-topmost", False))
        except Exception:
            try:
                win.lift()
            except Exception:
                pass

        root = ttk.Frame(win, padding=10)
        root.pack(fill="both", expand=True)

        q_var = tk.StringVar(value=str(self.quad_var.get()).strip().upper())

        top = ttk.Frame(root)
        top.pack(fill="x")
        ttk.Label(top, text="Quadrant:").pack(side="left")
        q_cb = ttk.Combobox(top, state="readonly", width=5, values=("SW", "NW", "SE", "NE"), textvariable=q_var)
        q_cb.pack(side="left", padx=(6, 12))

        stats_var = tk.StringVar(value="—")
        ttk.Label(root, textvariable=stats_var, wraplength=820).pack(anchor="w", pady=(6, 6))

        small_cell = 18
        gap_mat = 6
        block_px = 8 * small_cell
        pad = 10
        n_side = 4
        canvas_w = 2 * pad + n_side * block_px + (n_side - 1) * gap_mat
        canvas_h = canvas_w

        canvas = tk.Canvas(root, width=canvas_w, height=canvas_h, highlightthickness=0, bg="#f4f4f4")
        canvas.pack()

        def _cell_color(lsb_ps: float) -> str:
            if lsb_ps <= 0:
                return "#e8e8e8"
            if 26.0 <= lsb_ps <= 34.0:
                return "#b7e1cd"
            if 22.0 <= lsb_ps <= 40.0:
                return "#fff2cc"
            return "#f4c7c3"

        def _render() -> None:
            canvas.delete("all")
            q = str(q_var.get()).strip().upper()
            tot_cal = 0
            for mat in range(16):
                mr, mc = mat // 4, mat % 4
                ox = pad + mc * (block_px + gap_mat)
                oy = pad + mr * (block_px + gap_mat)
                canvas.create_rectangle(
                    ox - 1, oy - 1, ox + block_px + 1, oy + block_px + 1, outline="#777777", width=1
                )
                canvas.create_text(
                    ox + block_px - 4,
                    oy + 6,
                    text=str(mat),
                    anchor="ne",
                    font=("Segoe UI", 9, "bold"),
                    fill="#222222",
                )

                for pix in range(64):
                    pr, pc = pix // 8, pix % 8
                    d = self._dco_calib_cache.get((q, mat, pix))
                    lsb = float(d.get("lsb_ps", 0.0)) if isinstance(d, dict) else 0.0
                    if lsb > 0.0:
                        tot_cal += 1
                    x0 = ox + pc * small_cell
                    y0 = oy + pr * small_cell
                    x1 = x0 + small_cell - 1
                    y1 = y0 + small_cell - 1
                    canvas.create_rectangle(x0, y0, x1, y1, fill=_cell_color(lsb), outline="#bbbbbb", width=1)
                    if lsb > 0.0:
                        canvas.create_text(
                            (x0 + x1) / 2,
                            (y0 + y1) / 2,
                            text=f"{lsb:.0f}",
                            font=("Segoe UI", 7),
                        )

            stats_var.set(
                f"Q={q}: calibrated pixels {tot_cal}/1024 (16×64). "
                f"Color: green ≈26–34 ps (target ~30), yellow 22–40 ps, red otherwise; gray = not calibrated. "
                f"Click a pixel to open the FTDAC popup (MAT 4–7: only if enabled)."
            )

        def _on_click(ev: tk.Event) -> None:
            x = int(ev.x) - pad
            y = int(ev.y) - pad
            block = block_px + gap_mat
            if x < 0 or y < 0:
                return
            mc = x // block
            xr = x - mc * block
            mr = y // block
            yr = y - mr * block
            if mc < 0 or mc > 3 or mr < 0 or mr > 3:
                return
            if xr >= block_px or yr >= block_px:
                return
            mat = mr * 4 + mc
            pc = int(xr // small_cell)
            pr = int(yr // small_cell)
            if pc < 0 or pc > 7 or pr < 0 or pr > 7:
                return
            pix = pr * 8 + pc
            try:
                self.quad_var.set(str(q_var.get()).strip().upper())
            except Exception:
                pass
            self._open_ftdac_popup(mat, pix)

        canvas.bind("<Button-1>", _on_click)
        q_cb.bind("<<ComboboxSelected>>", lambda _e: _render())
        _render()

    def _capture_mat_snapshot(self, quad: str) -> int:
        """
        Lettura pre-caricata MAT (pix_on + ftdac) per una singola quadrant.
        Serve solo per mostrare subito lo stato corretto in UI dopo `hw.start_config`.
        """
        if self.offline:
            return 0
        # Snapshot "da hardware" invalida quello "da file".
        self._mat_snapshot_prefill_from_file = False
        q = str(quad).strip().upper()
        if q not in self._mat_snapshot_cache:
            return 0
        t0 = time.perf_counter()
        # Reset cache per evitare dati vecchi.
        self._mat_snapshot_cache[q] = {}
        cached_mats = 0

        # Configure HW state once to avoid per-MAT overhead.
        # readMatPixelsAndFTDAC() can skip selection/topreadout when this is already done.
        try:
            self.hw.select_quadrant(q)
        except Exception:
            pass
        try:
            self.hw.TopReadout("i2c")
        except Exception:
            pass
        for mat_id in range(16):
            # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
            if 4 <= int(mat_id) <= 7:
                continue
            try:
                r = self.hw.readMatPixelsAndFTDAC(
                    q,
                    mattonella=mat_id,
                    _skip_select_quadrant=True,
                    _skip_topreadout=True,
                )
                if not isinstance(r, dict):
                    continue
                pix_on = r.get("pix_on")
                ftdac = r.get("ftdac")
                if (
                    isinstance(pix_on, list)
                    and isinstance(ftdac, list)
                    and len(pix_on) == 64
                    and len(ftdac) == 64
                ):
                    self._mat_snapshot_cache[q][mat_id] = {"pix_on": pix_on, "ftdac": ftdac}
                    cached_mats += 1
            except Exception:
                # Best-effort: se una singola MAT fallisce, la UI la rileggerà al bisogno.
                continue

        dt = time.perf_counter() - t0
        _dbg(
            f"snapshot created (quando ri scarichi la mappa di tutti i registri nuovamente) (quad={q}) cached_mats={cached_mats}/16 in {dt:.3f}s"
        )
        return cached_mats

    def _prefill_mat_snapshot_from_full_configuration_file(self, full_cfg_path: Path, quad: str) -> int:
        """
        Prefill della cache MAT/pix_on/ftdac *senza* leggere l'intero chip.
        Serve a far sì che la GUI sia coerente con il file di configurazione C#.
        """
        if self.offline:
            return 0

        from .config import parse_full_configuration

        q = str(quad).strip().upper()
        if q not in self._mat_snapshot_cache:
            return 0

        cfg = parse_full_configuration(full_cfg_path)

        # Cache reset
        self._mat_snapshot_cache[q] = {}

        cached_mats = 0
        for mat_id in range(16):
            data = cfg.mats.get(mat_id)
            if not isinstance(data, list) or len(data) < 108:
                continue

            # MAT regs 0..63: pix regs; bit6 == PIXON
            pix_bytes = [int(x) & 0xFF for x in data[0:64]]
            pix_on = [((b & 0x40) != 0) for b in pix_bytes]

            # MAT regs FT_BASE=76..107: 32 bytes, 2 pixel per byte => 64 nibbles
            ft_bytes = [int(x) & 0xFF for x in data[76:108]]
            ftdac: list[int] = [0] * 64
            for i, bb in enumerate(ft_bytes):
                ftdac[2 * i] = bb & 0x0F
                ftdac[2 * i + 1] = (bb >> 4) & 0x0F

            self._mat_snapshot_cache[q][mat_id] = {"pix_on": pix_on, "ftdac": ftdac}
            cached_mats += 1

        return cached_mats

    def _fifo_decode_word(self, w: int) -> dict[str, int]:
        """
        Decode 64-bit FIFO word for I2C readout (see datasheet section 7.3/7.4).
        We focus on the fields that are useful for monitoring like the C# GUI:
          - mattonella id (bits 46..43)
          - pixel id (pixline bits 42..40, pix bits 39..37) -> 0..63
          - fifo_empty flag (bit 47) + fifo_count (bits 55..48) for I2C readout
        """
        w = int(w) & ((1 << 64) - 1)
        mat = int((w >> 43) & 0x0F)
        pixline = int((w >> 40) & 0x07)  # 0..7
        pix = int((w >> 37) & 0x07)  # 0..7
        channel = int(pixline * 8 + pix)
        fifo_empty = int((w >> 47) & 0x01)
        fifo_cnt = int((w >> 48) & 0xFF)
        fifo_full = int((w >> 56) & 0x01)
        fifo_halffull = int((w >> 57) & 0x01)
        return {
            "mat": mat,
            "channel": channel,
            "fifo_empty": fifo_empty,
            "fifo_cnt": fifo_cnt,
            "fifo_full": fifo_full,
            "fifo_halffull": fifo_halffull,
        }

    def _fifo_decode_word_tdc(self, w: int, *, quad: str) -> dict[str, object]:
        """
        Extended FIFO decode for DCO/TDC calibration / measurement words (C# compatible fields),
        with optional TA/TOT computation when DCO periods are known.
        """
        base = self._fifo_decode_word(int(w))
        fields = raw_fifo_to_fields(int(w))
        mat = int(fields.get("mat", base.get("mat", 0)))  # type: ignore[arg-type]
        pix = int(fields.get("pix", base.get("channel", 0)))  # type: ignore[arg-type]
        cal_mode = int(fields.get("cal_mode", 0))  # type: ignore[arg-type]

        def _as_int(x: object) -> Optional[int]:
            try:
                if x is None or x == "-":
                    return None
                return int(x)  # type: ignore[arg-type]
            except Exception:
                return None

        dco = _as_int(fields.get("dco_field"))
        de = _as_int(fields.get("de_field"))
        cal_time = _as_int(fields.get("cal_time_field"))
        cnt_tot = _as_int(fields.get("cnt_tot"))
        counts_0 = _as_int(fields.get("counts_0"))
        counts_1 = _as_int(fields.get("counts_1"))

        out: dict[str, object] = {
            **base,
            "pix": pix,
            "cal_mode": cal_mode,
            "dco": dco,
            "de": de,
            "cal_time": cal_time,
            "cnt_tot": cnt_tot,
            "counts_0": counts_0,
            "counts_1": counts_1,
        }

        # If calibration periods are known for this (quad, mat, pix), compute TA/TOT like C#.
        k = (str(quad).strip().upper(), int(mat), int(pix))
        calib = self._dco_calib_cache.get(k)
        if isinstance(calib, dict):
            try:
                dco0_ps = float(calib.get("dco0_ps", 0.0))
                dco1_ps = float(calib.get("dco1_ps", 0.0))
            except Exception:
                dco0_ps, dco1_ps = 0.0, 0.0
            if dco0_ps > 0.0 and dco1_ps > 0.0:
                out["dco0_ps"] = dco0_ps
                out["dco1_ps"] = dco1_ps
                out["lsb_ps"] = float(dco0_ps) - float(dco1_ps)
                # TA/TOT are meaningful for measurement words (CAL_Mode == 0) where counts_0/1 exist.
                if cal_mode == 0 and counts_0 is not None and counts_1 is not None and cnt_tot is not None:
                    try:
                        ta_ps = (float(int(counts_0) - 1) * dco0_ps) - (float(int(counts_1) - 2) * dco1_ps)
                        tot_ps = float(int(cnt_tot) - 1) * dco0_ps
                        out["ta_ps"] = ta_ps
                        out["tot_ps"] = tot_ps
                    except Exception:
                        pass

        return out

    def _open_fifo_analyze_popup(self, quad: str, samples: list[dict[str, object]]) -> None:
        """
        Analyze FIFO decoded samples:
        - filter by MAT/CH
        - TA histogram (with std dev)
        - TOT histogram
        - TA vs TOT scatter (time-walk view)
        """
        q = str(quad).strip().upper()
        win = tk.Toplevel(self)
        win.title(f"Analyze FIFO data — Q={q}")
        win.transient(self)
        win.resizable(True, True)

        root = ttk.Frame(win, padding=10)
        root.pack(fill="both", expand=True)
        root.columnconfigure(0, weight=1)
        root.rowconfigure(1, weight=1)

        # Use the live list provided by the FIFO window (do NOT copy),
        # so "Clear data" really clears what you will see next time.
        data: list[dict[str, object]] = samples if isinstance(samples, list) else []

        # Controls
        ctrl = ttk.Frame(root)
        ctrl.grid(row=0, column=0, sticky="ew")
        ctrl.columnconfigure(10, weight=1)

        use_filter = tk.BooleanVar(value=True)
        mat_var = tk.StringVar(value="0")
        ch_var = tk.StringVar(value="0")

        ttk.Checkbutton(ctrl, text="Filter MAT/CH", variable=use_filter).grid(row=0, column=0, sticky="w", padx=(0, 10))
        ttk.Label(ctrl, text="MAT").grid(row=0, column=1, sticky="w")
        ttk.Spinbox(ctrl, from_=0, to=15, width=5, textvariable=mat_var).grid(row=0, column=2, sticky="w", padx=(6, 12))
        ttk.Label(ctrl, text="CH").grid(row=0, column=3, sticky="w")
        ttk.Spinbox(ctrl, from_=0, to=63, width=5, textvariable=ch_var).grid(row=0, column=4, sticky="w", padx=(6, 12))

        stats_var = tk.StringVar(value="—")
        ttk.Label(ctrl, textvariable=stats_var, font=("Segoe UI", 9)).grid(row=0, column=5, sticky="w")

        # Plots
        nb = ttk.Notebook(root)
        nb.grid(row=1, column=0, sticky="nsew", pady=(10, 0))

        tab_ta = ttk.Frame(nb, padding=8)
        tab_tot = ttk.Frame(nb, padding=8)
        tab_sc = ttk.Frame(nb, padding=8)
        nb.add(tab_ta, text="TA distribution")
        nb.add(tab_tot, text="TOT distribution")
        nb.add(tab_sc, text="TA vs TOT")

        cv_ta = tk.Canvas(tab_ta, bg="white", highlightthickness=1, highlightbackground="#cfd8dc", height=320)
        cv_tot = tk.Canvas(tab_tot, bg="white", highlightthickness=1, highlightbackground="#cfd8dc", height=320)
        cv_sc = tk.Canvas(tab_sc, bg="white", highlightthickness=1, highlightbackground="#cfd8dc", height=320)
        cv_ta.pack(fill="both", expand=True)
        cv_tot.pack(fill="both", expand=True)
        cv_sc.pack(fill="both", expand=True)

        def _safe_float(x: object) -> Optional[float]:
            try:
                if x is None:
                    return None
                return float(x)  # type: ignore[arg-type]
            except Exception:
                return None

        def _d_mat(d: dict[str, object]) -> Optional[int]:
            try:
                v = d.get("mat", None)
                if v is None:
                    return None
                return int(v)
            except Exception:
                return None

        def _d_ch(d: dict[str, object]) -> Optional[int]:
            # Backward compatible: accept both "channel" and legacy "ch" keys.
            try:
                if "channel" in d:
                    v = d.get("channel", None)
                    if v is None:
                        return None
                    return int(v)
            except Exception:
                pass
            try:
                if "ch" in d:
                    v = d.get("ch", None)
                    if v is None:
                        return None
                    return int(v)
            except Exception:
                pass
            try:
                # FIFO decoder uses "pix" for the pixel/channel id (0..63).
                if "pix" in d:
                    v = d.get("pix", None)
                    if v is None:
                        return None
                    return int(v)
            except Exception:
                pass
            return None

        def _filter_samples() -> tuple[int, list[float], list[float]]:
            try:
                want_mat = int(str(mat_var.get()).strip())
            except Exception:
                want_mat = 0
            try:
                want_ch = int(str(ch_var.get()).strip())
            except Exception:
                want_ch = 0
            want_mat = max(0, min(15, want_mat))
            want_ch = max(0, min(63, want_ch))

            matched = 0
            tas: list[float] = []
            tots: list[float] = []
            for d in data:
                try:
                    if bool(use_filter.get()):
                        dm = _d_mat(d)
                        dc = _d_ch(d)
                        if dm is None or dc is None:
                            continue
                        if int(dm) != want_mat:
                            continue
                        if int(dc) != want_ch:
                            continue
                    matched += 1
                    ta = _safe_float(d.get("ta_ps"))
                    tot = _safe_float(d.get("tot_ps"))
                    if ta is not None:
                        tas.append(float(ta))
                    if tot is not None:
                        tots.append(float(tot))
                except Exception:
                    continue
            return matched, tas, tots

        def _draw_hist(
            cv: tk.Canvas, values: list[float], *, title: str, matched: int, bins: int = 60
        ) -> None:
            cv.delete("all")
            w = int(cv.winfo_width() or 800)
            h = int(cv.winfo_height() or 320)
            pad_l, pad_r, pad_t, pad_b = 50, 14, 24, 34
            cv.create_text(pad_l, 12, text=title, anchor="w", fill="#263238", font=("Segoe UI", 10, "bold"))
            if not values:
                if matched > 0:
                    cv.create_text(
                        w // 2,
                        h // 2 - 10,
                        text=f"Matched samples: {matched} (MAT/CH filter ok)",
                        fill="#455a64",
                        font=("Segoe UI", 10, "bold"),
                    )
                    cv.create_text(
                        w // 2,
                        h // 2 + 12,
                        text="But TA/TOT are missing for these words (no DCO calib for that pixel, or not CAL_Mode==0).",
                        fill="#607d8b",
                    )
                else:
                    cv.create_text(w // 2, h // 2, text="No samples matched this MAT/CH.", fill="#607d8b")
                return
            vmin = min(values)
            vmax = max(values)
            if not (vmax > vmin):
                vmax = vmin + 1.0
            bins = max(10, int(bins))
            step = (vmax - vmin) / float(bins)
            counts = [0] * bins
            for v in values:
                idx = int((v - vmin) / step)
                if idx < 0:
                    idx = 0
                if idx >= bins:
                    idx = bins - 1
                counts[idx] += 1
            cmax = max(counts) if counts else 1
            # Axes
            x0, y0 = pad_l, pad_t
            x1, y1 = w - pad_r, h - pad_b
            cv.create_line(x0, y1, x1, y1, fill="#90a4ae")
            cv.create_line(x0, y0, x0, y1, fill="#90a4ae")
            # Bars
            bw = max(1.0, (x1 - x0) / float(bins))
            for i, c in enumerate(counts):
                if c <= 0:
                    continue
                x_left = x0 + i * bw
                x_right = x_left + bw * 0.95
                y_top = y1 - (float(c) / float(cmax)) * (y1 - y0)
                cv.create_rectangle(x_left, y_top, x_right, y1, fill="#1976d2", outline="")
            # Labels
            cv.create_text(x0, y1 + 18, text=f"{vmin:.1f}", anchor="w", fill="#455a64", font=("Segoe UI", 9))
            cv.create_text(x1, y1 + 18, text=f"{vmax:.1f}", anchor="e", fill="#455a64", font=("Segoe UI", 9))
            cv.create_text(x0 - 6, y0, text=str(cmax), anchor="ne", fill="#455a64", font=("Segoe UI", 9))

        def _draw_scatter(cv: tk.Canvas, xs: list[float], ys: list[float], *, title: str) -> None:
            cv.delete("all")
            w = int(cv.winfo_width() or 800)
            h = int(cv.winfo_height() or 320)
            pad_l, pad_r, pad_t, pad_b = 50, 14, 24, 34
            cv.create_text(pad_l, 12, text=title, anchor="w", fill="#263238", font=("Segoe UI", 10, "bold"))
            if not xs or not ys:
                cv.create_text(w // 2, h // 2, text="No paired TA/TOT samples.", fill="#607d8b")
                return
            n = min(len(xs), len(ys))
            pts = list(zip(xs[:n], ys[:n]))
            # Subsample for speed
            if len(pts) > 4000:
                step = max(1, len(pts) // 4000)
                pts = pts[::step]
            xmin = min(p[0] for p in pts)
            xmax = max(p[0] for p in pts)
            ymin = min(p[1] for p in pts)
            ymax = max(p[1] for p in pts)
            if not (xmax > xmin):
                xmax = xmin + 1.0
            if not (ymax > ymin):
                ymax = ymin + 1.0
            x0, y0 = pad_l, pad_t
            x1, y1 = w - pad_r, h - pad_b
            cv.create_line(x0, y1, x1, y1, fill="#90a4ae")
            cv.create_line(x0, y0, x0, y1, fill="#90a4ae")
            for x, y in pts:
                px = x0 + (float(x - xmin) / float(xmax - xmin)) * (x1 - x0)
                py = y1 - (float(y - ymin) / float(ymax - ymin)) * (y1 - y0)
                cv.create_oval(px - 1, py - 1, px + 1, py + 1, fill="#c62828", outline="")
            cv.create_text(x0, y1 + 18, text=f"TA {xmin:.0f}..{xmax:.0f} ps", anchor="w", fill="#455a64", font=("Segoe UI", 9))
            cv.create_text(x1, y1 + 18, text=f"TOT {ymin:.0f}..{ymax:.0f} ps", anchor="e", fill="#455a64", font=("Segoe UI", 9))

        def _refresh() -> None:
            matched, tas, tots = _filter_samples()
            # Pair for scatter
            pairs_ta: list[float] = []
            pairs_tot: list[float] = []
            try:
                want_mat = int(str(mat_var.get()).strip())
            except Exception:
                want_mat = 0
            try:
                want_ch = int(str(ch_var.get()).strip())
            except Exception:
                want_ch = 0
            want_mat = max(0, min(15, want_mat))
            want_ch = max(0, min(63, want_ch))
            for d in data:
                try:
                    if bool(use_filter.get()):
                        dm = _d_mat(d)
                        dc = _d_ch(d)
                        if dm is None or dc is None:
                            continue
                        if int(dm) != want_mat:
                            continue
                        if int(dc) != want_ch:
                            continue
                    ta = _safe_float(d.get("ta_ps"))
                    tot = _safe_float(d.get("tot_ps"))
                    if ta is not None and tot is not None:
                        pairs_ta.append(float(ta))
                        pairs_tot.append(float(tot))
                except Exception:
                    continue

            # Std dev (TA only)
            sigma = None
            if len(tas) >= 2:
                try:
                    import statistics

                    sigma = float(statistics.pstdev(tas))
                except Exception:
                    sigma = None
            if bool(use_filter.get()):
                stats_var.set(
                    f"matched={matched}  samples: TA={len(tas)} TOT={len(tots)} paired={len(pairs_ta)}  |  "
                    f"MAT={want_mat} CH={want_ch}  |  σ(TA)={sigma:.2f} ps" if sigma is not None else
                    f"matched={matched}  samples: TA={len(tas)} TOT={len(tots)} paired={len(pairs_ta)}  |  MAT={want_mat} CH={want_ch}"
                )
            else:
                stats_var.set(
                    f"matched={matched}  samples: TA={len(tas)} TOT={len(tots)} paired={len(pairs_ta)}  |  σ(TA)={sigma:.2f} ps"
                    if sigma is not None
                    else f"matched={matched}  samples: TA={len(tas)} TOT={len(tots)} paired={len(pairs_ta)}"
                )

            _draw_hist(cv_ta, tas, title="TA distribution (ps)", matched=matched)
            _draw_hist(cv_tot, tots, title="TOT distribution (ps)", matched=matched)
            _draw_scatter(cv_sc, pairs_ta, pairs_tot, title="TA vs TOT (ps)")

        ttk.Button(ctrl, text="Analyze / refresh", command=_refresh).grid(row=0, column=6, sticky="w", padx=(12, 0))

        def _clear_data() -> None:
            data.clear()
            # Keep controls but reset plots/stats.
            stats_var.set("cleared")
            try:
                cv_ta.delete("all")
                cv_tot.delete("all")
                cv_sc.delete("all")
            except Exception:
                pass
            _refresh()

        ttk.Button(ctrl, text="Clear data", command=_clear_data).grid(row=0, column=7, sticky="w", padx=(8, 0))

        # Auto-refresh on resize and on filter changes.
        cv_ta.bind("<Configure>", lambda _e: _refresh())
        cv_tot.bind("<Configure>", lambda _e: _refresh())
        cv_sc.bind("<Configure>", lambda _e: _refresh())
        use_filter.trace_add("write", lambda *_a: _refresh())
        mat_var.trace_add("write", lambda *_a: _refresh())
        ch_var.trace_add("write", lambda *_a: _refresh())

        self.after(50, _refresh)

    def _fifo_ensure_i2c(self) -> None:
        # FIFO readout over I2C requires TOP readout interface set to i2c.
        try:
            self.hw.TopReadout("i2c")
        except Exception:
            pass

    def _fifo_log(self, text_widget: tk.Text, msg: str) -> None:
        text_widget.configure(state="normal")
        text_widget.insert("end", msg + "\n")
        text_widget.see("end")
        text_widget.configure(state="disabled")

    def _fifo_auto_stop(self) -> None:
        if self._fifo_auto_after_id is not None:
            try:
                self.after_cancel(self._fifo_auto_after_id)
            except Exception:
                pass
            self._fifo_auto_after_id = None

    def _check_calibration(self, quad: str) -> None:
        """
        Quadrant-level post-calibration helper:
        - read/drain FIFO and identify channels (MAT, CH) still counting
        - increment FTCODE by +1 only for those channels
        - repeat until FIFO is quiet or safety limits reached

        Note: MAT 4..7 cannot be addressed directly; they will be reported but not adjusted here.
        """
        if self.offline:
            self._set_status("Check Calibration: offline")
            return

        q = str(quad).strip().upper()

        win = tk.Toplevel(self)
        win.title(f"Check Calibration — Q={q}")
        win.transient(self)
        win.resizable(True, True)

        root = ttk.Frame(win, padding=10)
        root.pack(fill="both", expand=True)

        status_var = tk.StringVar(value="—")
        ttk.Label(root, textvariable=status_var, font=("Segoe UI", 10, "bold")).pack(anchor="w", pady=(0, 6))

        stats_var = tk.StringVar(value="")
        ttk.Label(root, textvariable=stats_var, font=("Segoe UI", 9)).pack(anchor="w", pady=(0, 6))

        out = tk.Text(root, height=18, width=92, wrap="none", state="disabled")
        yscroll = ttk.Scrollbar(root, orient="vertical", command=out.yview)
        out.configure(yscrollcommand=yscroll.set)
        out.pack(side="left", fill="both", expand=True)
        yscroll.pack(side="right", fill="y")

        def log(msg: str) -> None:
            try:
                self._fifo_log(out, msg)
            except Exception:
                pass

        last_active: set[tuple[int, int]] = set()
        last_lock = threading.Lock()
        adjusted_lock = threading.Lock()
        adjusted_unique: set[tuple[int, int]] = set()

        def turn_off_offending() -> None:
            """
            Turn OFF channels that were last seen counting in FIFO.
            We can only disable per-channel PIXON/FEON; TDCON is MAT-level.
            """
            if self.offline:
                return

            def work_off() -> None:
                try:
                    with last_lock:
                        offenders = sorted(list(last_active))
                    if not offenders:
                        self.after(0, lambda: log("No offenders captured yet (run at least 1 iteration)."))
                        return
                    self.after(0, lambda n=len(offenders): log(f"Turn OFF: starting (n={n})"))
                    self.hw.select_quadrant(q)
                    # For reliability after WDU-like glitches.
                    try:
                        self._fifo_ensure_i2c()
                    except Exception:
                        pass
                    off_done = 0
                    off_skip = 0
                    off_err = 0
                    # Disable TDCON for MATs that have offenders (MAT-level).
                    mats_off: set[int] = set()
                    for mat_id, ch in offenders:
                        if 4 <= int(mat_id) <= 7:
                            off_skip += 1
                            continue
                        mats_off.add(int(mat_id))
                        # Best-effort: disable PIXON and FEON for that channel.
                        try:
                            self.hw.AnalogChannelOFF(q, mattonella=int(mat_id), canale=int(ch))
                        except Exception:
                            off_err += 1
                        try:
                            self.hw.setAnalogFEON(q, mattonella=int(mat_id), canale=int(ch), on=False)
                        except Exception:
                            off_err += 1
                        off_done += 1
                        time.sleep(0.002)
                    for mid in sorted(mats_off):
                        try:
                            self.hw.EnableTDC(q, Mattonella=int(mid), enable=False)
                        except Exception:
                            off_err += 1
                    self.after(
                        0,
                        lambda: log(
                            f"Turn OFF: done={off_done} mats_tdcoff={len(mats_off)} skipped47={off_skip} errs={off_err}. "
                            "Tip: re-run Check Calibration to confirm FIFO quiet."
                        ),
                    )
                except Exception as e:
                    self.after(0, lambda e=e: log(f"Turn OFF error: {e!r}"))

            threading.Thread(target=work_off, daemon=True).start()

        btns = ttk.Frame(root)
        btns.pack(fill="x", pady=(6, 0))
        ttk.Button(btns, text="Turn OFF offending channels", command=turn_off_offending).pack(side="left")
        ttk.Button(btns, text="Close", command=win.destroy).pack(side="right")

        def work() -> None:
            try:
                self.after(0, lambda: status_var.set(f"Running… (Q={q})"))
                self.hw.select_quadrant(q)
                self._fifo_ensure_i2c()
                max_iters = 20
                sample_batches = 8  # each batch reads 24 words (burst)
                max_words_per_batch = 24
                total_all = 16 * 64
                total_adjustable = (16 - 4) * 64  # exclude MAT 4..7 (cannot adjust directly)

                for it in range(max_iters):
                    # Sample FIFO even if it's continuously refilling (drain may never become empty).
                    words: list[int] = []
                    for _ in range(sample_batches):
                        try:
                            words.extend(list(self.hw.FifoReadNumWords(max_words_per_batch)))
                        except Exception:
                            # fall back to single reads (robust)
                            try:
                                words.append(int(self.hw.FifoReadSingleRobust(quad=q, retries=6, backoff_s=0.003, do_bus_recovery=True)))
                            except Exception:
                                pass
                        time.sleep(0.003)

                    active: set[tuple[int, int]] = set()
                    for w in words:
                        if int(w) == 0:
                            continue
                        try:
                            d = self._fifo_decode_word(int(w))
                            if int(d.get("fifo_empty", 0)) == 1 and int(d.get("fifo_cnt", 0)) == 0:
                                continue
                            active.add((int(d["mat"]), int(d["channel"])))
                        except Exception:
                            continue

                    if not active:
                        self.after(0, lambda: status_var.set(f"OK: FIFO quiet (Q={q})"))
                        log("OK: FIFO quiet")
                        return
                    # Update shared "last_active" in-place so the Turn OFF button can see it.
                    with last_lock:
                        last_active.clear()
                        last_active.update(active)

                    # Adjust only readable MATs; report 4..7.
                    skipped_47 = sorted([a for a in active if 4 <= int(a[0]) <= 7])
                    todo = sorted([a for a in active if not (4 <= int(a[0]) <= 7)])

                    noisy_total = len(active)
                    noisy_pct = (100.0 * float(noisy_total) / float(total_all)) if total_all else 0.0

                    if skipped_47:
                        _dbg(
                            f"Check Calibration: hits on MAT 4..7 cannot be adjusted directly: "
                            f"{skipped_47[:8]}{'...' if len(skipped_47)>8 else ''}"
                        )
                        log(
                            f"WARN: hits on MAT 4..7 (cannot adjust per-channel): "
                            f"{skipped_47[:6]}{'...' if len(skipped_47)>6 else ''}"
                        )

                    changed = 0
                    at_max = 0
                    for mat_id, ch in todo:
                        try:
                            cur = int(self.hw.readAnalogChannelFineTune(q, mattonella=int(mat_id), canale=int(ch)))
                        except Exception:
                            cur = 15
                        if cur >= 15:
                            at_max += 1
                            continue
                        new = int(cur) + 1
                        try:
                            self.hw.AnalogChannelFineTune(q, block=0, mattonella=int(mat_id), canale=int(ch), valore=int(new))
                            changed += 1
                            with adjusted_lock:
                                adjusted_unique.add((int(mat_id), int(ch)))
                        except Exception as e:
                            _dbg(f"Check Calibration: set FTCODE failed MAT={mat_id} CH={ch} err={e!r}")
                            try:
                                self.hw.i2c_bus_recovery()
                            except Exception:
                                pass
                            continue
                        # Small delay helps DLL stability
                        time.sleep(0.005)

                    self.after(
                        0,
                        lambda it=it, changed=changed, active=active, at_max=at_max: status_var.set(
                            f"iter {it+1}/{max_iters}: active={len(active)} adjusted={changed} at_max15={at_max}"
                        ),
                    )
                    with adjusted_lock:
                        adj_u = int(len(adjusted_unique))
                    adj_pct = (100.0 * float(adj_u) / float(total_adjustable)) if total_adjustable else 0.0
                    self.after(
                        0,
                        lambda noisy_total=noisy_total, noisy_pct=noisy_pct, adj_u=adj_u, adj_pct=adj_pct: stats_var.set(
                            f"Noisy channels: {noisy_total}/{total_all} ({noisy_pct:.2f}%)   "
                            f"Adjusted channels (this run): {adj_u}/{total_adjustable} ({adj_pct:.2f}%)"
                        ),
                    )
                    log(
                        f"iter {it+1}: active={len(active)} adjusted={changed} at_max15={at_max} "
                        f"(sampled_words={len(words)})"
                    )
                    # Print a short list of noisy channels (first N) for quick visibility.
                    try:
                        preview = sorted(list(active))[:20]
                        log(f"noisy preview: {preview}{' ...' if len(active) > 20 else ''}")
                    except Exception:
                        pass
                    # If nothing changed, we cannot make further progress.
                    if changed == 0:
                        self.after(0, lambda: status_var.set(f"STOP: no further increments possible (active={len(active)})"))
                        if at_max > 0:
                            log(
                                "STOP: channels still counting but FTCODE already at 15 for some/all. "
                                "Need to raise VTHR_H/L or disable offending channels."
                            )
                        else:
                            log("STOP: no increments applied (all channels already at max or unreachable).")
                        return
                    time.sleep(0.02)

                self.after(0, lambda: status_var.set("STOP: max iters reached"))
                log("STOP: max iters reached")
            except Exception as e:
                self.after(0, lambda e=e: status_var.set(f"ERROR: {e}"))
                log(f"ERROR: {e!r}")

        threading.Thread(target=work, daemon=True).start()

    def _adc_channel_for_dac(self, dac: str) -> Optional[int]:
        """
        Map internal DAC name to Quad ADC channel when a direct measurement exists.
        Datasheet/API mapping (api._adc_quad_oneshot):
          0 Vthr_H, 1 Vthr_L, 2 Vinj_H, 3 Vref_L, 4 Vfeed, 5 Vref, 6 V_Icap, 7 V_Iref
        """
        d = dac.strip().upper()
        if d == "VTHR_H":
            return 0
        if d == "VTHR_L":
            return 1
        if d == "VINJ_H":
            return 2
        # C# convention used in our GUI:
        # - "VLDO" measurement is Vref_L (ADC channel 3)
        # - "VFB"/"VF" measurement is Vfeed (ADC channel 4)
        if d == "VLDO":
            return 3
        if d in {"VFB", "VF"}:
            return 4
        # No direct ADC mapping currently exposed for these in our API: VINJ_L
        return None

    # -----
    # Quadrants die-view monitoring (per-quadrant blocks in letterboxing gray, not on the photo)
    # -----
    def _cancel_quadrants_mon_job(self) -> None:
        jid = getattr(self, "_quadrants_mon_job", None)
        if jid is not None:
            try:
                self.after_cancel(jid)
            except Exception:
                pass
            self._quadrants_mon_job = None

    def _trigger_die_canvas_refresh(self) -> None:
        fn = getattr(self, "_die_canvas_redraw", None)
        if callable(fn):
            try:
                fn()
            except Exception:
                pass
        self._refresh_home_quad_monitor_texts()

    def _refresh_home_quad_monitor_texts(self) -> None:
        d = getattr(self, "_home_quad_mon_texts", None)
        if not isinstance(d, dict):
            return
        for q in ("NW", "NE", "SW", "SE"):
            tx = d.get(q)
            if tx is None:
                continue
            body = (self._quad_monitor_canvas.get(q) or "").strip()
            try:
                tx.configure(state="normal")
                tx.delete("1.0", "end")
                tx.insert("end", body if body else "—")
                tx.configure(state="disabled")
            except tk.TclError:
                pass

    def _gather_quad_stats(self, quad: str) -> dict[str, int]:
        """Per-quadrant counts (full MAT×pixel scan — runs in a worker thread)."""
        pixon = 0
        feon = 0
        for mat in range(16):
            # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
            if 4 <= int(mat) <= 7:
                continue
            for ch in range(64):
                if self.hw.readAnalogChannelON(quad, mattonella=mat, canale=ch):
                    pixon += 1
                if self.hw.readAnalogFEON(quad, mattonella=mat, canale=ch):
                    feon += 1
        tdc_mats = 0
        for mat in range(16):
            if 4 <= int(mat) <= 7:
                continue
            if self.hw.readEnableTDC(quad, Mattonella=mat)["tdc_on"]:
                tdc_mats += 1
        return {"pixon": pixon, "feon": feon, "tdc_mats": tdc_mats}

    def _format_quad_monitor_block(
        self,
        quad: str,
        st: dict[str, int],
        *,
        power_ok: Optional[bool],
        power_err: Optional[str],
    ) -> str:
        """Testo per un quadrante: FEON, PIXON, TDC (Analog Power globale: solo bottone in home)."""
        _ = (power_ok, power_err)  # API invariata; stato power non duplicato qui
        n_pix = 1024
        n_mat = 16
        # Legacy keys (pre split FEON vs PIXON); default 0 if absent.
        p = int(st.get("pixon", st.get("digpix_on", 0)))
        f = int(st.get("feon", st.get("analog_on", 0)))
        t = int(st.get("tdc_mats", 0))
        pp = round(100.0 * p / n_pix, 1)
        pf = round(100.0 * f / n_pix, 1)
        pt = round(100.0 * t / n_mat, 1)
        lines = [
            f"{quad} — FEON (analog FE) = {f}/{n_pix}  ·  PIXON (digital out) = {p}/{n_pix}",
            f"FEON bit7: {f} / {n_pix} ({pf}%)",
            f"PIXON bit6: {p} / {n_pix} ({pp}%)",
            f"TDCon: {t} / {n_mat} MAT ({pt}%)",
            "Power consumption: — mW (per-quadrant: n/a in API)",
            "Analog ch. calibrated: — (n/a)",
        ]
        return "\n".join(lines)

    def _placeholder_quad_monitor_block(self, quad: str, *, offline: bool) -> str:
        tag = "OFFLINE" if offline else "…"
        lines = [
            f"{quad} — channels on: {tag}",
            f"FEON (analog FE): {tag}",
            f"PIXON (digital out): {tag}",
            f"TDCon: {tag}",
            "Power consumption: — mW (per-quadrant: n/a in API)",
            "Analog ch. calibrated: — (n/a)",
        ]
        return "\n".join(lines)

    def _apply_monitor_panels_placeholder(self) -> None:
        for q in ("NW", "NE", "SW", "SE"):
            self._quad_monitor_canvas[q] = self._placeholder_quad_monitor_block(q, offline=False)
        self._trigger_die_canvas_refresh()

    def _apply_monitor_panels_offline(self) -> None:
        for q in ("NW", "NE", "SW", "SE"):
            self._quad_monitor_canvas[q] = self._placeholder_quad_monitor_block(q, offline=True)
        self._trigger_die_canvas_refresh()

    def _apply_monitor_panels_from_stats(
        self,
        stats: dict[str, dict[str, int]],
        *,
        power_ok: Optional[bool],
        power_err: Optional[str],
    ) -> None:
        for q in ("NW", "NE", "SW", "SE"):
            st = stats[q]
            self._quad_monitor_canvas[q] = self._format_quad_monitor_block(
                q, st, power_ok=power_ok, power_err=power_err
            )
        self._trigger_die_canvas_refresh()
        # Update analog power button in Quadrants view (if present).
        if self._analog_power_btn is not None:
            self._update_analog_power_button(power_ok=power_ok, power_err=power_err)

    def _apply_monitor_panels_partial(
        self,
        quad: str,
        st: dict[str, int],
        *,
        power_ok: Optional[bool],
        power_err: Optional[str],
    ) -> None:
        q = str(quad).strip().upper()
        if q not in self._quad_monitor_canvas:
            return
        self._quad_monitor_canvas[q] = self._format_quad_monitor_block(q, st, power_ok=power_ok, power_err=power_err)
        self._trigger_die_canvas_refresh()
        if self._analog_power_btn is not None:
            self._update_analog_power_button(power_ok=power_ok, power_err=power_err)

    def _update_analog_power_button(self, *, power_ok: Optional[bool], power_err: Optional[str]) -> None:
        if self._analog_power_btn is None:
            return
        if power_err:
            self._analog_power_btn.configure(
                bg="#757575",
                activebackground="#757575",
                fg="white",
                text="Analog Power: ERR",
            )
            return
        if power_ok is None:
            return
        self._analog_power_btn.configure(
            bg="#1f9d55" if power_ok else "#aa2222",
            activebackground="#1f9d55" if power_ok else "#aa2222",
            fg="white",
            text=f"Analog Power: {'ON' if power_ok else 'OFF'} (click to {'OFF' if power_ok else 'ON'})",
        )

    def _apply_monitor_panels_error(self, msg: str) -> None:
        short = str(msg).replace("\n", " ")[:120]
        for q in ("NW", "NE", "SW", "SE"):
            self._quad_monitor_canvas[q] = f"(monitor)\n{short}"
        self._trigger_die_canvas_refresh()

    def _run_die_monitor_scan_async(self, seq: int) -> None:
        def work() -> None:
            try:
                stats_one: Optional[tuple[str, dict[str, int]]] = None
                power_ok: Optional[bool] = None
                power_err: Optional[str] = None
                # Do not block interactive actions:
                # take the HW lock only for short per-quadrant sequences; skip if busy.
                qs = ("NW", "NE", "SW", "SE")
                try:
                    qsel = qs[int(self._quadrants_mon_rr_idx) % len(qs)]
                except Exception:
                    qsel = "NW"
                try:
                    self._quadrants_mon_rr_idx = (int(self._quadrants_mon_rr_idx) + 1) % len(qs)
                except Exception:
                    self._quadrants_mon_rr_idx = 0

                got = False
                try:
                    got = bool(self._hw_seq_lock.acquire(timeout=0.03))
                except Exception:
                    got = False
                if got:
                    try:
                        stats_one = (qsel, self._gather_quad_stats(qsel))
                    finally:
                        try:
                            self._hw_seq_lock.release()
                        except Exception:
                            pass

                # Read Analog Power state once (best effort) without blocking.
                got = False
                try:
                    got = bool(self._hw_seq_lock.acquire(timeout=0.03))
                except Exception:
                    got = False
                if got:
                    try:
                        power_ok = bool(self.hw.readAnalogPower())
                    except Exception as e:
                        power_err = str(e)
                    finally:
                        try:
                            self._hw_seq_lock.release()
                        except Exception:
                            pass

                def apply() -> None:
                    if seq != self._quadrants_mon_seq:
                        return
                    if stats_one is None:
                        return
                    q, st = stats_one
                    self._apply_monitor_panels_partial(q, st, power_ok=power_ok, power_err=power_err)

                self.after(0, apply)
            except Exception as e:
                err_s = str(e)

                def fail() -> None:
                    if seq != self._quadrants_mon_seq:
                        return
                    self._apply_monitor_panels_error(err_s)

                self.after(0, fail)

        threading.Thread(target=work, daemon=True).start()

    def _schedule_quadrant_monitor_refresh(self, frm: ttk.Frame) -> None:
        self._cancel_quadrants_mon_job()
        self._quadrants_mon_seq += 1
        seq = self._quadrants_mon_seq

        def on_destroy(ev: tk.Event) -> None:
            if ev.widget is frm:
                self._quadrants_mon_seq += 1
                self._cancel_quadrants_mon_job()

        frm.bind("<Destroy>", on_destroy, add="+")

        if self.offline:
            self._apply_monitor_panels_offline()
            return
        # Manual mode by default: do not schedule any background refresh.
        # Use ICBOOST_AUTO_REFRESH=1 to re-enable periodic scan, or press explicit Refresh buttons.
        if not _env_truthy("ICBOOST_AUTO_REFRESH", "0"):
            return
        if _env_truthy("ICBOOST_DISABLE_MONITOR", "0"):
            return

        def tick() -> None:
            if not frm.winfo_exists() or seq != self._quadrants_mon_seq:
                return
            self._run_die_monitor_scan_async(seq)
            # Lighter monitor: one quadrant per tick (round-robin) → full cycle in ~8s.
            self._quadrants_mon_job = self.after(2000, tick)

        self._quadrants_mon_job = self.after(250, tick)

    def _cancel_quadrants_top_mon_job(self) -> None:
        jid = getattr(self, "_quadrants_top_mon_job", None)
        if jid is not None:
            try:
                self.after_cancel(jid)
            except Exception:
                pass
            self._quadrants_top_mon_job = None

    def _touch_quadrants_top_mon_from_trace(self) -> None:
        """Aggiorna subito il riepilogo TOP sulla home Quadrants se il job è attivo."""
        seq = int(getattr(self, "_quadrants_top_mon_seq", 0))
        if seq <= 0:
            return
        self._run_quadrants_top_mon_async(seq)

    def _run_quadrants_top_mon_async(self, seq: int) -> None:
        """Lettura TOP (mux = quadrante corrente) per il monitor sulla home."""

        def work() -> None:
            if self.offline:

                def apply_off() -> None:
                    if seq != self._quadrants_top_mon_seq:
                        return
                    self._quadrants_top_mon_var.set("TOP (read): OFFLINE — connect hardware.")

                self.after(0, apply_off)
                return
            try:
                qq = self.quad_var.get().strip().upper()
                if qq not in ("NW", "NE", "SW", "SE"):
                    qq = "SW"
                with self._hw_seq_lock:
                    self.hw.select_quadrant(qq)
                    d = int(self.hw.readTopDriverSTR())
                    ro = Ignite64Gui._norm_top_readout(self.hw.readTopReadout())
                    sl = Ignite64Gui._norm_top_slvs(self.hw.readTopSLVS())
                    tp = self.hw.readStartTP()
                rep = int(tp["repetition"])
                st_on = "ON" if tp.get("start") else "OFF"
                line = (
                    f"TOP [{qq}]  DriverSTR={d}  readout={ro}  SLVS={sl}  "
                    f"StartTP={st_on}  repet={rep}"
                )
            except Exception as e:
                line = f"TOP (read): {str(e)[:180]}"

            def apply() -> None:
                if seq != self._quadrants_top_mon_seq:
                    return
                self._quadrants_top_mon_var.set(line)

            self.after(0, apply)

        threading.Thread(target=work, daemon=True).start()

    def _schedule_quadrants_top_monitor(self, frm: ttk.Frame) -> None:
        """Aggiornamento periodico del riepilogo TOP sulla pagina Quadrants (solo lettura)."""
        self._cancel_quadrants_top_mon_job()
        self._quadrants_top_mon_seq += 1
        seq = self._quadrants_top_mon_seq

        def on_destroy(ev) -> None:
            if ev.widget is frm:
                self._quadrants_top_mon_seq += 1
                self._cancel_quadrants_top_mon_job()

        frm.bind("<Destroy>", on_destroy, add="+")

        if self.offline:
            self._quadrants_top_mon_var.set("TOP (read): OFFLINE — connect hardware.")
            return
        # Manual mode by default: no periodic TOP reads unless ICBOOST_AUTO_REFRESH=1.
        if not _env_truthy("ICBOOST_AUTO_REFRESH", "0"):
            return

        def tick() -> None:
            if not frm.winfo_exists() or seq != self._quadrants_top_mon_seq:
                return
            self._run_quadrants_top_mon_async(seq)
            self._quadrants_top_mon_job = self.after(3500, tick)

        self._quadrants_top_mon_job = self.after(200, tick)

    def _sel_top_apply_quad(self) -> str:
        v = self._top_apply_quad_var.get().strip().upper()
        return v if v in ("NW", "NE", "SW", "SE") else "NW"

    def _ensure_top_control_vars(self) -> None:
        """StringVar condivise per la striscia comandi TOP (una sola volta)."""
        if getattr(self, "_top_drv_var", None) is None:
            self._top_drv_var = tk.StringVar(value="0")
        if getattr(self, "_top_ro_var", None) is None:
            self._top_ro_var = tk.StringVar(value="i2c")
        if getattr(self, "_top_slvs_var", None) is None:
            self._top_slvs_var = tk.StringVar(value="hitor")
        if getattr(self, "_top_invtx_var", None) is None:
            self._top_invtx_var = tk.BooleanVar(value=False)
        if getattr(self, "_top_tp_rep_var", None) is None:
            self._top_tp_rep_var = tk.StringVar(value="1")
        if getattr(self, "_top_si_clk_in_var", None) is None:
            # Human-friendly subset of the C# options: Crystal vs SMA.
            self._top_si_clk_in_var = tk.StringVar(value="Crystal")
        if getattr(self, "_top_si5340_cfg_var", None) is None:
            # Default matches bring-up default in run_gui()/api.init_hw().
            self._top_si5340_cfg_var = tk.StringVar(value="Si5340-RevD_Crystal-Registers_bis.txt")
        if getattr(self, "_top_status_var", None) is None:
            self._top_status_var = tk.StringVar(value="—")

    def _before_top_write(self) -> None:
        """TOP registers are per quadrante: mux prima di ogni scrittura."""
        self.hw.select_quadrant(self._sel_top_apply_quad())

    @staticmethod
    def _norm_top_readout(val: object) -> str:
        s = str(val or "").lower()
        if "i2c" in s:
            return "i2c"
        if "ser" in s:
            return "ser"
        if "none" in s:
            return "none"
        return "none"

    @staticmethod
    def _norm_top_slvs(val: object) -> str:
        s = str(val or "").lower()
        if "hitor" in s or "hit_or" in s.replace(" ", ""):
            return "hitor"
        if "clk40" in s or "clk" in s:
            return "clk40"
        return "hitor"

    def _format_top_snapshot_block(self, sn: dict[str, object]) -> str:
        tp_raw = sn.get("start_tp")
        tp = tp_raw if isinstance(tp_raw, dict) else {}
        r9 = sn.get("top_reg9")
        r10 = sn.get("top_reg10")
        r11 = sn.get("top_reg11")

        def reg_line(name: str, val: object) -> str:
            if val is None:
                return f"  {name} = —"
            iv = int(val)
            return f"  {name} = 0x{iv:02X}  ({iv})"

        st_on = "ON" if tp.get("start") else "OFF"
        lines = [
            f"Driver STR (SLVS): {sn.get('driver_str', '—')}",
            f"Readout interface: {sn.get('readout', '—')}",
            f"GPO SLVS: {sn.get('slvs', '—')}",
            f"FE polarity: {sn.get('fe_polarity', '—')}",
            "",
            "TP signal (TOP reg 11 — StartTP / repet / EOS):",
            f"  Start TP: {st_on}",
            f"  Repetition (LSB): {tp.get('repetition', '—')}",
            f"  EOS (bit7): {tp.get('eos', '—')}",
            "",
            "Raw TOP byte (AFE pulse — tp_width / start_tp, see firmware / README):",
            reg_line("TOP[9]", r9),
            reg_line("TOP[10]", r10),
            reg_line("TOP[11]", r11),
        ]
        return "\n".join(lines)

    def _top_snapshot_set_text(self, w: tk.Text, s: str) -> None:
        w.configure(state="normal")
        w.delete("1.0", "end")
        w.insert("1.0", s)
        w.configure(state="disabled")

    def _schedule_top_snapshot_refresh(self) -> None:
        """Aggiorna i pannelli di lettura TOP (vista dedicata, thread I2C)."""
        texts = getattr(self, "_top_snapshot_texts", {})
        if not texts:
            return
        if self.offline:
            for q, txt in texts.items():
                self._top_snapshot_set_text(txt, f"Quadrant {q}\n\nOFFLINE — no HW read.")
            return

        def work() -> None:
            err: Optional[str] = None
            data: dict[str, dict[str, object]] = {}
            try:
                for q in ("NW", "NE", "SW", "SE"):
                    data[q] = self.hw.readTopSnapshot(q)
            except Exception as e:
                err = str(e)

            def apply() -> None:
                tw = getattr(self, "_top_snapshot_texts", {})
                if not tw:
                    return
                if err:
                    for w in tw.values():
                        self._top_snapshot_set_text(w, err[:800])
                    return
                for q, sn in data.items():
                    if q in tw:
                        self._top_snapshot_set_text(tw[q], self._format_top_snapshot_block(sn))

            self.after(0, apply)

        threading.Thread(target=work, daemon=True).start()

    def _install_top_snapshot_grid(self, parent: ttk.Frame) -> None:
        """Costruisce la griglia 2×2 dei riepiloghi TOP e riempie `_top_snapshot_texts`."""
        self._top_snapshot_texts = {}
        bg = getattr(self, "_window_bg", "#f0f0f0")
        grid = ttk.Frame(parent)
        grid.pack(fill="both", expand=True)
        for col in range(2):
            grid.columnconfigure(col, weight=1)
        for row in range(2):
            grid.rowconfigure(row, weight=1)
        order = (("NW", 0, 0), ("NE", 0, 1), ("SW", 1, 0), ("SE", 1, 1))
        for q, rr, cc in order:
            lf = ttk.Labelframe(grid, text=f"Quadrant {q}", padding=8)
            lf.grid(row=rr, column=cc, sticky="nsew", padx=6, pady=6)
            t = tk.Text(
                lf,
                height=18,
                width=44,
                font=("Segoe UI", 11),
                wrap="word",
                relief="flat",
                padx=4,
                pady=4,
            )
            t.pack(fill="both", expand=True)
            try:
                t.configure(background=bg)
            except Exception:
                pass
            t.configure(state="disabled")
            self._top_snapshot_texts[q] = t

    def _build_top_view(self, parent: ttk.Frame) -> ttk.Frame:
        """Vista TOP: prima scrittura (mux), sotto lettura 4× quadranti."""
        frm = ttk.Frame(parent)
        head = ttk.Frame(frm)
        head.pack(fill="x", pady=(0, 6))
        ttk.Label(head, text="TOP — all quadrants", font=("Segoe UI", 16, "bold")).pack(anchor="w")
        ttk.Label(
            head,
            text="Above: TOP register write controls. Below: detailed readout for NW/NE/SW/SE.",
            font=("Segoe UI", 10),
            foreground="#424242",
        ).pack(anchor="w", pady=(4, 0))

        # Striscia scrittura PRIMA della griglia: altrimenti expand sulla griglia spinge i comandi sotto il bordo finestra.
        self._build_top_controls_strip(frm).pack(fill="x", pady=(10, 0))

        ttk.Label(frm, text="TOP register readout (per quadrant)", font=("Segoe UI", 11, "bold")).pack(
            anchor="w", pady=(14, 4)
        )
        bar = ttk.Frame(frm)
        bar.pack(fill="x", pady=(0, 6))
        ttk.Button(bar, text="Refresh readout", command=self._schedule_top_snapshot_refresh).pack(side="left")

        body = ttk.Frame(frm)
        body.pack(fill="both", expand=True, pady=(4, 0))
        self._install_top_snapshot_grid(body)

        def _on_top_destroy(ev) -> None:
            if ev.widget == frm:
                self._top_snapshot_texts = {}

        frm.bind("<Destroy>", _on_top_destroy)

        return frm

    def _open_top_view(self) -> None:
        """Apre la vista TOP nella finestra principale (torna con «Indietro»)."""
        q = self.quad_var.get().strip().upper()
        if q in ("NW", "NE", "SW", "SE"):
            self._top_apply_quad_var.set(q)
        self._push_view(self._build_top_view(self.content))
        self.after_idle(self._schedule_top_snapshot_refresh)

    def _build_top_controls_strip(self, parent: ttk.Frame) -> ttk.Labelframe:
        """TOP registers: driver strength, readout path, SLVS mode, AFE pulse (StartTP + repetition)."""
        self._ensure_top_control_vars()
        lf = ttk.Labelframe(
            parent,
            text="TOP — write (mux); home summary follows current quadrant (navigation)",
            padding=8,
        )
        g = ttk.Frame(lf)
        g.pack(fill="x")

        from pathlib import Path
        import os

        cfg_base = Path(__file__).resolve().parents[1] / "ConfigurationFiles"

        def _resolve_cfg_path(name_or_path: str) -> Path:
            s = str(name_or_path or "").strip()
            if not s:
                return cfg_base / "Si5340-RevD_Crystal-Registers_bis.txt"
            p = Path(s)
            return p if (p.is_absolute() or len(p.parts) > 1) else (cfg_base / p)

        def _si5340_candidates() -> list[str]:
            try:
                cand = sorted(
                    [p.name for p in cfg_base.glob("Si5340-*.txt")],
                    key=lambda x: x.lower(),
                )
                return cand if cand else [self._top_si5340_cfg_var.get()]
            except Exception:
                return [self._top_si5340_cfg_var.get()]

        r = 0
        ttk.Label(g, text="Quadrant (write / readback)").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        ttk.Combobox(
            g,
            textvariable=self._top_apply_quad_var,
            values=("NW", "NE", "SW", "SE"),
            width=7,
            state="readonly",
        ).grid(row=r, column=1, columnspan=2, sticky="w", pady=2)
        r += 1

        ttk.Label(g, text="Driver STR (0–15)").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        ttk.Spinbox(g, from_=0, to=15, textvariable=self._top_drv_var, width=6).grid(row=r, column=1, sticky="w", pady=2)

        def apply_drv() -> None:
            if self.offline:
                self._set_status("OFFLINE")
                return
            try:
                self._before_top_write()
                self.hw.TopDriverSTR(int(self._top_drv_var.get()))
                self._set_status(f"TOP Driver STR applied → {self._sel_top_apply_quad()}")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Apply", command=apply_drv).grid(row=r, column=2, padx=8, pady=2)
        r += 1

        ttk.Label(g, text="Readout").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        ro_cb = ttk.Combobox(g, textvariable=self._top_ro_var, values=("i2c", "ser", "none"), width=10, state="readonly")
        ro_cb.grid(row=r, column=1, columnspan=2, sticky="w", pady=2)

        def apply_ro() -> None:
            if self.offline:
                return
            try:
                self._before_top_write()
                self.hw.TopReadout(self._top_ro_var.get())
                self._set_status(f"Readout set → {self._top_ro_var.get()} ({self._sel_top_apply_quad()})")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Apply readout", command=apply_ro).grid(row=r, column=3, padx=8, pady=2)
        r += 1

        ttk.Label(g, text="GPO SLVS").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        slvs_cb = ttk.Combobox(g, textvariable=self._top_slvs_var, values=("hitor", "clk40"), width=10, state="readonly")
        slvs_cb.grid(row=r, column=1, columnspan=2, sticky="w", pady=2)

        def apply_slvs() -> None:
            if self.offline:
                return
            try:
                self._before_top_write()
                self.hw.TopSLVS(self._top_slvs_var.get())
                self._set_status(f"SLVS set → {self._top_slvs_var.get()} ({self._sel_top_apply_quad()})")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Apply SLVS", command=apply_slvs).grid(row=r, column=3, padx=8, pady=2)
        r += 1

        ttk.Label(g, text="tTX inv TX").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        ttk.Checkbutton(g, variable=self._top_invtx_var).grid(row=r, column=1, sticky="w", pady=2)

        def apply_invtx() -> None:
            if self.offline:
                return
            try:
                self._before_top_write()
                self.hw.TopSlvsInvTx(bool(self._top_invtx_var.get()))
                self._set_status(f"SLVS_INVTX applied → {int(bool(self._top_invtx_var.get()))} ({self._sel_top_apply_quad()})")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Apply invTX", command=apply_invtx).grid(row=r, column=3, padx=8, pady=2)
        r += 1

        ttk.Label(g, text="TP repet (0–63)").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        ttk.Spinbox(g, from_=0, to=63, textvariable=self._top_tp_rep_var, width=6).grid(row=r, column=1, sticky="w", pady=2)

        def pulse_tp() -> None:
            if self.offline:
                return
            try:
                self._before_top_write()
                n = int(self._top_tp_rep_var.get())
                self.hw.StartTP(numberOfRepetition=n)
                self._set_status(f"StartTP repet={n} ({self._sel_top_apply_quad()})")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Start TP / pulse", command=pulse_tp).grid(row=r, column=2, padx=8, pady=2)
        r += 1

        # --- IOext: SI_CLK input source select (Crystal vs SMA) ---
        ttk.Label(g, text="SI_CLK IN sel").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        si_in_cb = ttk.Combobox(
            g,
            textvariable=self._top_si_clk_in_var,
            values=("Crystal", "SMA"),
            width=10,
            state="readonly",
        )
        si_in_cb.grid(row=r, column=1, sticky="w", pady=2)

        def apply_si_clk_in() -> None:
            if self.offline:
                return
            try:
                self._before_top_write()
                sel = str(self._top_si_clk_in_var.get()).strip().lower()
                # C# mapping: 0=SMA, 3=Crystal
                raw = 0 if ("sma" in sel) else 3
                self.hw.setSIClkInSel(int(raw))
                self._set_status(f"SI_CLK IN sel → {self._top_si_clk_in_var.get()} ({self._sel_top_apply_quad()})")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Apply", command=apply_si_clk_in).grid(row=r, column=2, padx=8, pady=2)
        r += 1

        # --- SI5340: load a clock configuration file ---
        ttk.Label(g, text="SI5340 cfg").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
        si_cfg_cb = ttk.Combobox(
            g,
            textvariable=self._top_si5340_cfg_var,
            values=_si5340_candidates(),
            width=42,
        )
        si_cfg_cb.grid(row=r, column=1, columnspan=2, sticky="w", pady=2)

        def browse_si_cfg() -> None:
            try:
                p = filedialog.askopenfilename(
                    title="Select SI5340 configuration file",
                    initialdir=str(cfg_base),
                    filetypes=[("SI5340 config", "*.txt"), ("All files", "*.*")],
                )
                if p:
                    try:
                        # Prefer showing just the filename if it's inside ConfigurationFiles/
                        pp = Path(p)
                        if cfg_base in pp.parents:
                            self._top_si5340_cfg_var.set(pp.name)
                        else:
                            self._top_si5340_cfg_var.set(str(pp))
                    except Exception:
                        self._top_si5340_cfg_var.set(str(p))
            except Exception as e:
                self._set_status(str(e))

        def apply_si_cfg() -> None:
            if self.offline:
                return
            try:
                self._before_top_write()
                p = _resolve_cfg_path(self._top_si5340_cfg_var.get())
                self.hw.loadClockSetting(str(p))
                self._set_status(f"SI5340 configured from {p.name} ({self._sel_top_apply_quad()})")
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Browse…", command=browse_si_cfg).grid(row=r, column=3, padx=8, pady=2)
        ttk.Button(g, text="Apply clock cfg", command=apply_si_cfg).grid(row=r, column=4, padx=8, pady=2)

        def refresh_top_ro() -> None:
            if self.offline:
                self._top_status_var.set("OFFLINE — TOP controls disabled")
                return
            try:
                self._before_top_write()
                qq = self._sel_top_apply_quad()
                d = int(self.hw.readTopDriverSTR())
                self._top_drv_var.set(str(d))
                self._top_ro_var.set(self._norm_top_readout(self.hw.readTopReadout()))
                self._top_slvs_var.set(self._norm_top_slvs(self.hw.readTopSLVS()))
                try:
                    self._top_invtx_var.set(bool(self.hw.readTopSlvsInvTx()))
                except Exception:
                    pass
                tp = self.hw.readStartTP()
                self._top_tp_rep_var.set(str(int(tp["repetition"])))
                try:
                    raw = int(self.hw.readSIClkInSel())
                    self._top_si_clk_in_var.set("SMA" if raw == 0 else ("Crystal" if raw == 3 else f"idx{raw}"))
                except Exception:
                    pass
                self._top_status_var.set(
                    f"TOP [{qq}]: readout={self._top_ro_var.get()}  SLVS={self._top_slvs_var.get()}  "
                    f"StartTP={tp['start']} repet={tp['repetition']}  "
                    f"(full registers in NW/NE/SW/SE panels above)"
                )
            except Exception as e:
                self._top_status_var.set(str(e)[:200])

        ttk.Button(g, text="Read TOP", command=refresh_top_ro).grid(row=r, column=3, padx=8, pady=2)
        r += 1

        ttk.Label(g, textvariable=self._top_status_var, wraplength=920, font=("Segoe UI", 10)).grid(
            row=r, column=0, columnspan=5, sticky="w", pady=(6, 0)
        )

        # Manual mode: keep bus idle unless the user presses "Read TOP".
        if (not self.offline) and _env_truthy("ICBOOST_AUTO_REFRESH", "0"):
            self.after(400, refresh_top_ro)
        else:
            self._top_status_var.set("OFFLINE")

        return lf

    # -----
    # Views
    # -----
    def _build_quadrants_view(self, parent: ttk.Frame) -> ttk.Frame:
        frm = ttk.Frame(parent)

        def _on_qhome_destroy(ev: tk.Event) -> None:
            if ev.widget is frm:
                self._die_canvas_redraw = None

        frm.bind("<Destroy>", _on_qhome_destroy, add="+")

        hdr = ttk.Frame(frm)
        hdr.pack(fill="x", pady=(0, 10))
        ttk.Label(hdr, text="IGNITE64 ASIC", font=("Segoe UI", 16, "bold")).pack(side="left", anchor="w")
        Image_b, _ImageTk_b = _maybe_load_pil()
        bim, _bwhy = self._load_banner_pil(Image_b)
        if bim is not None and Image_b is not None and _ImageTk_b is not None:
            try:
                w0, h0 = bim.size
                target_h = 52
                nw = max(1, int(w0 * (target_h / float(h0))))
                rs = _pil_resample_lanczos(Image_b)
                im2 = bim.resize((nw, target_h), rs)
                ph = _ImageTk_b.PhotoImage(im2.convert("RGBA"))
                self._banner_photo_ref = ph
                tk.Label(hdr, image=ph, bg=self._window_bg, cursor="").pack(side="right", padx=(12, 0))
            except Exception:
                self._banner_photo_ref = None

        top_mon = ttk.Labelframe(
            frm,
            text="TOP — status & reads (mux follows current quadrant / navigation)",
            padding=8,
        )
        top_mon.pack(fill="x", pady=(0, 6))
        top_mon_btn = ttk.Frame(top_mon)
        top_mon_btn.pack(fill="x", pady=(0, 8))
        ttk.Button(top_mon_btn, text="Open TOP page…", command=self._open_top_view).pack(side="left")
        ttk.Label(
            top_mon_btn,
            text="Driver, readout, SLVS, StartTP, …",
            font=("Segoe UI", 9),
            foreground="#666666",
        ).pack(side="left", padx=(10, 0))
        ttk.Label(
            top_mon,
            textvariable=self._quadrants_top_mon_var,
            font=("Segoe UI", 10),
            wraplength=1280,
            justify="left",
        ).pack(anchor="w")

        # Analog power toggle: green when ON, red when OFF.
        # This uses the global IOext GPIO reg10 bit6 (inverted logic, same as C#).
        def _analog_power_toggle() -> None:
            if self.offline:
                self._set_status("Analog Power toggle (offline)")
                return

            quad_dbg = str(self.quad_var.get()).strip().upper()
            _dbg(f"Analog Power toggle pressed (quad={quad_dbg})")

            btn = self._analog_power_btn
            if btn is not None:
                try:
                    btn.configure(state="disabled")
                except Exception:
                    pass

            busy_msg = f"Analog Power toggle (Q={self.quad_var.get()})"
            self._set_status(busy_msg)

            def work() -> None:
                # Avoid freezing Tk: HW call runs in background thread.
                # Use a timed lock acquire so we can fail fast if another HW sequence is stuck.
                new_state: Optional[bool] = None
                err_s: Optional[str] = None
                got = False
                try:
                    got = bool(self._hw_seq_lock.acquire(timeout=0.5))
                    if not got:
                        err_s = "HW busy (lock timeout)"
                        return
                    cur_raw = self.hw.readAnalogPower()
                    cur = bool(cur_raw)
                    target = not cur
                    _dbg(f"Analog Power read cur_raw={cur_raw!r} cur={cur} → target={target}")
                    self.hw.setAnalogPower(target)
                    new_state = bool(target)
                except Exception as e:
                    err_s = str(e)
                    _dbg(f"Analog Power toggle failed err={e!r}")
                finally:
                    if got:
                        try:
                            self._hw_seq_lock.release()
                        except Exception:
                            pass

                def apply() -> None:
                    if err_s:
                        self._set_status(f"Error: {err_s}")
                        self._update_analog_power_button(power_ok=None, power_err=err_s)
                    elif new_state is not None:
                        # Optimistic UI update; monitor will re-sync next tick anyway.
                        self._update_analog_power_button(power_ok=new_state, power_err=None)
                        self._set_status(f"Analog Power: {'ON' if new_state else 'OFF'}")
                        _dbg(f"Analog Power toggle completed new_state={new_state}")
                    if btn is not None:
                        try:
                            btn.configure(state="normal")
                        except Exception:
                            pass

                self.after(0, apply)

            threading.Thread(target=work, daemon=True).start()

        # Prefer a classic tk.Button (tk/tile colors are more consistent than ttk style backgrounds).
        self._analog_power_btn = tk.Button(
            frm,
            text="Analog Power: —",
            bg="#757575" if self.offline else "#757575",
            fg="white",
            activebackground="#757575",
            activeforeground="white",
            command=_analog_power_toggle,
            relief="raised",
            width=26,
        )
        if self.offline:
            self._analog_power_btn.configure(state="disabled")
        self._analog_power_btn.pack(anchor="w", pady=(0, 10))

        ttk.Label(
            frm,
            text="Die photo (or placeholder grid if JPEG missing): click a quadrant for FIFO / calib. TOP: button above. Per-quadrant monitors on the sides. Analog Power: button below.",
            font=("Segoe UI", 9),
            foreground="#555555",
        ).pack(anchor="w", pady=(0, 4))

        win_bg = getattr(self, "_window_bg", "#f0f0f0")
        Image_pil, ImageTk_mod = _maybe_load_pil()
        die_im, _die_meta = self._load_die_photo_pil(Image_pil)
        use_die = die_im is not None and Image_pil is not None and ImageTk_mod is not None

        die_host = ttk.Frame(frm)
        die_host.pack(fill="both", expand=True, pady=(0, 6))

        die_grid = ttk.Frame(die_host)
        die_grid.pack(fill="both", expand=True)
        # Side columns: quadrant monitors. Center column: die photo — uses full height between rows.
        die_grid.columnconfigure(0, weight=1, minsize=180)
        die_grid.columnconfigure(1, weight=8, minsize=420)
        die_grid.columnconfigure(2, weight=1, minsize=180)
        die_grid.rowconfigure(0, weight=1)
        die_grid.rowconfigure(1, weight=1)

        self._home_quad_mon_texts = {}

        def _add_home_quad_mon_cell(row: int, col: int, q: str, sticky: str) -> None:
            cell = tk.Frame(die_grid, bg=self._window_bg, highlightbackground="#90a4ae", highlightthickness=1)
            cell.grid(row=row, column=col, sticky=sticky, padx=4, pady=4)
            tk.Label(
                cell,
                text=f" {q} ",
                font=("Segoe UI", 10, "bold"),
                fg="#b71c1c",
                bg=self._window_bg,
            ).pack(anchor="w", padx=4, pady=(2, 0))
            tx = tk.Text(
                cell,
                height=18,
                width=48,
                wrap="word",
                font=("Consolas", 10),
                bg="#fafafa",
                relief="flat",
                highlightthickness=0,
                padx=4,
                pady=2,
            )
            tx.pack(fill="both", expand=True, padx=4, pady=(0, 4))
            tx.configure(state="disabled")
            self._home_quad_mon_texts[q] = tx

        _add_home_quad_mon_cell(0, 0, "NW", "nsew")
        _add_home_quad_mon_cell(1, 0, "SW", "nsew")
        canvas_host = tk.Frame(die_grid, bg=win_bg)
        canvas_host.grid(row=0, column=1, rowspan=2, sticky="nsew", pady=4, padx=(8, 8))
        _add_home_quad_mon_cell(0, 2, "NE", "nsew")
        _add_home_quad_mon_cell(1, 2, "SE", "nsew")

        if use_die:
            iw, ih = die_im.size
            cv_die = tk.Canvas(canvas_host, highlightthickness=0, bd=0, bg=win_bg)
            cv_die.pack(fill="both", expand=True)

            def redraw_die(_ev=None) -> None:
                cv_die.delete("all")
                cw = int(cv_die.winfo_width())
                ch = int(cv_die.winfo_height())
                if cw < 8 or ch < 8:
                    return
                nw, nh, x_off, y_off = _die_contain_geom(iw, ih, cw, ch)
                rs = _pil_resample_lanczos(Image_pil)
                im2 = die_im.resize((nw, nh), rs)
                ph = ImageTk_mod.PhotoImage(im2.convert("RGBA"))
                cv_die._die_photo_ref = ph  # type: ignore[attr-defined]
                cv_die.create_image(x_off, y_off, image=ph, anchor="nw", tags=("die_bg",))

                # Quadrato rosso = intera immagine chip (bitmap ridimensionata), non solo IGNITE_DIE_CHIP_BOX.
                bx0 = float(x_off)
                by0 = float(y_off)
                bx1 = float(x_off + nw)
                by1 = float(y_off + nh)
                mu = (bx0 + bx1) * 0.5
                mv = (by0 + by1) * 0.5

                cv_die.create_rectangle(bx0, by0, bx1, by1, outline="#b71c1c", width=3, tags=("die_overlay",))
                cv_die.create_line(mu, by0, mu, by1, fill="#4e342e", width=2, tags=("die_overlay",))
                cv_die.create_line(bx0, mv, bx1, mv, fill="#4e342e", width=2, tags=("die_overlay",))

                hh_px = max(1.0, float(nh))
                fz = max(14, min(96, int(hh_px * 0.12)))

                centers = {
                    "NW": ((bx0 + mu) * 0.5, (by0 + mv) * 0.5),
                    "NE": ((mu + bx1) * 0.5, (by0 + mv) * 0.5),
                    "SW": ((bx0 + mu) * 0.5, (mv + by1) * 0.5),
                    "SE": ((mu + bx1) * 0.5, (mv + by1) * 0.5),
                }
                for qn, (cx, cy) in centers.items():
                    _draw_quad_label(cv_die, cx, cy, qn, fz)

            def on_die_click(ev: tk.Event) -> None:
                cw2 = int(cv_die.winfo_width())
                ch2 = int(cv_die.winfo_height())
                if cw2 < 4 or ch2 < 4:
                    return
                nw2, nh2, x_off2, y_off2 = _die_contain_geom(iw, ih, cw2, ch2)
                u, v = _canvas_to_norm_uv_contain(float(ev.x), float(ev.y), nw2, nh2, x_off2, y_off2)
                if not (-1e-6 <= u <= 1.0 + 1e-6 and -1e-6 <= v <= 1.0 + 1e-6):
                    return
                u = max(0.0, min(1.0, u))
                v = max(0.0, min(1.0, v))
                _full = (0.0, 0.0, 1.0, 1.0)
                qq = _quad_from_uv_in_chip_box(u, v, _full)
                if qq:
                    self._open_quadrant(qq)

            cv_die.bind("<Configure>", redraw_die)
            cv_die.bind("<Button-1>", on_die_click)
            cv_die.configure(cursor="hand2")
            self._die_canvas_redraw = redraw_die
            self.after_idle(redraw_die)
            hint_chip = "Red box = die image bounds; monitors on the sides. Env: IGNITE_DIE_PHOTO."
        else:
            cv_fb = tk.Canvas(canvas_host, highlightthickness=0, bd=0, bg=win_bg)
            cv_fb.pack(fill="both", expand=True)

            def redraw_plain(_ev=None) -> None:
                cv_fb.delete("all")
                cw = int(cv_fb.winfo_width())
                ch = int(cv_fb.winfo_height())
                if cw < 24 or ch < 24:
                    return
                pad = max(20, min(cw, ch) // 12)
                avail_w = cw - 2 * pad
                avail_h = ch - 2 * pad
                side = min(avail_w, avail_h)
                side = max(120, side)
                x0 = (cw - side) // 2
                y0 = (ch - side) // 2
                x1 = x0 + side
                y1 = y0 + side
                mx = (x0 + x1) / 2
                my = (y0 + y1) / 2
                cv_fb.create_rectangle(x0, y0, x1, y1, outline="#b71c1c", width=3, tags=("plain_matrix",))
                fills = ("#ececef", "#e2e2e8", "#e2e2e8", "#ececef")
                cv_fb.create_rectangle(x0, y0, mx, my, outline="#757575", width=2, fill=fills[0])
                cv_fb.create_rectangle(mx, y0, x1, my, outline="#757575", width=2, fill=fills[1])
                cv_fb.create_rectangle(x0, my, mx, y1, outline="#757575", width=2, fill=fills[2])
                cv_fb.create_rectangle(mx, my, x1, y1, outline="#757575", width=2, fill=fills[3])
                cv_fb.create_line(x0, my, x1, my, fill="#555566", width=2)
                cv_fb.create_line(mx, y0, mx, y1, fill="#555566", width=2)
                fz_q = max(14, min(72, side // 5))
                _draw_quad_label(cv_fb, (x0 + mx) / 2, (y0 + my) / 2, "NW", fz_q)
                _draw_quad_label(cv_fb, (mx + x1) / 2, (y0 + my) / 2, "NE", fz_q)
                _draw_quad_label(cv_fb, (x0 + mx) / 2, (my + y1) / 2, "SW", fz_q)
                _draw_quad_label(cv_fb, (mx + x1) / 2, (my + y1) / 2, "SE", fz_q)
                cv_fb._plain_geom = (x0, y0, x1, y1, mx, my)  # type: ignore[attr-defined]

            def on_plain_click(ev: tk.Event) -> None:
                g = getattr(cv_fb, "_plain_geom", None)
                if not g:
                    return
                x0, y0, x1, y1, mx, my = g[:6]
                if not (x0 <= ev.x <= x1 and y0 <= ev.y <= y1):
                    return
                west = ev.x < mx
                north = ev.y < my
                if north:
                    qq = "NW" if west else "NE"
                else:
                    qq = "SW" if west else "SE"
                self._open_quadrant(qq)

            cv_fb.bind("<Configure>", redraw_plain)
            cv_fb.bind("<Button-1>", on_plain_click)
            cv_fb.configure(cursor="hand2")
            self._die_canvas_redraw = redraw_plain
            self.after_idle(redraw_plain)
            hint_chip = (
                "No die photo: simple grid + side monitors. Add icboost/assets/ignite64.jpg or set IGNITE_DIE_PHOTO."
            )

        if self.offline:
            self._apply_monitor_panels_offline()
        else:
            self._apply_monitor_panels_placeholder()

        self._schedule_quadrant_monitor_refresh(frm)
        self._schedule_quadrants_top_monitor(frm)

        ttk.Label(frm, text=hint_chip, font=("Segoe UI", 9), foreground="#444444").pack(anchor="w", pady=(8, 0))

        self.after_idle(self._refresh_home_quad_monitor_texts)

        return frm

    def _set_app_icon(self) -> None:
        """
        Best-effort application icon.
        Uses a PNG from assets (same approach as classic `wm iconphoto` snippets).
        """
        try:
            import sys

            env = os.environ.get("IGNITE64_ICON_PNG", "").strip().strip('"').strip("'")
            if env:
                ip = Path(env)
            else:
                ip = Path(__file__).resolve().parent / "assets" / "ignite64_asic_icon.png"
            if not ip.is_file():
                return
            ph = tk.PhotoImage(file=str(ip), master=self)
            self._icon_photo_ref = ph
            try:
                # Same method you used: root.tk.call('wm', 'iconphoto', root._w, PhotoImage(...))
                self.tk.call("wm", "iconphoto", self._w, ph)
            except Exception:
                try:
                    self.iconphoto(True, ph)
                except Exception:
                    pass

            # Windows taskbar: prefer a fixed .ico via iconbitmap (no generation here).
            if sys.platform.startswith("win"):
                try:
                    import ctypes

                    ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("ICBoost.IGNITE64.ASIC")  # type: ignore[attr-defined]
                except Exception:
                    pass
                try:
                    ico = Path(__file__).resolve().parent / "assets" / "ignite64_asic.ico"
                    if ico.is_file():
                        self.iconbitmap(default=str(ico))
                except Exception:
                    pass
        except Exception:
            # Never block GUI startup due to icon issues.
            return

    def _open_pulsing_window(self, quad: str) -> None:
        """
        AFE + TDC pulsing controls, plus FIFO monitor, roughly mirroring the C# GUI.
        """
        q = str(quad).strip().upper()
        if q not in ("NW", "NE", "SW", "SE"):
            q = str(self.quad_var.get()).strip().upper()
        if q not in ("NW", "NE", "SW", "SE"):
            q = "SW"

        win = tk.Toplevel(self)
        win.title(f"Pulsing — Q={q}")
        win.transient(self)
        win.resizable(True, True)

        root = ttk.Frame(win, padding=10)
        root.pack(fill="both", expand=True)
        root.columnconfigure(0, weight=1)
        root.columnconfigure(1, weight=1)

        status_var = tk.StringVar(value="—")
        ttk.Label(root, textvariable=status_var, wraplength=1100, font=("Segoe UI", 9)).grid(
            row=1, column=0, columnspan=2, sticky="w", pady=(8, 0)
        )

        # ----------------
        # AFE pulsing
        # ----------------
        afe = ttk.Labelframe(root, text="AFE PULSING", padding=10)
        afe.grid(row=0, column=0, sticky="nsew", padx=(0, 8))
        afe.columnconfigure(1, weight=1)

        tp_period = tk.StringVar(value="0")
        tp_width = tk.StringVar(value="0")
        tp_rep = tk.StringVar(value="0")
        tp_start = tk.BooleanVar(value=False)

        afe_mat = tk.StringVar(value="0")
        afe_ch = tk.StringVar(value="0")
        afe_hitor = tk.BooleanVar(value=False)

        def _afe_read() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return

            def do() -> dict[str, object]:
                self.hw.select_quadrant(q)
                return dict(self.hw.readTopTPPulse())

            out = self._with_hw(do, busy=f"Read TOP TP settings (Q={q})")
            if not isinstance(out, dict):
                return
            try:
                tp_period.set(str(int(out.get("tp_period", 0))))
                tp_width.set(str(int(out.get("tp_width", 0))))
                tp_rep.set(str(int(out.get("tp_repetition", 0))))
                tp_start.set(bool(out.get("start_tp", False)))
                status_var.set("TOP TP settings read OK.")
            except Exception:
                pass

        def _afe_apply_tp() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return

            def do() -> None:
                self.hw.select_quadrant(q)
                self.hw.TopTPPeriod(int(tp_period.get()))
                self.hw.TopTPWidth(int(tp_width.get()))
                self.hw.TopTPRepetition(int(tp_rep.get()))
                self.hw.TopStartTPFlag(bool(tp_start.get()))

            r = self._with_hw(do, busy=f"Apply TOP TP settings (Q={q})")
            if r is None:
                return
            status_var.set("Applied TOP TP settings.")

        def _afe_enable_atp() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return
            try:
                mid = int(str(afe_mat.get()).strip())
                ch = int(str(afe_ch.get()).strip())
            except Exception:
                status_var.set("AFE select channel: invalid MAT/CH")
                return

            def do() -> None:
                self.hw.ATPulse(q, mattonella=mid, canale=ch)

            r = self._with_hw(do, busy=f"Enable ATP Pulse (Q={q} MAT={mid} CH={ch})")
            if r is None:
                return
            status_var.set(f"ATP Pulse enabled for MAT {mid} CH {ch}.")

        def _afe_apply_hitor() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return
            try:
                mid = int(str(afe_mat.get()).strip())
            except Exception:
                status_var.set("HiTor: invalid MAT")
                return

            def do() -> None:
                self.hw.Hitor(q, mattonella=mid, valore=("HITOR" if bool(afe_hitor.get()) else "DAQTMR"))

            r = self._with_hw(do, busy=f"Set HiTor={int(bool(afe_hitor.get()))} (Q={q} MAT={mid})")
            if r is None:
                return
            status_var.set(f"HiTor {'ENABLED' if bool(afe_hitor.get()) else 'DISABLED'} on MAT {mid}.")

        def _afe_reset_tmr() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return

            def do() -> None:
                self.hw.select_quadrant(q)
                for mid in range(16):
                    # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
                    if 4 <= int(mid) <= 7:
                        continue
                    try:
                        self.hw.Hitor(q, mattonella=int(mid), valore="DAQTMR")
                    except Exception:
                        pass

            r = self._with_hw(do, busy=f"Reset MAT test mode to TMR (Q={q})")
            if r is None:
                return
            status_var.set("Reset done: MAT test mode set to TMR (best-effort; MAT 4–7 skipped).")

        r0 = 0
        ttk.Label(afe, text="TP period").grid(row=r0, column=0, sticky="w", pady=2)
        ttk.Spinbox(afe, from_=0, to=15, width=8, textvariable=tp_period).grid(row=r0, column=1, sticky="w", pady=2)
        r0 += 1
        ttk.Label(afe, text="TP width").grid(row=r0, column=0, sticky="w", pady=2)
        ttk.Spinbox(afe, from_=0, to=7, width=8, textvariable=tp_width).grid(row=r0, column=1, sticky="w", pady=2)
        r0 += 1
        ttk.Label(afe, text="TP repetition").grid(row=r0, column=0, sticky="w", pady=2)
        ttk.Spinbox(afe, from_=0, to=63, width=8, textvariable=tp_rep).grid(row=r0, column=1, sticky="w", pady=2)
        r0 += 1
        ttk.Checkbutton(afe, text="StartTP flag", variable=tp_start).grid(
            row=r0, column=0, columnspan=2, sticky="w", pady=(4, 2)
        )
        r0 += 1

        bar = ttk.Frame(afe)
        bar.grid(row=r0, column=0, columnspan=2, sticky="w", pady=(6, 10))
        ttk.Button(bar, text="Read", command=_afe_read).pack(side="left")
        ttk.Button(bar, text="Apply", command=_afe_apply_tp).pack(side="left", padx=(8, 0))

        ttk.Separator(afe, orient="horizontal").grid(row=r0 + 1, column=0, columnspan=2, sticky="ew", pady=(8, 8))
        r0 += 2

        ttk.Label(afe, text="Select channel (MAT, CH)").grid(row=r0, column=0, sticky="w", pady=2)
        row_sel = ttk.Frame(afe)
        row_sel.grid(row=r0, column=1, sticky="w", pady=2)
        ttk.Spinbox(row_sel, from_=0, to=15, width=5, textvariable=afe_mat).pack(side="left")
        ttk.Label(row_sel, text="CH").pack(side="left", padx=(8, 4))
        ttk.Spinbox(row_sel, from_=0, to=63, width=5, textvariable=afe_ch).pack(side="left")
        r0 += 1

        ttk.Button(afe, text="Enable ATP pulse for selected channel", command=_afe_enable_atp).grid(
            row=r0, column=0, columnspan=2, sticky="w", pady=(4, 2)
        )
        r0 += 1
        ttk.Checkbutton(
            afe,
            text="Enable HiTor on selected MAT (to route to SLVS HiTor)",
            variable=afe_hitor,
            command=_afe_apply_hitor,
        ).grid(row=r0, column=0, columnspan=2, sticky="w", pady=(4, 2))
        r0 += 1
        ttk.Button(afe, text="Reset test mode to TMR (disable HiTor everywhere)", command=_afe_reset_tmr).grid(
            row=r0, column=0, columnspan=2, sticky="w", pady=(6, 2)
        )

        # ----------------
        # TDC pulsing
        # ----------------
        tdc = ttk.Labelframe(root, text="TDC PULSING", padding=10)
        tdc.grid(row=0, column=1, sticky="nsew")
        tdc.columnconfigure(1, weight=1)

        pulse_src = tk.StringVar(value="0")
        tp_ta = tk.StringVar(value="0")
        tp_tot = tk.StringVar(value="0")
        tdc_mat = tk.StringVar(value="0")
        tdc_ch = tk.StringVar(value="0")

        src_labels = ["NONE", "Internal Pulse", "NONE", "External Pulse"]

        def _tdc_read() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return

            def do() -> dict[str, int]:
                self.hw.select_quadrant(q)
                return dict(self.hw.readTopTDCPulsing())

            out = self._with_hw(do, busy=f"Read TOP TDC pulsing (Q={q})")
            if not isinstance(out, dict):
                return
            try:
                pulse_src.set(str(int(out.get("pulsing_source", 0))))
                tp_ta.set(str(int(out.get("test_point_ta", 0))))
                tp_tot.set(str(int(out.get("test_point_tot", 0))))
                status_var.set("TDC pulsing settings read OK.")
            except Exception:
                pass

        def _tdc_apply() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return

            def do() -> None:
                self.hw.select_quadrant(q)
                self.hw.TopTDCPulsingSource(int(pulse_src.get()))
                self.hw.TopTDCTestPointTA(int(tp_ta.get()))
                self.hw.TopTDCTestPointTOT(int(tp_tot.get()))

            r = self._with_hw(do, busy=f"Apply TDC pulsing settings (Q={q})")
            if r is None:
                return
            status_var.set("Applied TDC pulsing settings.")

        def _tdc_enable_tdcpulse() -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return
            try:
                mid = int(str(tdc_mat.get()).strip())
                ch = int(str(tdc_ch.get()).strip())
            except Exception:
                status_var.set("TDC select channel: invalid MAT/CH")
                return

            def do() -> None:
                self.hw.TDCPulse(q, mattonella=mid, canale=ch)

            r = self._with_hw(do, busy=f"Enable TDC Pulse mode (Q={q} MAT={mid} CH={ch})")
            if r is None:
                return
            status_var.set(f"TDC Pulse mode enabled for MAT {mid} CH {ch}.")

        def _tdc_test_pulse(times: int) -> None:
            if self.offline:
                status_var.set("OFFLINE")
                return

            def do() -> None:
                self.hw.select_quadrant(q)
                self.hw.TopTDCTestPulse(times=int(times))

            r = self._with_hw(do, busy=f"TDC Test Pulse x{int(times)} (Q={q})")
            if r is None:
                return
            status_var.set(f"TDC Test Pulse done x{int(times)}.")

        rr = 0
        ttk.Label(tdc, text="Pulsing source").grid(row=rr, column=0, sticky="w", pady=2)
        src_cb = ttk.Combobox(tdc, state="readonly", width=22, values=[f"{i}: {s}" for i, s in enumerate(src_labels)])
        src_cb.grid(row=rr, column=1, sticky="w", pady=2)

        def _sync_src_to_var(_ev=None) -> None:
            try:
                pulse_src.set(str(int(src_cb.current())))
            except Exception:
                pass

        def _sync_var_to_src(*_a: object) -> None:
            try:
                idx = int(str(pulse_src.get()).strip())
            except Exception:
                idx = 0
            idx = max(0, min(3, idx))
            try:
                src_cb.current(idx)
            except Exception:
                pass

        src_cb.bind("<<ComboboxSelected>>", _sync_src_to_var)
        _sync_var_to_src()
        rr += 1
        ttk.Label(tdc, text="Test point TA").grid(row=rr, column=0, sticky="w", pady=2)
        ttk.Spinbox(tdc, from_=0, to=15, width=8, textvariable=tp_ta).grid(row=rr, column=1, sticky="w", pady=2)
        rr += 1
        ttk.Label(tdc, text="Test point TOT").grid(row=rr, column=0, sticky="w", pady=2)
        ttk.Spinbox(tdc, from_=0, to=31, width=8, textvariable=tp_tot).grid(row=rr, column=1, sticky="w", pady=2)
        rr += 1

        bar2 = ttk.Frame(tdc)
        bar2.grid(row=rr, column=0, columnspan=2, sticky="w", pady=(6, 10))
        ttk.Button(bar2, text="Read", command=_tdc_read).pack(side="left")
        ttk.Button(bar2, text="Apply", command=_tdc_apply).pack(side="left", padx=(8, 0))
        rr += 1

        ttk.Separator(tdc, orient="horizontal").grid(row=rr, column=0, columnspan=2, sticky="ew", pady=(8, 8))
        rr += 1

        ttk.Label(tdc, text="Select channel (MAT, CH)").grid(row=rr, column=0, sticky="w", pady=2)
        row_sel2 = ttk.Frame(tdc)
        row_sel2.grid(row=rr, column=1, sticky="w", pady=2)
        ttk.Spinbox(row_sel2, from_=0, to=15, width=5, textvariable=tdc_mat).pack(side="left")
        ttk.Label(row_sel2, text="CH").pack(side="left", padx=(8, 4))
        ttk.Spinbox(row_sel2, from_=0, to=63, width=5, textvariable=tdc_ch).pack(side="left")
        rr += 1

        ttk.Button(tdc, text="Enable TDC pulse for selected channel", command=_tdc_enable_tdcpulse).grid(
            row=rr, column=0, columnspan=2, sticky="w", pady=(4, 2)
        )
        rr += 1

        pulse_btns = ttk.Frame(tdc)
        pulse_btns.grid(row=rr, column=0, columnspan=2, sticky="w", pady=(8, 2))
        ttk.Label(pulse_btns, text="TDC Test Pulse").pack(side="left")
        ttk.Button(pulse_btns, text="x1", command=lambda: _tdc_test_pulse(1)).pack(side="left", padx=(10, 0))
        ttk.Button(pulse_btns, text="x10", command=lambda: _tdc_test_pulse(10)).pack(side="left", padx=(6, 0))
        ttk.Button(pulse_btns, text="x100", command=lambda: _tdc_test_pulse(100)).pack(side="left", padx=(6, 0))

        # -------------
        # FIFO monitor (embedded)
        # -------------
        fifo = ttk.Labelframe(root, text="FIFO monitor (I2C)", padding=10)
        fifo.grid(row=2, column=0, columnspan=2, sticky="nsew", pady=(10, 0))
        fifo.columnconfigure(0, weight=1)

        fifo_summary = tk.StringVar(value="—")
        ttk.Label(fifo, textvariable=fifo_summary, wraplength=1100, font=("Segoe UI", 9)).grid(row=0, column=0, sticky="w")

        out = tk.Text(fifo, height=12, width=120, wrap="none", state="disabled")
        yscroll = ttk.Scrollbar(fifo, orient="vertical", command=out.yview)
        out.configure(yscrollcommand=yscroll.set)
        out.grid(row=1, column=0, sticky="nsew", pady=(8, 0))
        yscroll.grid(row=1, column=1, sticky="ns", pady=(8, 0))

        btns = ttk.Frame(fifo)
        btns.grid(row=2, column=0, sticky="w", pady=(8, 0))
        auto_var = tk.BooleanVar(value=False)
        auto_ms = tk.StringVar(value="250")
        auto_after: list[Optional[str]] = [None]
        decoded_samples: list[dict[str, object]] = []

        def _fifo_clear() -> None:
            auto_var.set(False)
            try:
                if auto_after[0] is not None:
                    self.after_cancel(auto_after[0])
            except Exception:
                pass
            auto_after[0] = None
            decoded_samples.clear()
            out.configure(state="normal")
            out.delete("1.0", "end")
            out.configure(state="disabled")
            fifo_summary.set("Cleared (log + decoded samples).")

        def _fifo_read_one(decoded: bool) -> None:
            if self.offline:
                self._fifo_log(out, "OFFLINE")
                fifo_summary.set("OFFLINE")
                return

            def do() -> int:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(q)
                return int(self.hw.FifoReadSingle())

            w = self._with_hw(do, busy=f"FIFO read (Q={q})")
            if w is None:
                return
            w = int(w)
            if w == 0:
                self._fifo_log(out, "EMPTY")
                fifo_summary.set("EMPTY")
                return
            try:
                d0 = self._fifo_decode_word(w)
                if int(d0.get("fifo_empty", 0)) == 1 and int(d0.get("fifo_cnt", 0)) == 0:
                    self._fifo_log(out, "EMPTY(flag)")
                    fifo_summary.set("EMPTY(flag)")
                    return
            except Exception:
                pass
            if decoded:
                try:
                    dd = self._fifo_decode_word_tdc(w, quad=q)
                    decoded_samples.append(dd)
                    mat = int(dd.get("mat", 0) or 0)
                    ch = int(dd.get("channel", 0) or 0)
                    ta = dd.get("ta_ps")
                    tot = dd.get("tot_ps")
                    msg = f"MAT={mat:02d} CH={ch:02d}"
                    if isinstance(ta, float) and isinstance(tot, float):
                        msg += f"  TA={ta:.2f}ps TOT={tot:.2f}ps"
                    self._fifo_log(out, msg)
                    fifo_summary.set("HIT(decoded)")
                    return
                except Exception:
                    pass
            self._fifo_log(out, f"0x{w:016X}")
            fifo_summary.set("HIT(raw)")

        def _fifo_drain(decoded: bool) -> None:
            if self.offline:
                return

            def do() -> list[int]:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(q)
                return list(self.hw.FifoDrain(max_words=128))

            words = self._with_hw(do, busy=f"FIFO drain (Q={q})")
            if not isinstance(words, list):
                return
            if not words:
                self._fifo_log(out, "DRAIN: empty")
                fifo_summary.set("Drain empty")
                return
            n = 0
            for ww in words:
                n += 1
                if int(ww) == 0:
                    continue
                if decoded:
                    try:
                        dd = self._fifo_decode_word_tdc(int(ww), quad=q)
                        decoded_samples.append(dd)
                        mat = int(dd.get("mat", 0) or 0)
                        ch = int(dd.get("channel", 0) or 0)
                        ta = dd.get("ta_ps")
                        tot = dd.get("tot_ps")
                        msg = f"{n:03d}: MAT={mat:02d} CH={ch:02d}"
                        if isinstance(ta, float) and isinstance(tot, float):
                            msg += f"  TA={ta:.2f}ps TOT={tot:.2f}ps"
                        self._fifo_log(out, msg)
                        continue
                    except Exception:
                        pass
                self._fifo_log(out, f"{n:03d}: 0x{int(ww):016X}")
            fifo_summary.set(f"Drain: {len(words)} words")

        def _auto_tick() -> None:
            if not bool(auto_var.get()) or not win.winfo_exists():
                auto_after[0] = None
                return
            _fifo_read_one(decoded=True)
            try:
                ms = int(str(auto_ms.get()).strip() or "250")
            except Exception:
                ms = 250
            ms = max(20, min(5000, ms))
            auto_after[0] = self.after(ms, _auto_tick)

        def _toggle_auto() -> None:
            if bool(auto_var.get()):
                if auto_after[0] is None:
                    _auto_tick()
            else:
                try:
                    if auto_after[0] is not None:
                        self.after_cancel(auto_after[0])
                except Exception:
                    pass
                auto_after[0] = None

        ttk.Button(btns, text="Read (raw)", command=lambda: _fifo_read_one(decoded=False)).pack(side="left")
        ttk.Button(btns, text="Read (decoded)", command=lambda: _fifo_read_one(decoded=True)).pack(side="left", padx=(6, 0))
        ttk.Button(btns, text="Drain (decoded)", command=lambda: _fifo_drain(decoded=True)).pack(side="left", padx=(6, 0))
        ttk.Button(btns, text="Drain (raw)", command=lambda: _fifo_drain(decoded=False)).pack(side="left", padx=(6, 0))
        ttk.Button(btns, text="Clear", command=_fifo_clear).pack(side="left", padx=(10, 0))
        # Pass the live buffer (not a copy) so Analyze "Clear data"
        # actually clears what you will see next time too.
        ttk.Button(btns, text="Analyze…", command=lambda qq=str(q): self._open_fifo_analyze_popup(qq, decoded_samples)).pack(
            side="left", padx=(10, 0)
        )
        ttk.Checkbutton(btns, text="Auto", variable=auto_var, command=_toggle_auto).pack(side="left", padx=(10, 0))
        ttk.Label(btns, text="ms").pack(side="left", padx=(6, 2))
        ttk.Entry(btns, textvariable=auto_ms, width=6).pack(side="left")

        # Prime with a read so the user sees current settings.
        self.after(120, _afe_read)
        self.after(160, _tdc_read)

    def _make_block_diagram(
        self,
        parent_: ttk.Frame,
        block_id: int,
        *,
        compact: bool = False,
        quad_for_hw: Optional[str] = None,
        home_grid_tile: bool = False,
        fetch_stagger: Optional[int] = None,
        click_opens_quadrant: bool = False,
    ) -> ttk.Frame:
        mats = self.mapping.mats_in_block(block_id)
        owner = self.mapping.analog_owner_mat(block_id)
        kind = self.mapping.block_kind(block_id)

        def _quad_q_read() -> str:
            if quad_for_hw is not None:
                return str(quad_for_hw).strip().upper()
            return str(self.quad_var.get()).strip().upper()

        pad_outer = 1 if home_grid_tile else ((2, 2) if compact else 6)
        outer = ttk.Frame(parent_, padding=pad_outer)
        outer.configure(cursor="hand2")

        if home_grid_tile:
            title = ttk.Label(
                outer,
                text=f"B{block_id} · {kind}",
                font=("Segoe UI", 7, "bold"),
            )
        else:
            title = ttk.Label(
                outer,
                text=f"Block {block_id} ({kind})",
                font=(("Segoe UI", 9, "bold") if compact else ("Segoe UI", 10, "bold")),
            )
        title.pack(anchor="w")

        # Stylized diagram: responsive and always drawn inside a centered square.
        c = tk.Canvas(
            outer,
            highlightthickness=1,
            highlightbackground="#bdbdbd",
            bg="white",
        )
        _cpad = (1, 0) if home_grid_tile else ((2, 0) if compact else (6, 0))
        c.pack(pady=_cpad, fill="both", expand=True)

        mat_tl, mat_tr, mat_bl, mat_br = mats
        kind_u = str(kind).upper()
        an_fill = "#ffe8e8" if "NOLDO" in kind_u else "#e8fff0"

        mat_cache: dict[int, Optional[dict[str, object]]] = {mid: None for mid in mats}
        _fetch_gen: list[int] = [0]
        _fetch_after: list[Optional[str]] = [None]

        # Se abbiamo uno snapshot pre-riempito da FILE, usa subito quei valori
        # per rendere la vista block coerente senza leggere l'intero chip.
        if self._mat_snapshot_prefill_active and self._mat_snapshot_prefill_from_file:
            qkey_seed = _quad_q_read()
            cached_for_quad_seed = self._mat_snapshot_cache.get(qkey_seed, {})
            for mid in mats:
                if int(mid) in cached_for_quad_seed:
                    mat_cache[mid] = cached_for_quad_seed[int(mid)]

        def redraw_display(_ev=None) -> None:
            c.delete("all")
            w = int(c.winfo_width())
            h = int(c.winfo_height())
            if w <= 2 or h <= 2:
                return

            side = min(w, h)
            ox = int((w - side) / 2)
            oy = int((h - side) / 2)

            if home_grid_tile:
                pad = max(2, int(side * 0.018))
                gap = max(2, int(side * 0.012))
                an_w = max(12, int(side * 0.058))
                label_space = max(7, int(side * 0.032))
            elif compact:
                pad = max(4, int(side * 0.03))
                gap = max(4, int(side * 0.022))
                an_w = max(22, int(side * 0.09))
                label_space = max(12, int(side * 0.055))
            else:
                pad = max(12, int(side * 0.06))
                gap = max(10, int(side * 0.04))
                an_w = max(36, int(side * 0.12))
                label_space = max(18, int(side * 0.07))
            avail_h = side - label_space
            mat_w = int((side - 2 * pad - 2 * gap - an_w) / 2)
            mat_h = int((avail_h - 2 * pad - gap) / 2)
            an_h = mat_h * 2 + gap

            x0 = ox + pad
            y0 = oy + pad
            tl = (x0, y0, x0 + mat_w, y0 + mat_h)
            bl = (x0, y0 + mat_h + gap, x0 + mat_w, y0 + 2 * mat_h + gap)
            ax0 = x0 + mat_w + gap
            an = (ax0, y0, ax0 + an_w, y0 + an_h)
            rx0 = ax0 + an_w + gap
            tr = (rx0, y0, rx0 + mat_w, y0 + mat_h)
            br = (rx0, y0 + mat_h + gap, rx0 + mat_w, y0 + 2 * mat_h + gap)

            _fz = 0.72 if home_grid_tile else 1.0
            font_an = ("Segoe UI", max(6, int(side * 0.035 * _fz)), "bold")
            font_owner = ("Segoe UI", max(6, int(side * 0.03 * _fz)))
            font_mat_lbl = ("Segoe UI", max(5, int(side * 0.028 * _fz)), "bold")
            font_ft = ("Segoe UI", max(5, int(side * 0.021 * _fz)))

            def draw_mat(rect: tuple[float, float, float, float], mat_id: int) -> None:
                rx0_, ry0, rx1_, ry1 = rect
                rw = rx1_ - rx0_
                rh = ry1 - ry0
                ft_h = max(11.0, min(rh * 0.22, 28.0))
                gy1 = ry1 - ft_h
                outline_col = "#2f2f2f"
                entry = mat_cache.get(mat_id)
                if entry is None:
                    snap = self._mat_snapshot_cache.get(_quad_q_read(), {}).get(int(mat_id))
                    if isinstance(snap, dict):
                        entry = snap
                if self._mat_snapshot_prefill_active and self._mat_snapshot_prefill_from_file:
                    qkey = _quad_q_read()
                    cached = self._mat_snapshot_cache.get(qkey, {}).get(int(mat_id))
                    if isinstance(cached, dict):
                        entry = cached
                err_t = None
                if isinstance(entry, dict) and entry.get("_err"):
                    err_t = str(entry["_err"])
                    outline_col = "#c62828"

                c.create_rectangle(rx0_, ry0, rx1_, ry1, fill="#fafafa", outline=outline_col, width=2)
                c.create_text(
                    (rx0_ + rx1_) / 2,
                    ry0 + max(7.0, rh * 0.08),
                    text=f"MAT {mat_id}",
                    font=font_mat_lbl,
                    fill="#1f1f1f",
                )

                ix0 = rx0_ + 2
                iy0 = ry0 + max(11.0, rh * 0.14)
                ix1 = rx1_ - 2
                iy1 = max(iy0 + 4, gy1 - 2)
                gw = ix1 - ix0
                gh = iy1 - iy0
                if gw < 8 or gh < 8:
                    return

                pix_on: Optional[list[bool]] = None
                fe_on: Optional[list[bool]] = None
                ftdac: Optional[list[int]] = None
                if isinstance(entry, dict) and not entry.get("_err"):
                    po = entry.get("pix_on")
                    fo = entry.get("fe_on")
                    ft = entry.get("ftdac")
                    if isinstance(po, list) and isinstance(ft, list) and len(po) >= 64 and len(ft) >= 64:
                        pix_on = [bool(x) for x in po[:64]]
                        ftdac = [int(x) for x in ft[:64]]
                        if isinstance(fo, list) and len(fo) >= 64:
                            fe_on = [bool(x) for x in fo[:64]]

                cell_w = gw / 8.0
                cell_h = gh / 8.0
                cg = max(0.4, min(cell_w, cell_h) * 0.06)
                for row in range(8):
                    for col in range(8):
                        ch = row * 8 + col
                        cx0 = ix0 + col * cell_w + cg
                        cy0 = iy0 + row * cell_h + cg
                        cx1 = ix0 + (col + 1) * cell_w - cg
                        cy1 = iy0 + (row + 1) * cell_h - cg
                        if pix_on is not None:
                            # Match block-detail view: green=ON, red=OFF (user expects "spenti" = rosso).
                            if fe_on is not None and not fe_on[ch]:
                                fill = "#bdbdbd"
                            else:
                                fill = "#2e7d32" if pix_on[ch] else "#aa2222"
                            ol = "#37474f"
                        elif self.offline:
                            fill = "#eeeeee"
                            ol = "#90a4ae"
                        else:
                            fill = "#f5f5f5"
                            ol = "#b0bec5"
                        c.create_rectangle(cx0, cy0, cx1, cy1, fill=fill, outline=ol, width=1)

                ft_y = (gy1 + ry1) / 2
                if err_t:
                    ft_msg = "MAT read error"
                    ft_fill = "#b71c1c"
                elif self.offline:
                    ft_msg = "OFFLINE"
                    ft_fill = "#616161"
                elif ftdac is not None:
                    nd = sum(1 for x in ftdac if int(x) != 15)
                    if nd > 0:
                        ft_msg = "FTDAC calib. DONE!"
                        ft_fill = "#1565c0"
                    else:
                        ft_msg = "FTDAC: all FT=15"
                        ft_fill = "#757575"
                else:
                    ft_msg = "…"
                    ft_fill = "#9e9e9e"

                c.create_text((rx0_ + rx1_) / 2, ft_y, text=ft_msg, font=font_ft, fill=ft_fill)

            draw_mat(tl, mat_tl)
            draw_mat(tr, mat_tr)
            draw_mat(bl, mat_bl)
            draw_mat(br, mat_br)

            c.create_rectangle(*an, fill=an_fill, outline="#aa0000", width=2)
            c.create_text(
                (an[0] + an[2]) / 2,
                (an[1] + an[3]) / 2,
                text="ANALOG\nSERVICES",
                angle=90,
                font=font_an,
                fill="#7a0000",
                justify="center",
            )
            c.create_text(
                ox + side / 2,
                oy + side - (label_space / 2),
                text=f"owner MAT {owner}",
                font=font_owner,
                fill="#3a3a3a",
            )

        def schedule_fetch() -> None:
            if bool(getattr(self, "_snapshot_capture_in_progress", False)):
                return
            if (not bool(getattr(self, "_blocks_view_force_hw", False))) and self._mat_snapshot_prefill_active and self._mat_snapshot_prefill_from_file:
                qkey = _quad_q_read()
                cached_for_quad = self._mat_snapshot_cache.get(qkey, {})
                # Skip HW reads if we already have cached MAT data for this block.
                if all(int(mid) in cached_for_quad for mid in mats):
                    return
            if _fetch_after[0] is not None:
                try:
                    self.after_cancel(_fetch_after[0])
                except Exception:
                    pass
                _fetch_after[0] = None

            def fire() -> None:
                _fetch_after[0] = None
                if self.offline:
                    return
                gen = _fetch_gen[0] + 1
                _fetch_gen[0] = gen
                quad_q = _quad_q_read()

                def work() -> None:
                    try:
                        out: dict[int, dict[str, object]] = {}
                        for mid in mats:
                            # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
                            # Never touch them directly here (block 1 is MAT 4..7).
                            if 4 <= int(mid) <= 7:
                                continue
                            out[mid] = self.hw.readMatPixelsAndFTDAC(quad_q, mattonella=mid)

                        def apply_ok() -> None:
                            if gen != _fetch_gen[0]:
                                return
                            qk = _quad_q_read()
                            qc = self._mat_snapshot_cache.setdefault(qk, {})
                            for mid in mats:
                                if mid in out:
                                    mat_cache[mid] = out[mid]
                                    try:
                                        qc[int(mid)] = out[mid]
                                    except Exception:
                                        pass
                            redraw_display()

                        self.after(0, apply_ok)
                    except Exception as e:
                        err_s = str(e)

                        def apply_err() -> None:
                            if gen != _fetch_gen[0]:
                                return
                            for mid in mats:
                                mat_cache[mid] = {"_err": err_s}
                            redraw_display()

                        self.after(0, apply_err)

                threading.Thread(target=work, daemon=True).start()

            _st = int(fetch_stagger) if fetch_stagger is not None else int(block_id)
            _delay_ms = 100 + _st * 40
            _fetch_after[0] = self.after(_delay_ms, fire)

        def on_configure(_ev=None) -> None:
            redraw_display()
            schedule_fetch()

        c.bind("<Configure>", on_configure)
        redraw_display()
        schedule_fetch()

        # click: Blocks view → block detail; home summary strip → quadrant (FIFO / calib page)
        def go(_ev=None) -> None:
            qset = _quad_q_read()
            self.quad_var.set(qset)
            if click_opens_quadrant:
                self._open_quadrant(qset)
            else:
                self._open_block(block_id)

        outer.bind("<Button-1>", go)
        title.bind("<Button-1>", go)
        c.bind("<Button-1>", go)
        outer.bind("<Return>", go)

        return outer

    def _open_quadrant(self, q: str) -> None:
        self.quad_var.set(str(q).strip().upper())
        self._push_view(self._build_blocks_view(self.content))

    def _build_blocks_view(self, parent: ttk.Frame) -> ttk.Frame:
        frm = ttk.Frame(parent)
        quad = self.quad_var.get()
        ttk.Label(frm, text=f"Quadrant {quad} → Blocks", font=("Segoe UI", 16, "bold")).pack(anchor="w", pady=(0, 4))
        ttk.Label(
            frm,
            text="Navigate to a block · MAT 8×8 = channel ON (green) / OFF (gray) · below: FTDAC status · FIFO →",
            font=("Segoe UI", 9),
            foreground="#424242",
        ).pack(anchor="w", pady=(0, 10))

        top = ttk.Frame(frm)
        top.pack(fill="both", expand=True)

        def refresh_blocks_hw() -> None:
            try:
                self._blocks_view_force_hw = True
                # Read full snapshot from HW for the quadrant (best-effort for MAT 4..7).
                self._quadrant_full_refresh(self.quad_var.get())
                cb = getattr(self, "_blocks_view_refresh_cb", None)
                if callable(cb):
                    cb()
            finally:
                self._blocks_view_force_hw = False

        # IREF is a quadrant-level setting (external DAC). Place it here (overview page).
        try:
            self._iref_sync_for_quad(quad)
        except Exception:
            pass
        iref_bar = ttk.Frame(top)
        iref_bar.pack(fill="x", pady=(0, 8))
        self._btn_save_quad_cfg = ttk.Button(iref_bar, text="Save config…", command=self._save_quadrant_full_config)
        self._btn_save_quad_cfg.pack(side="right", padx=(0, 6))
        ttk.Button(iref_bar, text="Refresh", command=refresh_blocks_hw).pack(side="right", padx=(0, 6))
        ttk.Button(iref_bar, text="Check Calibration", command=lambda q=str(self.quad_var.get()).strip().upper(): self._check_calibration(q)).pack(
            side="right", padx=(0, 6)
        )
        iref = ttk.Labelframe(iref_bar, text=f"IREF (mV) — quad {str(quad).strip().upper()}", padding=(6, 4))
        iref.pack(side="left")
        ttk.Button(iref, text="Read", command=lambda q=quad: self._iref_read(q)).pack(side="left")
        iref_entry = ttk.Entry(iref, width=10, textvariable=self._iref_var)
        iref_entry.pack(side="left", padx=(6, 6))
        ttk.Button(iref, text="Set", command=lambda q=quad, e=iref_entry: self._iref_set(q, e.get())).pack(side="left")
        ttk.Label(iref, textvariable=self._iref_meas_var).pack(side="left", padx=(10, 0))

        # Quadrant-wide ALL toggles (persist per quadrant).
        qkey = str(quad).strip().upper()
        if qkey not in self._quad_all_vars:
            self._quad_all_vars[qkey] = {
                "fe": tk.BooleanVar(value=False),
                "px": tk.BooleanVar(value=False),
                "td": tk.BooleanVar(value=False),
            }
        q_fe = self._quad_all_vars[qkey]["fe"]
        q_px = self._quad_all_vars[qkey]["px"]
        q_td = self._quad_all_vars[qkey]["td"]

        def apply_quad_all(*, feon: Optional[bool] = None, pixon: Optional[bool] = None, tdcon: Optional[bool] = None) -> None:
            q = str(self.quad_var.get()).strip().upper()

            def do() -> None:
                self.hw.select_quadrant(q)
                # Small delay helps reliability on some setups (mirrors C# scattered Task.Delay(1..3)).
                try:
                    per_write_delay_s = float(os.environ.get("IGNITE64_I2C_DELAY_S", "0.001").strip())
                except Exception:
                    per_write_delay_s = 0.001
                if per_write_delay_s < 0:
                    per_write_delay_s = 0.0
                # Broadcast writes (C# uses MAT ID 254) within the selected quadrant.
                bdev = 254
                # For broadcast read-modify-write, mimic C# behavior: read "base" values from a safe MAT
                # (broadcast devices often don't support readback directly).
                base_mid = 0
                if tdcon is not None:
                    reg = 64
                    old = int(self.hw.i2c_read_byte(self.hw.matid_to_devaddr(base_mid), reg))
                    new = (old & ~0x40) | (0x40 if bool(tdcon) else 0x00)
                    self.hw.i2c_write_byte(bdev, reg, int(new) & 0xFF)
                    if per_write_delay_s:
                        time.sleep(per_write_delay_s)
                if feon is not None or pixon is not None:
                    for ch in range(64):
                        old = int(self.hw.i2c_read_byte(self.hw.matid_to_devaddr(base_mid), int(ch)))
                        new = old
                        if feon is not None:
                            new = (new & ~0x80) | (0x80 if bool(feon) else 0x00)
                        if pixon is not None:
                            new = (new & ~0x40) | (0x40 if bool(pixon) else 0x00)
                        self.hw.i2c_write_byte(bdev, int(ch), int(new) & 0xFF)
                        if per_write_delay_s:
                            time.sleep(per_write_delay_s)

                # Best-effort verify broadcast succeeded by reading back from safe MATs.
                # We can't read MAT 4..7, but broadcast should also affect MAT0/8/etc.
                try:
                    verify_mats = [0, 8, 12]
                    for vm in verify_mats:
                        if 4 <= int(vm) <= 7:
                            continue
                        vdev = self.hw.matid_to_devaddr(int(vm))
                        if tdcon is not None:
                            vv = int(self.hw.i2c_read_byte(vdev, 64))
                            want = 1 if bool(tdcon) else 0
                            got = 1 if (vv & 0x40) else 0
                            if got != want:
                                _dbg(f"broadcast verify mismatch: MAT{vm} reg64 TDCON got={got} want={want} vv=0x{vv:02X}")
                        if feon is not None or pixon is not None:
                            for vch in (0, 1, 2, 31, 32, 63):
                                vv = int(self.hw.i2c_read_byte(vdev, int(vch)))
                                if feon is not None:
                                    want = 1 if bool(feon) else 0
                                    got = 1 if (vv & 0x80) else 0
                                    if got != want:
                                        _dbg(
                                            f"broadcast verify mismatch: MAT{vm} ch{vch} FEON got={got} want={want} vv=0x{vv:02X}"
                                        )
                                if pixon is not None:
                                    want = 1 if bool(pixon) else 0
                                    got = 1 if (vv & 0x40) else 0
                                    if got != want:
                                        _dbg(
                                            f"broadcast verify mismatch: MAT{vm} ch{vch} PIXON got={got} want={want} vv=0x{vv:02X}"
                                        )
                except Exception:
                    pass

            self._with_hw(do, busy=f"Apply ALL (quad={q})")
            # Update GUI cache for all MATs (including 4..7 which we never read directly).
            # This makes quadrant colors reflect broadcast actions immediately.
            try:
                qk = str(q).strip().upper()
                qc = self._mat_snapshot_cache.setdefault(qk, {})
                for mid in range(16):
                    ent = qc.get(int(mid))
                    if not isinstance(ent, dict):
                        ent = {}
                        qc[int(mid)] = ent
                    po = ent.get("pix_on")
                    fo = ent.get("fe_on")
                    if not isinstance(po, list) or len(po) < 64:
                        po = [False] * 64
                    if not isinstance(fo, list) or len(fo) < 64:
                        fo = [True] * 64
                    if pixon is not None:
                        po = [bool(pixon)] * 64
                    if feon is not None:
                        fo = [bool(feon)] * 64
                    ent["pix_on"] = po
                    ent["fe_on"] = fo
            except Exception:
                pass

            # Drop file-prefill mode and force refresh of block widgets.
            self._mat_snapshot_prefill_active = False
            try:
                self._blocks_view_force_hw = True
                # After broadcast, do a full refresh to keep quadrant view equivalent to C#.
                self._quadrant_full_refresh(q)
                cb = getattr(self, "_blocks_view_refresh_cb", None)
                if callable(cb):
                    cb()
            finally:
                self._blocks_view_force_hw = False

        all_bar = ttk.Labelframe(iref_bar, text="ALL (quadrant)", padding=(6, 4))
        all_bar.pack(side="left", padx=(12, 0))
        ttk.Checkbutton(all_bar, text="FEON ALL", variable=q_fe, command=lambda: apply_quad_all(feon=bool(q_fe.get()))).pack(
            side="left"
        )
        ttk.Checkbutton(all_bar, text="PIXON ALL", variable=q_px, command=lambda: apply_quad_all(pixon=bool(q_px.get()))).pack(
            side="left", padx=(8, 0)
        )
        ttk.Checkbutton(all_bar, text="TDCON ALL", variable=q_td, command=lambda: apply_quad_all(tdcon=bool(q_td.get()))).pack(
            side="left", padx=(8, 0)
        )
        ttk.Button(
            iref_bar,
            text="PULSE SECTION",
            command=lambda q=str(quad).strip().upper(): self._open_pulsing_window(q),
            width=16,
        ).pack(side="left", padx=(12, 0), pady=(18, 0))

        grid = ttk.Frame(top)
        grid.pack(side="left", fill="both", expand=True)

        for block_id in range(4):
            w = self._make_block_diagram(grid, block_id, compact=False)
            r, c = divmod(block_id, 2)
            w.grid(row=r, column=c, padx=12, pady=12, sticky="nsew")
            grid.grid_columnconfigure(c, weight=1)
            grid.grid_rowconfigure(r, weight=1)

        # Expose a refresh callback for the current blocks view (used after broadcast ops).
        def _refresh_blocks_view() -> None:
            # Trigger a fetch+redraw on next idle tick.
            for child in grid.winfo_children():
                try:
                    child.event_generate("<Configure>")
                except Exception:
                    pass

        self._blocks_view_refresh_cb = _refresh_blocks_view

        # FIFO panel (monitoring / debug)
        fifo = ttk.Labelframe(top, text="Quadrant FIFO (I2C) — monitor", padding=8)
        fifo.pack(side="right", fill="both", expand=False, padx=(10, 0))

        fifo_summary_var = tk.StringVar(
            value="Monitor FIFO: — (esegui Read o attiva Auto; richiede readout I2C su TOP)"
        )
        ttk.Label(fifo, textvariable=fifo_summary_var, wraplength=400, font=("Segoe UI", 9)).pack(
            anchor="w", pady=(0, 8)
        )

        btns = ttk.Frame(fifo)
        btns.pack(fill="x", pady=(0, 6))

        btns2 = ttk.Frame(fifo)
        btns2.pack(fill="x", pady=(0, 6))

        out = tk.Text(fifo, height=18, width=52, wrap="none", state="disabled")
        yscroll = ttk.Scrollbar(fifo, orient="vertical", command=out.yview)
        out.configure(yscrollcommand=yscroll.set)
        out.pack(side="left", fill="both", expand=True)
        yscroll.pack(side="right", fill="y")

        def clear_out() -> None:
            # Stop auto mode when clearing output.
            self._fifo_auto_stop()
            decoded_samples.clear()
            out.configure(state="normal")
            out.delete("1.0", "end")
            out.configure(state="disabled")
            fifo_summary_var.set("Cleared (log + decoded samples).")

        decoded_samples: list[dict[str, object]] = []

        def read_one(decoded: bool) -> None:
            quad2 = self.quad_var.get()

            def do() -> int:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(quad2)
                return int(self.hw.FifoReadSingle())

            w = self._with_hw(do, busy=f"FIFO read (Q={quad2})")
            if w is None:
                return
            w = int(w)
            if w == 0:
                self._fifo_log(out, "EMPTY")
                fifo_summary_var.set(f"Ultimo read Q={quad2}: FIFO vuota (EMPTY)")
                return
            # Some firmwares return the last non-empty word even when FIFO is empty.
            # Trust the embedded FIFO-empty flag (bit47) over the raw 64-bit value.
            try:
                d0 = self._fifo_decode_word(w)
                if int(d0.get("fifo_empty", 0)) == 1 and int(d0.get("fifo_cnt", 0)) == 0:
                    self._fifo_log(out, "EMPTY")
                    fifo_summary_var.set(f"Ultimo read Q={quad2}: FIFO vuota (empty flag)")
                    return
            except Exception:
                d0 = {}
            if not decoded:
                self._fifo_log(out, f"0x{w:016X}")
                fifo_summary_var.set(f"Ultimo read Q={quad2}: word raw 0x{w:016X}")
                return
            dd = self._fifo_decode_word_tdc(w, quad=str(quad2).strip().upper())
            decoded_samples.append(dd)
            mat = int(dd.get("mat", 0) or 0)
            ch = int(dd.get("channel", 0) or 0)
            msg = (
                f"MAT={mat:2d}  CH={ch:2d}  "
                f"cnt={int(dd.get('fifo_cnt', 0) or 0):3d} empty={int(dd.get('fifo_empty', 0) or 0)} "
                f"full={int(dd.get('fifo_full', 0) or 0)} half={int(dd.get('fifo_halffull', 0) or 0)}"
            )
            ta = dd.get("ta_ps")
            tot = dd.get("tot_ps")
            if isinstance(ta, float) and isinstance(tot, float):
                msg += f"  TA={ta:.2f}ps TOT={tot:.2f}ps"
            self._fifo_log(out, msg)
            fifo_summary_var.set(f"Ultimo Q={quad2}: MAT={mat:02d} CH={ch:02d} cnt={int(dd.get('fifo_cnt', 0) or 0)}")

        def drain(decoded: bool) -> None:
            quad2 = self.quad_var.get()

            def do() -> list[int]:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(quad2)
                return list(self.hw.FifoDrain(max_words=128))

            ws = self._with_hw(do, busy=f"FIFO drain (Q={quad2})")
            if ws is None:
                return
            words = [int(x) for x in ws]  # type: ignore[arg-type]
            if not words:
                self._fifo_log(out, "DRAIN: empty")
                fifo_summary_var.set(f"Drain Q={quad2}: FIFO vuota")
                return
            # Stop on first "empty-flag" word to avoid printing a repeated stale word.
            n_eff = 0
            for w in words:
                if int(w) == 0:
                    break
                try:
                    dchk = self._fifo_decode_word(int(w))
                    if int(dchk.get("fifo_empty", 0)) == 1 and int(dchk.get("fifo_cnt", 0)) == 0:
                        break
                except Exception:
                    pass
                n_eff += 1
            self._fifo_log(out, f"DRAIN: {n_eff} words")
            fifo_summary_var.set(f"Drain Q={quad2}: {n_eff} parole lette")
            for w in words[:n_eff]:
                if not decoded:
                    self._fifo_log(out, f"0x{w:016X}")
                else:
                    dd = self._fifo_decode_word_tdc(w, quad=str(quad2).strip().upper())
                    decoded_samples.append(dd)
                    mat = int(dd.get("mat", 0) or 0)
                    ch = int(dd.get("channel", 0) or 0)
                    msg = (
                        f"MAT={mat:2d}  CH={ch:2d}  "
                        f"cnt={int(dd.get('fifo_cnt', 0) or 0):3d} empty={int(dd.get('fifo_empty', 0) or 0)} "
                        f"full={int(dd.get('fifo_full', 0) or 0)} half={int(dd.get('fifo_halffull', 0) or 0)}"
                    )
                    ta = dd.get("ta_ps")
                    tot = dd.get("tot_ps")
                    if isinstance(ta, float) and isinstance(tot, float):
                        msg += f"  TA={ta:.2f}ps TOT={tot:.2f}ps"
                    self._fifo_log(out, msg)

        ttk.Button(btns, text="Read 1 (raw)", command=lambda: read_one(False)).pack(side="left", padx=3)
        ttk.Button(btns, text="Read 1 (decoded)", command=lambda: read_one(True)).pack(side="left", padx=3)
        ttk.Button(btns, text="Drain (decoded)", command=lambda: drain(True)).pack(side="left", padx=3)
        ttk.Button(
            btns,
            text="Analyze…",
            # Pass the live buffer (not a copy) so Analyze "Clear data"
            # actually clears what you will see next time too.
            command=lambda q=str(self.quad_var.get()).strip().upper(): self._open_fifo_analyze_popup(q, decoded_samples),
        ).pack(side="left", padx=(10, 0))
        ttk.Button(btns, text="Clear", command=clear_out).pack(side="right", padx=3)

        ttk.Button(btns2, text="Drain (raw)", command=lambda: drain(False)).pack(side="left", padx=3)
        auto_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(btns2, text="Auto", variable=auto_var).pack(side="left", padx=(10, 3))
        ttk.Label(btns2, text="ms:").pack(side="left")
        auto_ms = tk.StringVar(value="200")
        auto_ms_entry = ttk.Entry(btns2, textvariable=auto_ms, width=6)
        auto_ms_entry.pack(side="left", padx=(3, 3))

        def auto_tick() -> None:
            if not bool(auto_var.get()):
                self._fifo_auto_after_id = None
                return
            # One decoded read per tick; if empty prints "EMPTY" (useful heartbeat).
            read_one(True)
            try:
                delay = int(str(auto_ms.get()).strip())
            except Exception:
                delay = 200
            if delay < 20:
                delay = 20
            self._fifo_auto_after_id = self.after(delay, auto_tick)

        def auto_toggle() -> None:
            if bool(auto_var.get()):
                # start
                self._fifo_auto_stop()
                auto_tick()
            else:
                self._fifo_auto_stop()

        # Ensure checkbox starts/stops the loop.
        auto_var.trace_add("write", lambda *_args: auto_toggle())

        return frm

    def _open_block(self, block_id: int) -> None:
        self._push_view(self._build_block_view(self.content, block_id))

    def _build_block_view(self, parent: ttk.Frame, block_id: int) -> ttk.Frame:
        frm = ttk.Frame(parent)
        quad = self.quad_var.get()
        mats = self.mapping.mats_in_block(block_id)
        kind = self.mapping.block_kind(block_id)
        # mats order is TL, TR, BL, BR
        mat_tl, mat_tr, mat_bl, mat_br = mats
        ttk.Label(frm, text=f"Quadrant {quad} → Block {block_id} ({kind})", font=("Segoe UI", 16, "bold")).pack(
            anchor="w", pady=(0, 10)
        )

        toolbar = ttk.Frame(frm)
        toolbar.pack(fill="x", pady=(0, 8))
        ttk.Button(toolbar, text="Refresh block", command=lambda: self._refresh_block(block_id)).pack(side="left", padx=6)
        ttk.Button(
            toolbar,
            text="FIFO…",
            command=lambda q=str(self.quad_var.get()).strip().upper(): self._open_fifo_popup(q),
        ).pack(side="left", padx=(6, 0))
        ttk.Button(
            toolbar,
            text="Calibrate channels…",
            command=lambda bid=block_id: self._calib_block_threshold(bid),
        ).pack(side="left", padx=(6, 0))

        # --- FTDAC ALL (set all 64 channels to same code) ---
        ttk.Separator(toolbar, orient="vertical").pack(side="left", fill="y", padx=10)
        ttk.Label(toolbar, text="FTDAC ALL").pack(side="left")
        ftdac_all_sb = tk.Spinbox(toolbar, from_=0, to=15, width=4)
        ftdac_all_sb.delete(0, "end")
        ftdac_all_sb.insert(0, "15")
        ftdac_all_sb.pack(side="left", padx=(6, 6))

        def _apply_ftdac_all() -> None:
            q = str(self.quad_var.get()).strip().upper()
            try:
                code = int(str(ftdac_all_sb.get()).strip())
            except Exception:
                self._set_status("FTDAC ALL: inserire un numero 0..15")
                return
            if code < 0 or code > 15:
                self._set_status("FTDAC ALL: inserire un numero 0..15")
                return

            targets = [int(x) for x in mats]
            if self.offline:
                self._set_status("FTDAC ALL: offline")
                return
            if self._bulk_all_in_progress:
                self._set_status("Busy: another ALL operation is running…")
                return

            busy = f"FTDAC ALL={code} (Q={q} block={int(block_id)})"
            self._set_status(busy)
            self._bulk_all_in_progress = True

            def _optimistic_ui_update() -> None:
                # Update snapshot cache + visible cells (no HW reads).
                try:
                    qc = self._mat_snapshot_cache.setdefault(q, {})
                    for mid in targets:
                        ent = qc.setdefault(int(mid), {})
                        ent["ftdac"] = [int(code)] * 64
                except Exception:
                    pass
                # Update any visible per-pixel labels in the current block view.
                for mid in targets:
                    if 4 <= int(mid) <= 7:
                        continue
                    for ch in range(64):
                        try:
                            self._update_ftdac_cell(int(mid), int(ch), int(code))
                        except Exception:
                            pass

            def work() -> None:
                err_s: Optional[str] = None
                try:
                    with self._hw_seq_lock:
                        self.hw.select_quadrant(q)
                        for mid in targets:
                            # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
                            if 4 <= int(mid) <= 7:
                                continue
                            for ch in range(64):
                                self.hw.AnalogChannelFineTune(
                                    q, block=0, mattonella=int(mid), canale=int(ch), valore=int(code)
                                )
                except Exception as e:
                    err_s = str(e)
                    _dbg(f"FTDAC ALL failed: {e!r}")

                def apply() -> None:
                    self._bulk_all_in_progress = False
                    if err_s:
                        self._set_status(f"FTDAC ALL: ERROR — {err_s}")
                        return
                    _optimistic_ui_update()
                    self._set_status(f"FTDAC ALL set to {code} (block {int(block_id)})")

                self.after(0, apply)

            threading.Thread(target=work, daemon=True).start()

        ttk.Button(toolbar, text="Apply", command=_apply_ftdac_all).pack(side="left")
        # ALL toggles for this block
        fe_all = tk.BooleanVar(value=False)
        px_all = tk.BooleanVar(value=False)
        td_all = tk.BooleanVar(value=False)
        fe_btn = ttk.Checkbutton(
            toolbar,
            text="FEON ALL",
            variable=fe_all,
            command=lambda: None,
        )
        px_btn = ttk.Checkbutton(
            toolbar,
            text="PIXON ALL",
            variable=px_all,
            command=lambda: None,
        )
        td_btn = ttk.Checkbutton(
            toolbar,
            text="TDCON ALL",
            variable=td_all,
            command=lambda: None,
        )

        controls = [fe_btn, px_btn, td_btn]
        fe_btn.configure(
            command=lambda: self._block_all_toggle(
                block_id,
                what="FEON ALL",
                var=fe_all,
                controls=controls,
                feon=bool(fe_all.get()),
            )
        )
        px_btn.configure(
            command=lambda: self._block_all_toggle(
                block_id,
                what="PIXON ALL",
                var=px_all,
                controls=controls,
                pixon=bool(px_all.get()),
            )
        )
        td_btn.configure(
            command=lambda: self._block_all_toggle(
                block_id,
                what="TDCON ALL",
                var=td_all,
                controls=controls,
                tdcon=bool(td_all.get()),
            )
        )

        fe_btn.pack(side="left", padx=(10, 0))
        px_btn.pack(side="left", padx=(6, 0))
        td_btn.pack(side="left", padx=(6, 0))
        ttk.Button(
            toolbar,
            text="PULSE SECTION",
            command=lambda q=str(self.quad_var.get()).strip().upper(): self._open_pulsing_window(q),
        ).pack(side="left", padx=(12, 0))

        ttk.Separator(frm).pack(fill="x", pady=8)

        # Grid layout:
        # MAT_TL | ANALOG (rowspan=2) | MAT_TR
        # MAT_BL | ANALOG            | MAT_BR
        main = ttk.Frame(frm)
        main.pack(fill="both", expand=True)
        main.grid_columnconfigure(0, weight=1, uniform="col")
        # Analog column: LP needs extra width for AFE current spinboxes.
        main.grid_columnconfigure(1, weight=0, minsize=280)
        main.grid_columnconfigure(2, weight=1, uniform="col")
        main.grid_rowconfigure(0, weight=1, uniform="row")
        main.grid_rowconfigure(1, weight=1, uniform="row")

        # Store per-block pixel canvas item ids (MAT id -> list of 64 rectangle ids)
        self._block_pix_cells: dict[int, list[int]] = {}
        self._block_pix_canvas: dict[int, tk.Canvas] = {}
        self._block_pix_txt_ids: dict[int, list[int]] = {}

        matfrm_tl = self._build_mat_mini(main, mat_tl, title=f"MAT {mat_tl}")
        matfrm_tr = self._build_mat_mini(main, mat_tr, title=f"MAT {mat_tr}")
        matfrm_bl = self._build_mat_mini(main, mat_bl, title=f"MAT {mat_bl}")
        matfrm_br = self._build_mat_mini(main, mat_br, title=f"MAT {mat_br}")

        matfrm_tl.grid(row=0, column=0, sticky="nsew", padx=(0, 8), pady=(0, 8))
        matfrm_tr.grid(row=0, column=2, sticky="nsew", padx=(8, 0), pady=(0, 8))
        matfrm_bl.grid(row=1, column=0, sticky="nsew", padx=(0, 8), pady=(8, 0))
        matfrm_br.grid(row=1, column=2, sticky="nsew", padx=(8, 0), pady=(8, 0))

        analog = self._build_analog_mini(main, block_id, kind=kind)
        analog.grid(row=0, column=1, rowspan=2, sticky="nsew", padx=4, pady=0)

        # Manual mode: do not auto-read on page open. Use "Refresh block" button.
        return frm

    def _open_fifo_popup(self, quad: str) -> None:
        """
        Quick FIFO monitor popup, usable from Block view (so you don't need to go back to Quadrant view).
        """
        q = str(quad).strip().upper()
        win = tk.Toplevel(self)
        win.title(f"FIFO monitor — Q={q}")
        win.transient(self)
        win.resizable(True, True)

        root = ttk.Frame(win, padding=10)
        root.pack(fill="both", expand=True)

        summary = tk.StringVar(value=f"FIFO: — (Q={q})")
        ttk.Label(root, textvariable=summary, wraplength=520, font=("Segoe UI", 9)).pack(anchor="w", pady=(0, 8))

        btns = ttk.Frame(root)
        btns.pack(fill="x", pady=(0, 6))
        btns2 = ttk.Frame(root)
        btns2.pack(fill="x", pady=(0, 6))

        out = tk.Text(root, height=18, width=72, wrap="none", state="disabled")
        yscroll = ttk.Scrollbar(root, orient="vertical", command=out.yview)
        out.configure(yscrollcommand=yscroll.set)
        out.pack(side="left", fill="both", expand=True)
        yscroll.pack(side="right", fill="y")

        decoded_samples: list[dict[str, object]] = []

        def read_one(decoded: bool) -> None:
            def do() -> int:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(q)
                return int(self.hw.FifoReadSingle())

            w = self._with_hw(do, busy=f"FIFO read (Q={q})")
            if w is None:
                return
            w = int(w)
            if w == 0:
                self._fifo_log(out, "EMPTY")
                summary.set(f"Q={q}: EMPTY")
                return
            try:
                d0 = self._fifo_decode_word(w)
                if int(d0.get("fifo_empty", 0)) == 1 and int(d0.get("fifo_cnt", 0)) == 0:
                    self._fifo_log(out, "EMPTY")
                    summary.set(f"Q={q}: EMPTY (flag)")
                    return
            except Exception:
                d0 = {}
            if not decoded:
                self._fifo_log(out, f"0x{w:016X}")
                summary.set(f"Q={q}: raw 0x{w:016X}")
                return
            dd = self._fifo_decode_word_tdc(w, quad=q)
            decoded_samples.append(dd)
            mat = int(dd.get("mat", 0) or 0)
            ch = int(dd.get("channel", 0) or 0)
            cal_mode = dd.get("cal_mode")
            pix = dd.get("pix")
            lsb = dd.get("lsb_ps")
            ta = dd.get("ta_ps")
            tot = dd.get("tot_ps")
            msg = (
                f"MAT={mat:2d}  CH={ch:2d}  "
                f"cnt={int(dd.get('fifo_cnt', 0) or 0):3d} empty={int(dd.get('fifo_empty', 0) or 0)} "
                f"full={int(dd.get('fifo_full', 0) or 0)} half={int(dd.get('fifo_halffull', 0) or 0)}"
            )
            if isinstance(cal_mode, int) and isinstance(pix, int):
                msg += f"  cal={cal_mode} pix={pix:2d}"
            if isinstance(lsb, float) and lsb > 0.0:
                msg += f"  LSB={lsb:.2f}ps"
            if isinstance(ta, float) and isinstance(tot, float):
                msg += f"  TA={ta:.2f}ps TOT={tot:.2f}ps"
            self._fifo_log(out, msg)
            summary.set(f"Q={q}: MAT={mat:02d} CH={ch:02d} cnt={int(dd.get('fifo_cnt', 0) or 0)}")

        def drain(decoded: bool) -> None:
            def do() -> list[int]:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(q)
                return list(self.hw.FifoDrain(max_words=128))

            ws = self._with_hw(do, busy=f"FIFO drain (Q={q})")
            if ws is None:
                return
            words = [int(x) for x in ws]  # type: ignore[arg-type]
            n_eff = 0
            for w in words:
                if int(w) == 0:
                    break
                try:
                    dchk = self._fifo_decode_word(int(w))
                    if int(dchk.get("fifo_empty", 0)) == 1 and int(dchk.get("fifo_cnt", 0)) == 0:
                        break
                except Exception:
                    pass
                n_eff += 1
            self._fifo_log(out, f"DRAIN: {n_eff} words")
            summary.set(f"Q={q}: DRAIN {n_eff} words")
            for w in words[:n_eff]:
                if not decoded:
                    self._fifo_log(out, f"0x{w:016X}")
                else:
                    dd = self._fifo_decode_word_tdc(w, quad=q)
                    decoded_samples.append(dd)
                    mat = int(dd.get("mat", 0) or 0)
                    ch = int(dd.get("channel", 0) or 0)
                    cal_mode = dd.get("cal_mode")
                    pix = dd.get("pix")
                    lsb = dd.get("lsb_ps")
                    ta = dd.get("ta_ps")
                    tot = dd.get("tot_ps")
                    msg = (
                        f"MAT={mat:2d}  CH={ch:2d}  "
                        f"cnt={int(dd.get('fifo_cnt', 0) or 0):3d} empty={int(dd.get('fifo_empty', 0) or 0)} "
                        f"full={int(dd.get('fifo_full', 0) or 0)} half={int(dd.get('fifo_halffull', 0) or 0)}"
                    )
                    if isinstance(cal_mode, int) and isinstance(pix, int):
                        msg += f"  cal={cal_mode} pix={pix:2d}"
                    if isinstance(lsb, float) and lsb > 0.0:
                        msg += f"  LSB={lsb:.2f}ps"
                    if isinstance(ta, float) and isinstance(tot, float):
                        msg += f"  TA={ta:.2f}ps TOT={tot:.2f}ps"
                    self._fifo_log(out, msg)

        ttk.Button(btns, text="Read 1 (decoded)", command=lambda: read_one(True)).pack(side="left", padx=3)
        ttk.Button(btns, text="Read 1 (raw)", command=lambda: read_one(False)).pack(side="left", padx=3)
        ttk.Button(btns, text="Drain (decoded)", command=lambda: drain(True)).pack(side="left", padx=3)
        ttk.Button(btns, text="Drain (raw)", command=lambda: drain(False)).pack(side="left", padx=3)
        ttk.Button(
            btns,
            text="Analyze…",
            # Pass the live buffer (not a copy) so Analyze "Clear data"
            # actually clears what you will see next time too.
            command=lambda qq=str(q).strip().upper(): self._open_fifo_analyze_popup(qq, decoded_samples),
        ).pack(side="left", padx=(10, 0))

        auto_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(btns2, text="Auto", variable=auto_var).pack(side="left", padx=(0, 6))
        ttk.Label(btns2, text="ms:").pack(side="left")
        auto_ms = tk.StringVar(value="200")
        ttk.Entry(btns2, textvariable=auto_ms, width=6).pack(side="left", padx=(4, 0))

        def auto_tick() -> None:
            if not bool(auto_var.get()):
                return
            read_one(True)
            try:
                ms = int(str(auto_ms.get()).strip())
            except Exception:
                ms = 200
            if ms < 20:
                ms = 20
            win.after(int(ms), auto_tick)

        auto_var.trace_add("write", lambda *_a: (auto_tick() if bool(auto_var.get()) else None))

    def _calib_block_threshold(self, block_id: int) -> None:
        """
        Experimental: calibrate threshold for all channels in a block, sequentially,
        using the *same* single-channel calibration routine.

        For each channel:
        - enable only that channel (PIXON + FEON) and enable TDCON for that MAT
        - start from FTCODE=15
        - sweep down until first FIFO hit, then set previous code
        """
        quad = str(self.quad_var.get()).strip().upper()
        mats = self.mapping.mats_in_block(int(block_id))

        win = tk.Toplevel(self)
        win.title(f"Threshold calibration — Block {block_id} (Q={quad})")
        win.transient(self)
        win.resizable(True, True)

        root = ttk.Frame(win, padding=10)
        root.pack(fill="both", expand=True)

        ttk.Label(
            root,
            text=(
                "Block calibration: calibrate channel-by-channel via FIFO.\n"
                "For each channel enable PIXON+FEON (that channel only) and TDCON for the MAT.\n"
                "Note: MAT 4..7 are skipped (direct I2C access disabled)."
            ),
            font=("Segoe UI", 9),
            justify="left",
        ).pack(anchor="w", pady=(0, 8))

        params = ttk.Labelframe(root, text="Parameters", padding=8)
        params.pack(fill="x", pady=(0, 8))

        step_code = tk.StringVar(value="1")
        min_code = tk.StringVar(value="0")
        settle_ms = tk.StringVar(value="50")
        poll_ms = tk.StringVar(value="5")
        polls_per_step = tk.StringVar(value="5")
        start_code = tk.StringVar(value="15")
        start_mat = tk.StringVar(value=str(int(mats[0]) if mats else 0))
        start_ch = tk.StringVar(value="0")
        end_mat = tk.StringVar(value=str(int(mats[-1]) if mats else 0))
        end_ch = tk.StringVar(value="63")

        def _row(r: int, label: str, var: tk.StringVar) -> None:
            ttk.Label(params, text=label, width=18).grid(row=r, column=0, sticky="w")
            ttk.Entry(params, textvariable=var, width=8).grid(row=r, column=1, sticky="w", padx=(6, 0))

        _row(0, "Step Δ:", step_code)
        _row(1, "Min FTCODE:", min_code)
        _row(2, "Start FTCODE:", start_code)
        _row(3, "Settle (ms):", settle_ms)
        _row(4, "Poll every (ms):", poll_ms)
        _row(5, "Polls/step:", polls_per_step)
        _row(6, "Start MAT:", start_mat)
        _row(7, "Start CH:", start_ch)
        _row(8, "End MAT:", end_mat)
        _row(9, "End CH:", end_ch)

        status = tk.StringVar(value="—")
        ttk.Label(root, textvariable=status, font=("Segoe UI", 10, "bold")).pack(anchor="w", pady=(0, 6))

        out = tk.Text(root, height=18, width=92, wrap="none", state="disabled")
        yscroll = ttk.Scrollbar(root, orient="vertical", command=out.yview)
        out.configure(yscrollcommand=yscroll.set)
        out.pack(side="left", fill="both", expand=True)
        yscroll.pack(side="right", fill="y")

        stop_evt = threading.Event()

        def log(msg: str) -> None:
            self._fifo_log(out, msg)

        def fifo_hit_expected(mat_id: int, ch: int) -> bool:
            """
            Return True only if the FIFO hit refers to the *current* (mat_id, ch).
            This prevents false calibration on a stale hit coming from the previous channel.
            """
            try:
                w = int(self.hw.FifoReadSingleRobust(quad=quad, retries=12, backoff_s=0.003, do_bus_recovery=True))
            except Exception as e:
                log(f"FIFO read error: {e!r}")
                return False
            if int(w) == 0:
                return False
            try:
                d = self._fifo_decode_word(int(w))
                if int(d.get("fifo_empty", 0)) == 1 and int(d.get("fifo_cnt", 0)) == 0:
                    return False
                # If the hit is not for the current channel, drain a bit and ignore.
                if int(d.get("mat", -1)) != int(mat_id) or int(d.get("channel", -1)) != int(ch):
                    log(
                        f"HIT(other): MAT={d['mat']:2d} CH={d['channel']:2d} "
                        f"(expected MAT={int(mat_id):2d} CH={int(ch):2d}) cnt={d['fifo_cnt']:3d}"
                    )
                    try:
                        _ = self.hw.FifoDrain(max_words=32)
                    except Exception:
                        pass
                    return False
                log(
                    f"HIT: MAT={d['mat']:2d} CH={d['channel']:2d} "
                    f"cnt={d['fifo_cnt']:3d} empty={d['fifo_empty']} full={d['fifo_full']} half={d['fifo_halffull']}"
                )
                return True
            except Exception:
                # If decode fails, treat as a hit (best-effort) but still keep it conservative.
                log(f"HIT(raw): 0x{int(w):016X}")
                return True

        def _calib_one(mat_id: int, ch: int, *, sc: int, step: int, mn: int, st_ms: int, p_ms: int, n_polls: int) -> Optional[int]:
            """
            Single-channel calibration routine used by both popup and block calibration.
            Returns calibrated FTCODE or None if no hit.
            """
            def _is_wdu_err(e: Exception) -> bool:
                s = str(e)
                return ("WDU_Transfer" in s) or ("0x20000015" in s) or ("WD" in s and "Transfer" in s)

            def _try(fn, *, what: str, retries: int = 4) -> bool:
                last: Optional[Exception] = None
                for i in range(max(1, int(retries))):
                    try:
                        fn()
                        return True
                    except Exception as e:
                        last = e
                        # If the USB driver/DLL is failing, stop early to avoid wedging it further.
                        try:
                            if _is_wdu_err(e):
                                stop_evt.set()
                                log(f"ABORT: USB/DLL transfer error during {what}: {e!r}")
                                return False
                        except Exception:
                            pass
                        try:
                            self.hw.i2c_bus_recovery()
                        except Exception:
                            pass
                        time.sleep(0.005 * float(i + 1))
                if last is not None:
                    log(f"HW error during {what}: {last!r}")
                return False

            _try(lambda: self.hw.select_quadrant(quad), what="select_quadrant")
            _try(lambda: self._fifo_ensure_i2c(), what="TopReadout(i2c)")
            # Enable only this channel: FEON+PIXON and enable TDCON for this MAT.
            _try(lambda: self.hw.setAnalogFEON(quad, mattonella=int(mat_id), canale=int(ch), on=True), what="FEON ON")
            _try(lambda: self.hw.AnalogChannelON(quad, mattonella=int(mat_id), canale=int(ch)), what="PIXON ON")
            _try(lambda: self.hw.EnableTDC(quad, Mattonella=int(mat_id), enable=True), what="TDCON ON")
            # Drain FIFO before starting this channel.
            try:
                _ = self.hw.FifoDrain(max_words=128)
            except Exception:
                pass

            prev_code = int(sc)
            code = int(sc)
            # Force starting FTCODE now (avoid inheriting previous channel's code).
            if not _try(
                lambda: self.hw.AnalogChannelFineTune(quad, block=0, mattonella=int(mat_id), canale=int(ch), valore=int(code)),
                what=f"Set FTCODE={int(code)}",
                retries=5,
            ):
                return None
            if st_ms:
                time.sleep(float(st_ms) / 1000.0)
            while code >= mn and not stop_evt.is_set():
                # Set code
                if not _try(
                    lambda c=code: self.hw.AnalogChannelFineTune(
                        quad, block=0, mattonella=int(mat_id), canale=int(ch), valore=int(c)
                    ),
                    what=f"Set FTCODE={int(code)}",
                    retries=5,
                ):
                    return None
                if st_ms:
                    time.sleep(float(st_ms) / 1000.0)
                hit = False
                for _j in range(n_polls):
                    if stop_evt.is_set():
                        break
                    if fifo_hit_expected(int(mat_id), int(ch)):
                        hit = True
                        break
                    time.sleep(float(p_ms) / 1000.0)
                if hit:
                    calib = max(0, min(15, int(prev_code)))
                    _try(
                        lambda: self.hw.AnalogChannelFineTune(
                            quad, block=0, mattonella=int(mat_id), canale=int(ch), valore=int(calib)
                        ),
                        what=f"Set calibrated FTCODE={int(calib)}",
                        retries=5,
                    )
                    return int(calib)
                prev_code = code
                code -= int(step)
            return None

        def worker() -> None:
            try:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(quad)

                step = int(str(step_code.get()).strip() or "1")
                if step <= 0:
                    step = 1
                mn = int(str(min_code.get()).strip() or "0")
                mn = max(0, min(15, mn))
                try:
                    sc = int(str(start_code.get()).strip() or "15")
                except Exception:
                    sc = 15
                sc = max(0, min(15, sc))
                st_ms = int(str(settle_ms.get()).strip() or "5")
                st_ms = max(0, st_ms)
                # Avoid stressing the USB/I2C DLL too aggressively (WDU_Transfer errors).
                if st_ms < 5:
                    st_ms = 5
                p_ms = int(str(poll_ms.get()).strip() or "50")
                p_ms = max(1, p_ms)
                if p_ms < 5:
                    p_ms = 5
                n_polls = int(str(polls_per_step.get()).strip() or "10")
                n_polls = max(1, n_polls)

                try:
                    sm = int(str(start_mat.get()).strip() or str(int(mats[0]) if mats else 0))
                except Exception:
                    sm = int(mats[0]) if mats else 0
                try:
                    sch = int(str(start_ch.get()).strip() or "0")
                except Exception:
                    sch = 0
                sch = max(0, min(63, int(sch)))
                try:
                    em = int(str(end_mat.get()).strip() or str(int(mats[-1]) if mats else sm))
                except Exception:
                    em = int(mats[-1]) if mats else int(sm)
                try:
                    ech = int(str(end_ch.get()).strip() or "63")
                except Exception:
                    ech = 63
                ech = max(0, min(63, int(ech)))

                # Normalize range in terms of iteration order across the block mats list.
                mats_ord = [int(x) for x in mats]
                if int(sm) not in mats_ord:
                    sm = mats_ord[0] if mats_ord else int(sm)
                if int(em) not in mats_ord:
                    em = mats_ord[-1] if mats_ord else int(em)
                i_sm = mats_ord.index(int(sm)) if mats_ord else 0
                i_em = mats_ord.index(int(em)) if mats_ord else 0
                if i_em < i_sm:
                    i_sm, i_em = i_em, i_sm
                    sm, em = em, sm
                    sch, ech = ech, sch

                log(f"RANGE MAT {int(sm)} CH {int(sch)} -> MAT {int(em)} CH {int(ech)} (block mats={mats_ord})")
                resume_started = False
                self.after(0, lambda: status.set("Calibration: starting channels…"))
                log(f"BLOCK {block_id} mats={mats}")

                # During calibration keep ONLY the current channel active:
                # - disable all PIXON in this block
                # - disable TDCON for the MATs in this block
                self.after(0, lambda: status.set("Prep: turning off PIXON/TDCON (only current channel active during calib)…"))
                for mid in mats:
                    if stop_evt.is_set():
                        return
                    if 4 <= int(mid) <= 7:
                        log(f"SKIP MAT {mid}: direct I2C disabled (4..7)")
                        continue
                    try:
                        self.hw.EnableTDC(quad, Mattonella=int(mid), enable=False)
                    except Exception:
                        pass
                    for ch in range(64):
                        if stop_evt.is_set():
                            return
                        try:
                            self.hw.AnalogChannelOFF(quad, mattonella=int(mid), canale=int(ch))
                        except Exception:
                            pass

                # Calibrate channels sequentially (within selected range).
                for mi, mid in enumerate(mats):
                    if mi < i_sm or mi > i_em:
                        continue
                    if stop_evt.is_set():
                        return
                    if 4 <= int(mid) <= 7:
                        continue
                    for ch in range(64):
                        if stop_evt.is_set():
                            return
                        # Apply start/end bounds for first/last MAT in range.
                        if mi == i_sm and int(ch) < int(sch):
                            continue
                        if mi == i_em and int(ch) > int(ech):
                            break
                        if not resume_started:
                            if int(mid) < int(sm):
                                continue
                            if int(mid) == int(sm) and int(ch) < int(sch):
                                continue
                            resume_started = True
                        # Keep track of where we are, so the user can resume.
                        try:
                            start_mat.set(str(int(mid)))
                            start_ch.set(str(int(ch)))
                        except Exception:
                            pass
                        self.after(
                            0,
                            lambda m=mid, c=ch, code=sc: status.set(f"Calibrating MAT {m} CH {c} (start={code})…"),
                        )
                        log(f"START MAT {int(mid)} CH {int(ch)} FTCODE={int(sc)}")
                        try:
                            calib = _calib_one(
                            int(mid),
                            int(ch),
                            sc=int(sc),
                            step=int(step),
                            mn=int(mn),
                            st_ms=int(st_ms),
                            p_ms=int(p_ms),
                            n_polls=int(n_polls),
                        )
                        except Exception as e:
                            log(f"ERROR MAT {int(mid)} CH {int(ch)}: {e!r}")
                            try:
                                self.hw.i2c_bus_recovery()
                            except Exception:
                                pass
                            continue
                        if stop_evt.is_set():
                            self.after(0, lambda: status.set("ABORT: USB/DLL error (WDU). Resume from Start MAT/CH."))
                            return
                        if calib is None:
                            log(f"NOHIT MAT {int(mid)} CH {int(ch)} (reached min={mn})")
                        else:
                            log(f"DONE MAT {int(mid)} CH {int(ch)} calibrated FTCODE={int(calib)}")
                            self.after(0, lambda m=mid, c=ch, cc=calib: self._update_ftdac_cell(int(m), int(c), int(cc)))

                        # After each channel: turn it back OFF and disable TDCON, so only the
                        # currently-calibrated channel stays active.
                        try:
                            self.hw.AnalogChannelOFF(quad, mattonella=int(mid), canale=int(ch))
                        except Exception:
                            pass
                        try:
                            self.hw.EnableTDC(quad, Mattonella=int(mid), enable=False)
                        except Exception:
                            pass

                # Restore state for verification: enable PIXON + TDCON for all pixels of the block.
                self.after(0, lambda: status.set("Restore: turning PIXON+TDCON back on for whole block…"))
                for mid in mats:
                    if stop_evt.is_set():
                        return
                    if 4 <= int(mid) <= 7:
                        continue
                    try:
                        self.hw.EnableTDC(quad, Mattonella=int(mid), enable=True)
                    except Exception:
                        pass
                    for ch in range(64):
                        if stop_evt.is_set():
                            return
                        try:
                            self.hw.AnalogChannelON(quad, mattonella=int(mid), canale=int(ch))
                        except Exception:
                            pass

                self.after(0, lambda: status.set("DONE: block calibration completed."))
                log("DONE: block calibration completed.")
            except Exception as e:
                self.after(0, lambda e=e: status.set(f"ERROR: {e}"))
                self.after(0, lambda e=e: log(f"ERROR: {e!r}"))

        def start() -> None:
            stop_evt.clear()
            threading.Thread(target=worker, daemon=True).start()

        def stop() -> None:
            stop_evt.set()
            status.set("Stop requested…")

        btns = ttk.Frame(root)
        btns.pack(fill="x", pady=(8, 0))
        ttk.Button(btns, text="Start", command=start).pack(side="left")
        ttk.Button(btns, text="Stop", command=stop).pack(side="left", padx=(6, 0))
        ttk.Button(btns, text="FIFO popup…", command=lambda: self._open_fifo_popup(quad)).pack(side="right")

        def on_close() -> None:
            stop_evt.set()
            win.destroy()

        win.protocol("WM_DELETE_WINDOW", on_close)

    def _build_mat_mini(self, parent: ttk.Frame, mat_id: int, *, title: str) -> ttk.Frame:
        frm = ttk.Labelframe(parent, text=title, padding=4)
        quad = str(self.quad_var.get()).strip().upper()

        # Per-MAT ALL toggles (only for direct-addressable MATs)
        ctl = ttk.Frame(frm)
        ctl.pack(fill="x", pady=(0, 4))
        fe_var = tk.BooleanVar(value=False)
        px_var = tk.BooleanVar(value=False)
        td_var = tk.BooleanVar(value=False)

        # Initialize FE/PIX from cache if available (best-effort)
        try:
            qc = self._mat_snapshot_cache.get(quad, {})
            ent = qc.get(int(mat_id), {}) if isinstance(qc, dict) else {}
            if isinstance(ent, dict):
                po = ent.get("pix_on")
                fo = ent.get("fe_on")
                if isinstance(po, list) and len(po) == 64:
                    px_var.set(all(bool(x) for x in po))
                if isinstance(fo, list) and len(fo) == 64:
                    fe_var.set(all(bool(x) for x in fo))
        except Exception:
            pass

        disabled = (4 <= int(mat_id) <= 7)
        if disabled:
            ttk.Label(ctl, text="MAT 4–7: direct I2C disabled", foreground="#777777").pack(side="left")
        else:
            ttk.Checkbutton(
                ctl,
                text="FEON ALL",
                variable=fe_var,
                command=lambda mid=int(mat_id): self._set_mat_all(mid, feon=bool(fe_var.get())),
            ).pack(side="left", padx=(0, 6))
            ttk.Checkbutton(
                ctl,
                text="PIXON ALL",
                variable=px_var,
                command=lambda mid=int(mat_id): self._set_mat_all(mid, pixon=bool(px_var.get())),
            ).pack(side="left", padx=(0, 6))
            ttk.Checkbutton(
                ctl,
                text="TDCON",
                variable=td_var,
                command=lambda mid=int(mat_id): self._set_mat_all(mid, tdcon=bool(td_var.get())),
            ).pack(side="left", padx=(0, 6))

        # Keep references so we can sync checkbox state to the actual chip state on refresh.
        try:
            if not hasattr(self, "_mat_all_vars"):
                self._mat_all_vars = {}  # type: ignore[attr-defined]
            self._mat_all_vars[(str(quad).strip().upper(), int(mat_id))] = {"fe": fe_var, "px": px_var, "td": td_var}  # type: ignore[attr-defined]
        except Exception:
            pass

        cv = tk.Canvas(frm, highlightthickness=0, bd=0, bg="#fafafa")
        cv.pack(fill="both", expand=True)

        # Celle rettangolari (larghezza > altezza) per proporzione visiva tipo pixel MAT.
        _cell_ar = 1.38

        def redraw_pixels(_ev=None) -> None:
            cv.delete("pix")
            cw = int(cv.winfo_width())
            ch = int(cv.winfo_height())
            if cw < 16 or ch < 16:
                return
            pad = 4
            aw = max(8.0, float(cw - 2 * pad))
            ah = max(8.0, float(ch - 2 * pad))
            cell_h = min(ah / 8.0, aw / (8.0 * _cell_ar))
            cell_w = cell_h * _cell_ar
            if 8.0 * cell_w > aw:
                cell_w = aw / 8.0
                cell_h = cell_w / _cell_ar
            gw = 8.0 * cell_w
            gh = 8.0 * cell_h
            gx0 = pad + (aw - gw) / 2.0
            gy0 = pad + (ah - gh) / 2.0
            gap = max(0.5, min(cell_w, cell_h) * 0.04)

            rect_ids: list[int] = []
            txt_ids: list[int] = []
            for row in range(8):
                for col in range(8):
                    x0 = gx0 + col * cell_w + gap
                    y0 = gy0 + row * cell_h + gap
                    x1 = gx0 + (col + 1) * cell_w - gap
                    y1 = gy0 + (row + 1) * cell_h - gap
                    pid = row * 8 + col
                    rid = cv.create_rectangle(
                        x0,
                        y0,
                        x1,
                        y1,
                        fill="#aa2222",
                        outline="#37474f",
                        width=1,
                        tags=("pix", f"r{pid}"),
                    )
                    rect_ids.append(rid)
                    show_txt = max(x1 - x0, y1 - y0) > 12
                    if show_txt:
                        fz = max(6, int(min(cell_w, cell_h) * 0.26))
                        tid = cv.create_text(
                            (x0 + x1) / 2,
                            (y0 + y1) / 2,
                            text="?",
                            fill="white",
                            font=("Segoe UI", fz, "bold"),
                            tags=("pix", f"t{pid}"),
                        )
                        txt_ids.append(tid)
                    else:
                        txt_ids.append(0)

            cv._mat_grid_geom = (mat_id, gx0, gy0, cell_w, cell_h, gap)  # type: ignore[attr-defined]
            self._block_pix_cells[mat_id] = rect_ids
            self._block_pix_canvas[mat_id] = cv
            self._block_pix_txt_ids[mat_id] = txt_ids

            cv._geom_gen = int(getattr(cv, "_geom_gen", 0)) + 1  # type: ignore[attr-defined]
            gen = int(cv._geom_gen)  # type: ignore[attr-defined]

            def reload_after_resize() -> None:
                if int(getattr(cv, "_geom_gen", 0)) != gen:  # type: ignore[attr-defined]
                    return
                self._refresh_mat_mini(mat_id)

            self.after(120, reload_after_resize)

        def on_click(ev: tk.Event) -> None:
            g = getattr(cv, "_mat_grid_geom", None)
            if not g:
                return
            mid, gx0, gy0, c_w, c_h, _gap = g
            x = float(ev.x) - gx0
            y = float(ev.y) - gy0
            if x < 0 or y < 0:
                return
            col = int(x / c_w)
            row = int(y / c_h)
            if 0 <= row < 8 and 0 <= col < 8:
                self._open_ftdac_popup(int(mid), row * 8 + col)

        def on_right_click(ev: tk.Event) -> None:
            # Keep old behavior available: right-click toggles PIXON.
            g = getattr(cv, "_mat_grid_geom", None)
            if not g:
                return
            mid, gx0, gy0, c_w, c_h, _gap = g
            x = float(ev.x) - gx0
            y = float(ev.y) - gy0
            if x < 0 or y < 0:
                return
            col = int(x / c_w)
            row = int(y / c_h)
            if 0 <= row < 8 and 0 <= col < 8:
                self._toggle_pixel_block(int(mid), row * 8 + col)

        cv.bind("<Configure>", redraw_pixels)
        cv.bind("<Button-1>", on_click)
        cv.bind("<Button-3>", on_right_click)
        redraw_pixels()

        return frm

    def _schedule_blocks_view_redraw(self) -> None:
        """If Quadrant→Blocks is open, nudge embedded canvases to redraw from ``_mat_snapshot_cache``."""
        try:
            cb = getattr(self, "_blocks_view_refresh_cb", None)
            if callable(cb):
                self.after(0, cb)
        except Exception:
            pass

    def _sync_mat_snapshot_cache_pixel(self, qkey: str, mat_id: int, pix_id: int, *, pix_on: bool, fe_on: bool) -> None:
        """
        Merge one pixel into the global MAT snapshot so block overview tiles stay in sync with HW
        (previously only FILE-prefill mode updated this cache, so colors lagged until re-navigation).
        """
        q = str(qkey).strip().upper()
        if q not in {"SW", "NW", "SE", "NE"}:
            return
        mid = int(mat_id)
        pid = int(pix_id)
        if pid < 0 or pid > 63:
            return
        qc = self._mat_snapshot_cache.setdefault(q, {})
        ent = qc.setdefault(mid, {})
        po = ent.get("pix_on")
        if not isinstance(po, list) or len(po) < 64:
            po = [False] * 64
        fo = ent.get("fe_on")
        if not isinstance(fo, list) or len(fo) < 64:
            fo = [True] * 64
        ft = ent.get("ftdac")
        if not isinstance(ft, list) or len(ft) < 64:
            ft = [15] * 64
        po[pid] = bool(pix_on)
        fo[pid] = bool(fe_on)
        ent["pix_on"] = po
        ent["fe_on"] = fo
        ent["ftdac"] = ft

    def _update_ftdac_cell(self, mat_id: int, pix_id: int, code: int) -> None:
        canvas = self._block_pix_canvas.get(int(mat_id))
        txts = self._block_pix_txt_ids.get(int(mat_id)) or []
        if canvas is not None and pix_id < len(txts) and txts[pix_id]:
            try:
                canvas.itemconfigure(txts[pix_id], text=str(int(code)))
            except tk.TclError:
                pass
        qkey = str(self.quad_var.get()).strip().upper()
        if qkey in {"SW", "NW", "SE", "NE"}:
            qc = self._mat_snapshot_cache.setdefault(qkey, {})
            ent = qc.setdefault(int(mat_id), {})
            po = ent.get("pix_on")
            if not isinstance(po, list) or len(po) < 64:
                po = [False] * 64
                ent["pix_on"] = po
            fo = ent.get("fe_on")
            if not isinstance(fo, list) or len(fo) < 64:
                fo = [True] * 64
                ent["fe_on"] = fo
            ft = ent.get("ftdac")
            if not isinstance(ft, list) or len(ft) < 64:
                ft = [15] * 64
            if int(pix_id) < len(ft):
                ft[int(pix_id)] = int(code) & 0x0F
            ent["ftdac"] = ft
        self._schedule_blocks_view_redraw()

    def _update_pixel_fill_state(self, mat_id: int, pix_id: int, *, pix_on: bool, fe_on: bool) -> None:
        """
        Color policy for block pixels:
        - FEON=0 -> gray (channel effectively off regardless of PIXON)
        - FEON=1 -> green if PIXON=1 else red
        """
        canvas = self._block_pix_canvas.get(int(mat_id))
        rects = self._block_pix_cells.get(int(mat_id))
        if canvas is not None and rects and int(pix_id) < len(rects):
            if not bool(fe_on):
                fill = "#bdbdbd"
            else:
                fill = "#1f9d55" if bool(pix_on) else "#aa2222"
            try:
                canvas.itemconfigure(rects[int(pix_id)], fill=fill)
            except tk.TclError:
                pass

        qkey = str(self.quad_var.get()).strip().upper()
        self._sync_mat_snapshot_cache_pixel(qkey, int(mat_id), int(pix_id), pix_on=bool(pix_on), fe_on=bool(fe_on))
        self._schedule_blocks_view_redraw()

    def _open_ftdac_popup(self, mat_id: int, pix_id: int) -> None:
        quad = self.quad_var.get()
        if 4 <= int(mat_id) <= 7:
            self._set_status(f"MAT {mat_id}: direct I2C disabled (stack issue). Use broadcast / Calib DCO.")
            return

        win = tk.Toplevel(self)
        win.title(f"FTDAC — Q={quad} MAT={mat_id} PIX={pix_id}")
        win.transient(self)
        win.resizable(False, False)

        cur_var = tk.StringVar(value="?")
        set_var = tk.StringVar(value="")
        delta_var = tk.StringVar(value="1")
        pix_on_var = tk.StringVar(value="?")
        tdc_on_var = tk.StringVar(value="?")
        both_var = tk.BooleanVar(value=False)

        hdr = ttk.Frame(win, padding=10)
        hdr.pack(fill="x")
        ttk.Label(hdr, text=f"Quadrant {quad}  |  MAT {mat_id}  |  Pixel {pix_id}", font=("Segoe UI", 11, "bold")).pack(
            anchor="w"
        )

        row = ttk.Frame(win, padding=(10, 0))
        row.pack(fill="x", pady=(6, 0))
        ttk.Label(row, text="FTCODE =", width=12).pack(side="left")
        tk.Label(row, textvariable=cur_var, font=("Segoe UI", 12, "bold")).pack(side="left")

        row2 = ttk.Frame(win, padding=(10, 0))
        row2.pack(fill="x", pady=(10, 0))
        ttk.Label(row2, text="Set to:", width=12).pack(side="left")
        e_set = ttk.Entry(row2, width=6, textvariable=set_var)
        e_set.pack(side="left")

        state_box = ttk.Labelframe(win, text="Channel enable", padding=(10, 6))
        state_box.pack(fill="x", padx=10, pady=(10, 0))
        st1 = ttk.Frame(state_box)
        st1.pack(fill="x")
        ttk.Label(st1, text="PIXON:", width=8).pack(side="left")
        ttk.Label(st1, textvariable=pix_on_var, width=6).pack(side="left")
        ttk.Label(st1, text="TDCON:", width=8).pack(side="left", padx=(12, 0))
        ttk.Label(st1, textvariable=tdc_on_var, width=6).pack(side="left")
        ttk.Label(st1, text="FEON:", width=8).pack(side="left", padx=(12, 0))
        fe_on_var = tk.StringVar(value="?")
        ttk.Label(st1, textvariable=fe_on_var, width=6).pack(side="left")
        feon_flag = tk.BooleanVar(value=False)

        def _update_pixel_fill(on: bool) -> None:
            # Backward-compatible helper: reads FEON and applies color policy.
            try:
                fe = bool(self.hw.readAnalogFEON(quad, mattonella=int(mat_id), canale=int(pix_id)))
            except Exception:
                fe = True
            self._update_pixel_fill_state(int(mat_id), int(pix_id), pix_on=bool(on), fe_on=bool(fe))

        def _read_states() -> None:
            def do() -> dict[str, bool]:
                self.hw.select_quadrant(quad)
                p = bool(self.hw.readAnalogChannelON(quad, mattonella=int(mat_id), canale=int(pix_id)))
                t = bool(self.hw.readEnableTDC(quad, Mattonella=int(mat_id))["tdc_on"])
                f = bool(self.hw.readAnalogFEON(quad, mattonella=int(mat_id), canale=int(pix_id)))
                return {"pix_on": p, "tdc_on": t, "fe_on": f}

            r = self._with_hw(do, busy=f"Read PIXON/TDC (Q={quad} MAT={mat_id} PIX={pix_id})")
            if not isinstance(r, dict):
                return
            p = bool(r.get("pix_on", False))
            t = bool(r.get("tdc_on", False))
            f = bool(r.get("fe_on", False))
            pix_on_var.set("ON" if p else "OFF")
            tdc_on_var.set("ON" if t else "OFF")
            fe_on_var.set("ON" if f else "OFF")
            both_var.set(bool(p and t))
            feon_flag.set(bool(f))

        def _apply_both(enable: bool) -> None:
            def do() -> dict[str, bool]:
                self.hw.select_quadrant(quad)
                # PIXON per-channel
                if bool(enable):
                    self.hw.AnalogChannelON(quad, mattonella=int(mat_id), canale=int(pix_id))
                else:
                    self.hw.AnalogChannelOFF(quad, mattonella=int(mat_id), canale=int(pix_id))
                # TDCON is MAT-level
                self.hw.EnableTDC(quad, Mattonella=int(mat_id), enable=bool(enable))
                p = bool(self.hw.readAnalogChannelON(quad, mattonella=int(mat_id), canale=int(pix_id)))
                t = bool(self.hw.readEnableTDC(quad, Mattonella=int(mat_id))["tdc_on"])
                return {"pix_on": p, "tdc_on": t}

            r = self._with_hw(do, busy=f"Set PIXON+TDC={'ON' if enable else 'OFF'} (Q={quad} MAT={mat_id} PIX={pix_id})")
            if not isinstance(r, dict):
                return
            p = bool(r.get("pix_on", False))
            t = bool(r.get("tdc_on", False))
            pix_on_var.set("ON" if p else "OFF")
            tdc_on_var.set("ON" if t else "OFF")
            both_var.set(bool(p and t))
            _update_pixel_fill(p)

        def _apply_feon(enable: bool) -> None:
            def do() -> bool:
                self.hw.select_quadrant(quad)
                self.hw.setAnalogFEON(quad, mattonella=int(mat_id), canale=int(pix_id), on=bool(enable))
                return bool(self.hw.readAnalogFEON(quad, mattonella=int(mat_id), canale=int(pix_id)))

            v = self._with_hw(do, busy=f"Set FEON={'ON' if enable else 'OFF'} (Q={quad} MAT={mat_id} PIX={pix_id})")
            if v is None:
                return
            feon_flag.set(bool(v))
            fe_on_var.set("ON" if bool(v) else "OFF")
            # Update pixel fill immediately according to FEON+PIXON.
            try:
                p = bool(self.hw.readAnalogChannelON(quad, mattonella=int(mat_id), canale=int(pix_id)))
            except Exception:
                p = False
            self._update_pixel_fill_state(int(mat_id), int(pix_id), pix_on=p, fe_on=bool(v))

        ttk.Checkbutton(
            state_box,
            text="Enable channel (PIXON + TDCON)",
            variable=both_var,
            command=lambda: _apply_both(bool(both_var.get())),
        ).pack(anchor="w", pady=(6, 0))
        ttk.Checkbutton(
            state_box,
            text="FEON (ENPOW)",
            variable=feon_flag,
            command=lambda: _apply_feon(bool(feon_flag.get())),
        ).pack(anchor="w", pady=(2, 0))

        # --- TDC calibration results (from Calib DCO dialog) ---
        dco_box = ttk.Labelframe(win, text="TDC calibration (DCO)", padding=(10, 6))
        dco_box.pack(fill="x", padx=10, pady=(10, 0))
        dco0_var = tk.StringVar(value="—")
        dco1_var = tk.StringVar(value="—")
        lsb_var = tk.StringVar(value="—")

        drow = ttk.Frame(dco_box)
        drow.pack(fill="x")
        ttk.Label(drow, text="DCO0_T (ps):", width=14).pack(side="left")
        ttk.Label(drow, textvariable=dco0_var, width=14).pack(side="left")
        ttk.Label(drow, text="DCO1_T (ps):", width=14).pack(side="left", padx=(12, 0))
        ttk.Label(drow, textvariable=dco1_var, width=14).pack(side="left")
        drow2 = ttk.Frame(dco_box)
        drow2.pack(fill="x", pady=(4, 0))
        ttk.Label(drow2, text="LSB (ps):", width=14).pack(side="left")
        ttk.Label(drow2, textvariable=lsb_var, width=14).pack(side="left")

        def _refresh_dco_cache_view() -> None:
            k = (str(quad).strip().upper(), int(mat_id), int(pix_id))
            d = self._dco_calib_cache.get(k)
            if not isinstance(d, dict):
                dco0_var.set("—")
                dco1_var.set("—")
                lsb_var.set("—")
                return
            try:
                d0 = float(d.get("dco0_ps", 0.0))
                d1 = float(d.get("dco1_ps", 0.0))
                lsb = float(d.get("lsb_ps", 0.0))
            except Exception:
                dco0_var.set("—")
                dco1_var.set("—")
                lsb_var.set("—")
                return
            if d0 <= 0.0 or d1 <= 0.0 or lsb <= 0.0:
                dco0_var.set("—")
                dco1_var.set("—")
                lsb_var.set("—")
                return
            dco0_var.set(f"{d0:.2f}")
            dco1_var.set(f"{d1:.2f}")
            lsb_var.set(f"{lsb:.2f}")

        def _read_cur() -> None:
            def do() -> int:
                return int(self.hw.readAnalogChannelFineTune(quad, mattonella=int(mat_id), canale=int(pix_id)))

            v = self._with_hw(do, busy=f"Read FTDAC (Q={quad} MAT={mat_id} PIX={pix_id})")
            if v is None:
                return
            cur_var.set(str(int(v)))
            _read_states()
            _refresh_dco_cache_view()

        def _apply(code: int) -> None:
            code = int(code)
            if code < 0:
                code = 0
            if code > 15:
                code = 15

            def do() -> int:
                self.hw.AnalogChannelFineTune(quad, block=0, mattonella=int(mat_id), canale=int(pix_id), valore=int(code))
                return int(self.hw.readAnalogChannelFineTune(quad, mattonella=int(mat_id), canale=int(pix_id)))

            v = self._with_hw(do, busy=f"Set FTDAC={code} (Q={quad} MAT={mat_id} PIX={pix_id})")
            if v is None:
                return
            cur_var.set(str(int(v)))
            self._update_ftdac_cell(int(mat_id), int(pix_id), int(v))

        def _on_set() -> None:
            try:
                _apply(int(str(set_var.get()).strip()))
            except Exception:
                self._set_status("FTDAC: inserire un numero 0..15")

        btns = ttk.Frame(win, padding=(10, 10))
        btns.pack(fill="x")
        ttk.Button(btns, text="Refresh", command=_read_cur).pack(side="left")
        ttk.Button(btns, text="Set", command=_on_set).pack(side="left", padx=(6, 0))
        ttk.Button(btns, text="Calibrate channel…", command=lambda: self._calib_pixel_threshold(mat_id, pix_id)).pack(
            side="right"
        )

        step_row = ttk.Frame(win, padding=(10, 0))
        step_row.pack(fill="x", pady=(0, 10))
        ttk.Label(step_row, text="Δ:", width=12).pack(side="left")
        ttk.Entry(step_row, width=6, textvariable=delta_var).pack(side="left")

        def _delta(sign: int) -> None:
            try:
                d = int(str(delta_var.get()).strip())
            except Exception:
                d = 1
            if d <= 0:
                d = 1
            try:
                cur = int(str(cur_var.get()).strip())
            except Exception:
                _read_cur()
                try:
                    cur = int(str(cur_var.get()).strip())
                except Exception:
                    return
            _apply(cur + int(sign) * int(d))

        ttk.Button(step_row, text="+Δ", command=lambda: _delta(+1)).pack(side="left", padx=(10, 4))
        ttk.Button(step_row, text="-Δ", command=lambda: _delta(-1)).pack(side="left")

        # Better UX: ENTER in "Set to" triggers Set
        e_set.bind("<Return>", lambda _ev: _on_set())

        _read_cur()

    def _calib_pixel_threshold(self, mat_id: int, pix_id: int) -> None:
        """
        Experimental: calibrate a single pixel threshold by sweeping FTDAC down until a FIFO hit appears.
        When the first hit is detected, we store the *previous* code (one step above) as calibrated.
        """
        quad = str(self.quad_var.get()).strip().upper()
        if 4 <= int(mat_id) <= 7:
            self._set_status(f"MAT {mat_id}: calibration disabled (I2C stack issue).")
            return

        win = tk.Toplevel(self)
        win.title(f"Threshold calibration — Q={quad} MAT={mat_id} PIX={pix_id}")
        win.transient(self)
        win.resizable(True, True)

        root = ttk.Frame(win, padding=10)
        root.pack(fill="both", expand=True)

        info = ttk.Label(
            root,
            text=(
                "Procedure: decrease FTCODE (FTDAC) until a FIFO hit appears.\n"
                "On first hit, set the previous code and stop."
            ),
            font=("Segoe UI", 9),
            justify="left",
        )
        info.pack(anchor="w", pady=(0, 8))

        params = ttk.Labelframe(root, text="Parameters", padding=8)
        params.pack(fill="x", pady=(0, 8))

        start_code = tk.StringVar(value="")
        step_code = tk.StringVar(value="1")
        min_code = tk.StringVar(value="0")
        settle_ms = tk.StringVar(value="50")
        poll_ms = tk.StringVar(value="5")
        polls_per_step = tk.StringVar(value="5")

        def _row(r: int, label: str, var: tk.StringVar) -> None:
            ttk.Label(params, text=label, width=18).grid(row=r, column=0, sticky="w")
            ttk.Entry(params, textvariable=var, width=8).grid(row=r, column=1, sticky="w", padx=(6, 0))

        _row(0, "Start FTCODE:", start_code)
        _row(1, "Step Δ:", step_code)
        _row(2, "Min FTCODE:", min_code)
        _row(3, "Settle (ms):", settle_ms)
        _row(4, "Poll every (ms):", poll_ms)
        _row(5, "Polls/step:", polls_per_step)

        status = tk.StringVar(value="—")
        ttk.Label(root, textvariable=status, font=("Segoe UI", 10, "bold")).pack(anchor="w", pady=(0, 6))

        out = tk.Text(root, height=14, width=80, wrap="none", state="disabled")
        yscroll = ttk.Scrollbar(root, orient="vertical", command=out.yview)
        out.configure(yscrollcommand=yscroll.set)
        out.pack(side="left", fill="both", expand=True)
        yscroll.pack(side="right", fill="y")

        stop_evt = threading.Event()

        def log(msg: str) -> None:
            self._fifo_log(out, msg)

        # On entering calibration: enable the channel (PIXON + FEON) and enable TDCON for this MAT,
        # and default start FTCODE to 15.
        try:
            start_code.set("15")
        except Exception:
            pass

        def _prepare_channel() -> None:
            def do() -> None:
                self.hw.select_quadrant(quad)
                self.hw.setAnalogFEON(quad, mattonella=int(mat_id), canale=int(pix_id), on=True)
                self.hw.AnalogChannelON(quad, mattonella=int(mat_id), canale=int(pix_id))
                self.hw.EnableTDC(quad, Mattonella=int(mat_id), enable=True)

            # Best-effort: don't block popup; errors will be visible in status/log.
            try:
                self._with_hw(do, busy=f"Prep calib (PIXON+FEON+TDCON) (Q={quad} MAT={mat_id} PIX={pix_id})")
            except Exception:
                pass

        self.after(50, _prepare_channel)

        def fifo_hit_once() -> bool:
            """
            Return True if we see a non-empty FIFO word (according to embedded empty flag),
            otherwise False.
            """
            try:
                w = int(self.hw.FifoReadSingleRobust(quad=quad, retries=12, backoff_s=0.003, do_bus_recovery=True))
            except Exception as e:
                # Don't abort calibration for a single glitch; just log and treat as "no hit" this poll.
                log(f"FIFO read error: {e!r}")
                return False
            if w == 0:
                return False
            try:
                d = self._fifo_decode_word(w)
                if int(d.get("fifo_empty", 0)) == 1 and int(d.get("fifo_cnt", 0)) == 0:
                    return False
                # Log the decoded info for the operator.
                log(
                    f"HIT: MAT={d['mat']:2d} CH={d['channel']:2d} "
                    f"cnt={d['fifo_cnt']:3d} empty={d['fifo_empty']} full={d['fifo_full']} half={d['fifo_halffull']}"
                )
                return True
            except Exception:
                log(f"HIT: 0x{w:016X}")
                return True

        def worker() -> None:
            try:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(quad)

                # Read current code if Start empty.
                sc_raw = str(start_code.get()).strip()
                if not sc_raw:
                    try:
                        cur = int(self.hw.readAnalogChannelFineTune(quad, mattonella=int(mat_id), canale=int(pix_id)))
                    except Exception:
                        cur = 15
                else:
                    cur = int(sc_raw)
                if cur < 0:
                    cur = 0
                if cur > 15:
                    cur = 15

                step = int(str(step_code.get()).strip() or "1")
                if step <= 0:
                    step = 1
                mn = int(str(min_code.get()).strip() or "0")
                if mn < 0:
                    mn = 0
                if mn > 15:
                    mn = 15

                st_ms = int(str(settle_ms.get()).strip() or "5")
                if st_ms < 0:
                    st_ms = 0
                if st_ms < 5:
                    st_ms = 5
                p_ms = int(str(poll_ms.get()).strip() or "50")
                if p_ms < 1:
                    p_ms = 1
                if p_ms < 5:
                    p_ms = 5
                n_polls = int(str(polls_per_step.get()).strip() or "10")
                if n_polls < 1:
                    n_polls = 1

                # Drain residual FIFO once at start.
                try:
                    _ = self.hw.FifoDrain(max_words=128)
                except Exception:
                    pass

                prev_code = cur
                self.after(0, lambda: status.set(f"Calibrating… start={cur} step={step} min={mn}"))
                self.after(0, lambda: log(f"START: current FTCODE={cur}"))

                code = cur
                while code >= mn and not stop_evt.is_set():
                    # Set code
                    self.hw.AnalogChannelFineTune(quad, block=0, mattonella=int(mat_id), canale=int(pix_id), valore=int(code))
                    # Update UI in popup immediately
                    self.after(0, lambda c=code: status.set(f"FTCODE={c} → polling FIFO…"))
                    self.after(0, lambda c=code: log(f"SET FTCODE={c}"))
                    if st_ms:
                        time.sleep(float(st_ms) / 1000.0)

                    hit = False
                    for _i in range(n_polls):
                        if stop_evt.is_set():
                            break
                        if fifo_hit_once():
                            hit = True
                            break
                        time.sleep(float(p_ms) / 1000.0)

                    if hit:
                        # Calibrated = previous code (one step above the first-hit code)
                        calib = int(prev_code)
                        if calib < 0:
                            calib = 0
                        if calib > 15:
                            calib = 15
                        self.hw.AnalogChannelFineTune(
                            quad, block=0, mattonella=int(mat_id), canale=int(pix_id), valore=int(calib)
                        )
                        self.after(0, lambda c=calib: status.set(f"CALIBRATED: FTCODE={c} (hit at {code})"))
                        self.after(0, lambda c=calib: log(f"DONE: calibrated FTCODE={c} (hit at {code})"))
                        # Update block mini view numbers/colors
                        self.after(0, lambda c=calib: self._update_ftdac_cell(int(mat_id), int(pix_id), int(c)))
                        return

                    prev_code = code
                    code -= step

                self.after(0, lambda: status.set("STOP: no hit (min reached or interrupted)."))
                self.after(0, lambda: log("STOP: no hit"))
            except Exception as e:
                self.after(0, lambda e=e: status.set(f"ERROR: {e}"))
                self.after(0, lambda e=e: log(f"ERROR: {e!r}"))

        def start() -> None:
            stop_evt.clear()
            threading.Thread(target=worker, daemon=True).start()

        def stop() -> None:
            stop_evt.set()
            status.set("Stop requested…")

        btns = ttk.Frame(root)
        btns.pack(fill="x", pady=(8, 0))
        ttk.Button(btns, text="Start", command=start).pack(side="left")
        ttk.Button(btns, text="Stop", command=stop).pack(side="left", padx=(6, 0))
        ttk.Button(btns, text="FIFO popup…", command=lambda: self._open_fifo_popup(quad)).pack(side="right")

        def on_close() -> None:
            stop_evt.set()
            win.destroy()

        win.protocol("WM_DELETE_WINDOW", on_close)

    def _toggle_pixel_block(self, mat_id: int, pix_id: int) -> None:
        quad = self.quad_var.get()
        # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
        if 4 <= int(mat_id) <= 7:
            self._set_status(f"MAT {mat_id}: direct I2C disabled (stack issue). Use broadcast / Calib DCO.")
            _dbg(f"toggle pixel blocked: MAT {mat_id} in [4..7] (known I2C stack issue)")
            return

        def do() -> bool:
            self.hw.select_quadrant(quad)
            cur = bool(self.hw.readAnalogChannelON(quad, mattonella=mat_id, canale=pix_id))
            if cur:
                self.hw.AnalogChannelOFF(quad, mattonella=mat_id, canale=pix_id)
            else:
                self.hw.AnalogChannelON(quad, mattonella=mat_id, canale=pix_id)
            return not cur

        new_state = self._with_hw(do, busy=f"Toggling pixel {pix_id} (Q={quad} MAT={mat_id})")
        if new_state is None:
            return
        try:
            fe = bool(self.hw.readAnalogFEON(quad, mattonella=int(mat_id), canale=int(pix_id)))
        except Exception:
            fe = True
        self._update_pixel_fill_state(int(mat_id), int(pix_id), pix_on=bool(new_state), fe_on=bool(fe))

        self._set_status(f"Pixel {pix_id}={'ON' if new_state else 'OFF'} (Q={quad} MAT={mat_id})")

    def _block_all_toggle(
        self,
        block_id: int,
        *,
        what: str,
        var: tk.BooleanVar,
        controls: list[object],
        feon: Optional[bool] = None,
        pixon: Optional[bool] = None,
        tdcon: Optional[bool] = None,
    ) -> None:
        """
        Debounced wrapper for FEON/PIXON/TDCON ALL toggles.
        Prevents burst-clicks from enqueueing multiple long I2C sequences and keeps UI vars consistent.
        """
        desired = bool(var.get())
        prev = not desired

        if self.offline:
            self._set_status(f"{what}: offline")
            try:
                var.set(prev)
            except Exception:
                pass
            return

        if self._bulk_all_in_progress:
            try:
                var.set(prev)
            except Exception:
                pass
            self._set_status("Busy: ALL operation already running…")
            return

        # Disable ALL controls during the operation.
        for w in controls:
            try:
                w.configure(state="disabled")  # type: ignore[attr-defined]
            except Exception:
                pass

        def _done(*, ok: bool, err: Optional[str]) -> None:
            if not ok:
                try:
                    var.set(prev)
                except Exception:
                    pass
                if err and err != "busy":
                    self._set_status(f"{what}: ERROR — {err}")
            for w in controls:
                try:
                    w.configure(state="normal")  # type: ignore[attr-defined]
                except Exception:
                    pass

        self._set_block_all(
            block_id,
            feon=feon,
            pixon=pixon,
            tdcon=tdcon,
            _done_cb=_done,
        )

    def _set_block_all(
        self,
        block_id: int,
        *,
        feon: Optional[bool] = None,
        pixon: Optional[bool] = None,
        tdcon: Optional[bool] = None,
        _done_cb: Optional[object] = None,
    ) -> None:
        quad = self.quad_var.get()
        mats = self.mapping.mats_in_block(block_id)

        if self.offline:
            self._set_status("Apply ALL (offline)")
            return
        if self._bulk_all_in_progress:
            self._set_status("Busy: another ALL operation is running…")
            try:
                if callable(_done_cb):
                    _done_cb(ok=False, err="busy")  # type: ignore[misc]
            except Exception:
                pass
            return

        busy = f"Apply ALL (block={block_id} quad={str(quad).strip().upper()})"
        self._set_status(busy)
        self._bulk_all_in_progress = True
        try:
            import time as _time

            t_start = float(_time.perf_counter())
        except Exception:
            t_start = 0.0

        # Snapshot of intent for optimistic UI update after worker completes.
        qkey = str(quad).strip().upper()
        want_fe = None if feon is None else bool(feon)
        want_px = None if pixon is None else bool(pixon)

        def _optimistic_update() -> None:
            # Update local cache + canvas without re-reading HW (avoid another long I2C burst).
            try:
                qc = self._mat_snapshot_cache.get(qkey, {})
            except Exception:
                qc = {}
            for mid in mats:
                if 4 <= int(mid) <= 7:
                    continue
                ent = qc.setdefault(int(mid), {})
                po = ent.get("pix_on")
                if not isinstance(po, list) or len(po) < 64:
                    po = [False] * 64
                    ent["pix_on"] = po
                fo = ent.get("fe_on")
                if not isinstance(fo, list) or len(fo) < 64:
                    fo = [True] * 64
                    ent["fe_on"] = fo

                canvas = self._block_pix_canvas.get(int(mid))
                rects = self._block_pix_cells.get(int(mid))
                for ch in range(64):
                    cur_po = bool(po[ch]) if ch < len(po) else False
                    cur_fo = bool(fo[ch]) if ch < len(fo) else True
                    if want_px is not None:
                        cur_po = bool(want_px)
                        po[ch] = bool(cur_po)
                    if want_fe is not None:
                        cur_fo = bool(want_fe)
                        fo[ch] = bool(cur_fo)
                    # Apply color policy directly (same as _update_pixel_fill_state)
                    if canvas is not None and rects and ch < len(rects):
                        if not bool(cur_fo):
                            fill = "#bdbdbd"
                        else:
                            fill = "#1f9d55" if bool(cur_po) else "#aa2222"
                        try:
                            canvas.itemconfigure(rects[ch], fill=fill)
                        except tk.TclError:
                            pass

                # Sync per-MAT ALL checkboxes (if present in this view) to match the applied intent.
                try:
                    mv = getattr(self, "_mat_all_vars", {}).get((qkey, int(mid)))
                    if isinstance(mv, dict):
                        if want_fe is not None and isinstance(mv.get("fe"), tk.BooleanVar):
                            mv["fe"].set(bool(want_fe))
                        if want_px is not None and isinstance(mv.get("px"), tk.BooleanVar):
                            mv["px"].set(bool(want_px))
                        if tdcon is not None and isinstance(mv.get("td"), tk.BooleanVar):
                            mv["td"].set(bool(tdcon))
                except Exception:
                    pass
            self._schedule_blocks_view_redraw()

        def work() -> None:
            err_s: Optional[str] = None
            try:
                with self._hw_seq_lock:
                    self.hw.select_quadrant(quad)
                    for mi, mid in enumerate(mats):
                        if 4 <= int(mid) <= 7:
                            continue
                        # Coarse progress: MAT index
                        try:
                            self.after(0, lambda mi=mi, mid=mid: self._set_status(f"{busy} … MAT {int(mid)} ({mi+1}/{len(mats)})"))
                        except Exception:
                            pass
                        if tdcon is not None:
                            self.hw.EnableTDC(quad, Mattonella=int(mid), enable=bool(tdcon))
                        if feon is not None or pixon is not None:
                            for ch in range(64):
                                # Fine progress every 8 channels to avoid spamming Tk.
                                if (ch % 8) == 0:
                                    try:
                                        self.after(
                                            0,
                                            lambda ch=ch, mid=mid: self._set_status(
                                                f"{busy} … MAT {int(mid)} CH {int(ch)}/63"
                                            ),
                                        )
                                    except Exception:
                                        pass
                                if feon is not None:
                                    self.hw.setAnalogFEON(quad, mattonella=int(mid), canale=int(ch), on=bool(feon))
                                if pixon is not None:
                                    if bool(pixon):
                                        self.hw.AnalogChannelON(quad, mattonella=int(mid), canale=int(ch))
                                    else:
                                        self.hw.AnalogChannelOFF(quad, mattonella=int(mid), canale=int(ch))
            except Exception as e:
                err_s = str(e)
                _dbg(f"Apply ALL failed: {e!r}")

            def apply() -> None:
                self._bulk_all_in_progress = False
                try:
                    import time as _time

                    dt_s = float(_time.perf_counter() - t_start) if t_start else 0.0
                except Exception:
                    dt_s = 0.0
                if err_s:
                    self._set_status(f"Apply ALL: ERROR after {dt_s:.2f}s — {err_s}" if dt_s else f"Apply ALL: ERROR — {err_s}")
                    try:
                        if callable(_done_cb):
                            _done_cb(ok=False, err=err_s)  # type: ignore[misc]
                    except Exception:
                        pass
                    return
                _optimistic_update()
                self._set_status(
                    f"Apply ALL: DONE in {dt_s:.2f}s (optimistic UI; use Refresh block to re-sync)"
                    if dt_s
                    else "Apply ALL: DONE (optimistic UI; use Refresh block to re-sync)"
                )
                try:
                    if callable(_done_cb):
                        _done_cb(ok=True, err=None)  # type: ignore[misc]
                except Exception:
                    pass

            self.after(0, apply)

        threading.Thread(target=work, daemon=True).start()

    def _set_mat_all(self, mat_id: int, *, feon: Optional[bool] = None, pixon: Optional[bool] = None, tdcon: Optional[bool] = None) -> None:
        quad = self.quad_var.get()

        def do() -> None:
            self.hw.select_quadrant(quad)
            mid = int(mat_id)
            if 4 <= int(mid) <= 7:
                return
            if tdcon is not None:
                self.hw.EnableTDC(quad, Mattonella=int(mid), enable=bool(tdcon))
            if feon is not None or pixon is not None:
                for ch in range(64):
                    if feon is not None:
                        self.hw.setAnalogFEON(quad, mattonella=int(mid), canale=int(ch), on=bool(feon))
                    if pixon is not None:
                        if bool(pixon):
                            self.hw.AnalogChannelON(quad, mattonella=int(mid), canale=int(ch))
                        else:
                            self.hw.AnalogChannelOFF(quad, mattonella=int(mid), canale=int(ch))

        self._with_hw(do, busy=f"Apply ALL (MAT={int(mat_id)} quad={str(quad).strip().upper()})")
        # Always refresh from HW/cache so checkboxes reflect real chip state (even on errors).
        try:
            self._refresh_mat_mini(int(mat_id))
        except Exception:
            pass

    def _iref_sync_for_quad(self, quad: str) -> None:
        q = str(quad).strip().upper()
        if q not in self._iref_last_mv_by_quad:
            return
        try:
            self._iref_var.set(f"{float(self._iref_last_mv_by_quad[q]):.6g}")
        except Exception:
            pass

    def _iref_read(self, quad: Optional[str] = None) -> None:
        # External IREF DAC is write-only; show last value set (C# behavior).
        q = str(quad or self.quad_var.get()).strip().upper()
        self._iref_sync_for_quad(q)
        try:
            self._set_status(f"IREF (cached, quad={q}) = {float(self._iref_last_mv_by_quad.get(q, 0.0)):.6g} mV")
        except Exception:
            self._set_status(f"IREF (cached, quad={q})")

    def _iref_set(self, quad: str, value_s: str) -> None:
        try:
            v = float(str(value_s).strip())
        except Exception:
            self._set_status("IREF: valore non numerico (mV)")
            return
        if v < 0:
            v = 0.0

        # Use current VDDA (mV) if available; otherwise fall back to 1200 mV like C# default max.
        try:
            vdda_mv = float(str(getattr(self, "_vdda_var", tk.StringVar(value="")).get()).strip())
            if vdda_mv <= 0:
                raise ValueError()
        except Exception:
            vdda_mv = 1200.0

        q = str(quad).strip().upper()
        if q not in {"SW", "NW", "SE", "NE"}:
            self._set_status("IREF: quad non valido (SW/NW/SE/NE)")
            return

        def do() -> dict[str, object]:
            self.hw.select_quadrant(q)
            self.hw.AnalogSetIREF(v, vdda_mv=float(vdda_mv))
            # Optional physical confirmation: measure V_Iref via Quad ADC channel 7.
            meas = None
            try:
                meas = self.hw._adc_quad_oneshot(channel=7, gain=0, res_bits=16, delay_s=0.01)  # type: ignore[attr-defined]
            except Exception:
                meas = None
            return {"set_mv": float(v), "vdda_mv": float(vdda_mv), "meas": meas}

        r = self._with_hw(do, busy=f"Set IREF={v} mV (VDDA={float(vdda_mv):.6g} mV)")
        if not isinstance(r, dict):
            return
        self._iref_last_mv_by_quad[q] = float(r.get("set_mv", v))
        try:
            self._iref_var.set(f"{float(self._iref_last_mv_by_quad[q]):.6g}")
        except Exception:
            pass
        meas = r.get("meas")
        if isinstance(meas, dict):
            try:
                mv = float(meas.get("value_mv"))  # type: ignore[arg-type]
                vv = float(meas.get("value"))  # type: ignore[arg-type]
                self._iref_meas_var.set(f"ADC V_Iref: {vv:.6g} V / {mv:.6g} mV")
            except Exception:
                pass
        self._set_status(
            f"IREF set={float(self._iref_last_mv_by_quad[q]):.6g} mV (quad={q}, scaled by VDDA={float(vdda_mv):.6g} mV)"
        )

    def _refresh_mat_mini(self, mat_id: int) -> None:
        quad = self.quad_var.get()
        qkey = str(quad).strip().upper()

        # Known chip/bus issue: MAT 4..7 must NEVER be accessed directly.
        # Always render them from cache / synthesized state.
        if 4 <= int(mat_id) <= 7:
            qc = self._mat_snapshot_cache.setdefault(qkey, {})
            ent = qc.get(int(mat_id))
            if not isinstance(ent, dict):
                ent = {}
                qc[int(mat_id)] = ent
            po = ent.get("pix_on")
            fo = ent.get("fe_on")
            ft = ent.get("ftdac")
            if not isinstance(po, list) or len(po) < 64:
                po = [False] * 64
            if not isinstance(fo, list) or len(fo) < 64:
                fo = [True] * 64
            if not isinstance(ft, list) or len(ft) < 64:
                ft = [0] * 64
            # If quadrant ALL toggles exist, use them as best-effort truth.
            v = self._quad_all_vars.get(qkey, {})
            try:
                if "px" in v and isinstance(v["px"], tk.BooleanVar):
                    po = [bool(v["px"].get())] * 64
            except Exception:
                pass
            try:
                if "fe" in v and isinstance(v["fe"], tk.BooleanVar):
                    fo = [bool(v["fe"].get())] * 64
            except Exception:
                pass
            ent["pix_on"] = po
            ent["fe_on"] = fo
            ent["ftdac"] = ft
            pix_on, fe_on, ftdac = po, fo, ft
        else:

            # Se abbiamo fatto uno snapshot di avvio, usalo per rendere immediata la view.
            cached: Optional[dict[str, object]] = None
            if self._mat_snapshot_prefill_active:
                cached = self._mat_snapshot_cache.get(qkey, {}).get(int(mat_id))

            if isinstance(cached, dict):
                pix_on = cached.get("pix_on")
                fe_on = cached.get("fe_on")
                ftdac = cached.get("ftdac")
            else:
                def do() -> dict[str, object]:
                    return self.hw.readMatPixelsAndFTDAC(quad, mattonella=mat_id)

                r = self._with_hw(do, busy=f"Refreshing MAT {mat_id} (Q={quad})")
                if r is None or not isinstance(r, dict):
                    return
                pix_on = r.get("pix_on")
                fe_on = r.get("fe_on")
                ftdac = r.get("ftdac")

        if not (
            isinstance(pix_on, list)
            and (fe_on is None or isinstance(fe_on, list))
            and isinstance(ftdac, list)
            and len(pix_on) == 64
            and len(ftdac) == 64
        ):
            return

        # Sync per-MAT ALL toggle checkboxes to the *actual* chip/cache state.
        try:
            vars_map = getattr(self, "_mat_all_vars", {}).get((str(qkey), int(mat_id)), None)
            if isinstance(vars_map, dict):
                # FEON
                if isinstance(fe_on, list) and len(fe_on) == 64 and "fe" in vars_map:
                    try:
                        vars_map["fe"].set(all(bool(x) for x in fe_on))
                    except Exception:
                        pass
                # PIXON
                if isinstance(pix_on, list) and len(pix_on) == 64 and "px" in vars_map:
                    try:
                        vars_map["px"].set(all(bool(x) for x in pix_on))
                    except Exception:
                        pass
                # TDCON (single bit per MAT) – best effort readback for direct-accessible MATs
                if 0 <= int(mat_id) <= 15 and not (4 <= int(mat_id) <= 7) and "td" in vars_map:
                    try:
                        td = bool(self.hw.readEnableTDC(qkey, Mattonella=int(mat_id)).get("tdc_on", False))
                        vars_map["td"].set(bool(td))
                    except Exception:
                        pass
        except Exception:
            pass
        canvas = self._block_pix_canvas.get(mat_id)
        rects = self._block_pix_cells.get(mat_id)
        if canvas is None or not rects or len(rects) != 64:
            return
        txts = self._block_pix_txt_ids.get(mat_id) or []
        for pix in range(64):
            on = bool(pix_on[pix])
            fe = True if not isinstance(fe_on, list) else bool(fe_on[pix])
            code = int(ftdac[pix])
            self._update_pixel_fill_state(int(mat_id), int(pix), pix_on=on, fe_on=fe)
            if pix < len(txts) and txts[pix]:
                try:
                    canvas.itemconfigure(txts[pix], text=str(code))
                except tk.TclError:
                    pass

    def _refresh_block(self, block_id: int) -> None:
        quad = self.quad_var.get()
        mats = self.mapping.mats_in_block(block_id)
        self._set_status(f"Refreshing block {block_id} (Q={quad})")

        # Cancel pending cache-disable to keep snapshot consistent across rapid opens/resize refreshes.
        if self._mat_snapshot_disable_after_id is not None:
            try:
                self.after_cancel(self._mat_snapshot_disable_after_id)
            except Exception:
                pass
            self._mat_snapshot_disable_after_id = None

        # Debug: capire se lo snapshot pre-caricato viene davvero usato.
        if self._mat_snapshot_prefill_active and not self.offline:
            qkey = str(quad).strip().upper()
            cached_for_quad = self._mat_snapshot_cache.get(qkey, {})
            hit = sum(1 for mid in mats if int(mid) in cached_for_quad)
            _dbg(f"Block refresh {block_id} (quad={qkey}) prefill_active=1 cache_hit={hit}/{len(mats)}")
            for mid in mats:
                _dbg(
                    f"  MAT {int(mid)} cache={'hit' if int(mid) in cached_for_quad else 'miss'} (prefill={self._mat_snapshot_prefill_active})"
                )
        else:
            if not self.offline:
                _dbg(f"Block refresh {block_id} (quad={str(quad).strip().upper()}) prefill_active=0")

        for mat_id in mats:
            self._refresh_mat_mini(mat_id)
        # refresh analog mini (uses the same underlying refresh function)
        # When prefilled from file, don't block opening the page on HW I2C (mux can "stack").
        if self._mat_snapshot_prefill_active and self._mat_snapshot_prefill_from_file:
            _dbg(f"Skipping analog refresh on block open (file-prefill active) block={block_id} quad={str(quad).strip().upper()}")
        else:
            try:
                self._refresh_analog(block_id)
            except Exception:
                pass

        # Gestione disattivazione cache:
        # - se la cache è pre-riempita DA FILE, mantenendola attiva evitiamo letture HW ripetute e
        #   la GUI resta coerente col file.
        # - se invece la cache viene da snapshot "hardware", la disattiviamo dopo la prima apertura
        #   per evitare discrepanze se l'hardware cambia.
        if not getattr(self, "_mat_snapshot_prefill_from_file", False):
            # Dopo la prima apertura/sincronizzazione di una block view, non riusare più la cache
            # (per evitare discrepanze se l'hardware cambia).
            # Però: durante l'apertura ci sono refresh automatici (resize/Configure). Tenere attiva la
            # cache per una breve finestra evita letture HW ripetute e rende "snapshot" più immediato.
            if self._mat_snapshot_disable_after_id is not None:
                try:
                    self.after_cancel(self._mat_snapshot_disable_after_id)
                except Exception:
                    pass
                self._mat_snapshot_disable_after_id = None

            def _disable_cache() -> None:
                self._mat_snapshot_prefill_active = False
                self._mat_snapshot_disable_after_id = None
                _dbg("mat snapshot prefill flag disabled (grace period over)")

            self._mat_snapshot_disable_after_id = self.after(350, _disable_cache)
        self._set_status(f"Block {block_id} refreshed (Q={quad})")

    def _build_analog_mini(self, parent: ttk.Frame, block_id: int, *, kind: str) -> ttk.Frame:
        owner = self.mapping.analog_owner_mat(block_id)
        for attr in ("_afe_csa_var", "_afe_disc_var", "_afe_krum_var"):
            if hasattr(self, attr):
                try:
                    delattr(self, attr)
                except Exception:
                    pass

        frm = ttk.Labelframe(parent, text=f"Analog Column (MAT {owner})", padding=4)

        kind_u = str(kind).strip().upper()
        is_ldo = "LDO" in kind_u and "NOLDO" not in kind_u
        is_lp = "LP" in kind_u
        # soft colors, similar to datasheet highlighting
        hdr_bg = "#dff3df" if is_ldo else "#f3efe0"
        hdr_fg = "#1f1f1f"
        hdr = tk.Label(
            frm,
            text=f"{kind}  |  analog services",
            bg=hdr_bg,
            fg=hdr_fg,
            anchor="w",
            padx=6,
            pady=3,
            font=("Segoe UI", 9, "bold"),
        )
        hdr.pack(fill="x", pady=(0, 4))

        bar = ttk.Frame(frm)
        bar.pack(fill="x", pady=(0, 4))
        ttk.Button(bar, text="Read", command=lambda: self._refresh_analog(block_id)).pack(side="left")

        # Blocchi LP: correnti AFE (reg68: I_DISC + I_CSA 3 bit ciascuno; reg69 low nibble I_KRUM 4 bit).
        if is_lp:
            bias_frm = ttk.Labelframe(
                frm,
                text="Correnti AFE — I_CSA (3 bit) · I_DISC (3 bit) · I_KRUM (4 bit)",
                padding=4,
            )
            bias_frm.pack(fill="x", pady=(0, 8))
            self._afe_csa_var = tk.StringVar(value="0")
            self._afe_disc_var = tk.StringVar(value="0")
            self._afe_krum_var = tk.StringVar(value="0")
            br = ttk.Frame(bias_frm)
            br.pack(fill="x")
            ttk.Label(br, text="I_CSA:", width=8).pack(side="left")
            ttk.Spinbox(br, from_=0, to=7, textvariable=self._afe_csa_var, width=5).pack(side="left", padx=(0, 10))
            ttk.Label(br, text="I_DISC:", width=8).pack(side="left")
            ttk.Spinbox(br, from_=0, to=7, textvariable=self._afe_disc_var, width=5).pack(side="left", padx=(0, 10))
            ttk.Label(br, text="I_KRUM:", width=8).pack(side="left")
            ttk.Spinbox(br, from_=0, to=15, textvariable=self._afe_krum_var, width=5).pack(side="left", padx=(0, 10))
            ttk.Button(br, text="Apply", command=lambda: self._set_afe_bias(block_id)).pack(side="left", padx=6)

        # Reuse the same state variables used by the full analog view.
        self._dac_vars = {}
        self._dac_en_vars = {}
        self._dac_pow_vars = {}
        self._dac_meas_vars = {}
        self._dac_set_vars: dict[str, tk.StringVar] = {}
        self._vinjh_var = tk.StringVar(value="?")
        self._vinjl_var = tk.StringVar(value="?")
        self._c2p_var = tk.BooleanVar(value=False)
        self._vdda_var = tk.StringVar(value="?")
        # Keep references to mux buttons to highlight selected state.
        self._vinj_btns = {"VinjH": {}, "VinjL": {}}

        # DAC controls (compact 2-column layout)
        dacs = ["VTHR_H", "VTHR_L", "VINJ_H", "VINJ_L", "VLDO", "VFB"]
        dac_grid = ttk.Frame(frm)
        dac_grid.pack(fill="x", pady=(0, 2))
        for c in (0, 1):
            dac_grid.grid_columnconfigure(c, weight=1, uniform="daccol")

        bold_code_font = ("Segoe UI", 10, "bold")
        meas_font = ("Segoe UI", 8)

        for i, name in enumerate(dacs):
            r, c = divmod(i, 2)
            cell = ttk.Frame(dac_grid, padding=(2, 1))
            cell.grid(row=r, column=c, sticky="ew", padx=(0, 6) if c == 0 else (0, 0), pady=(0, 2))

            v = tk.StringVar(value="?")
            self._dac_vars[name] = v
            en = tk.BooleanVar(value=False)
            self._dac_en_vars[name] = en
            pw = tk.BooleanVar(value=False)
            self._dac_pow_vars[name] = pw
            mv = tk.StringVar(value="")
            self._dac_meas_vars[name] = mv

            top = ttk.Frame(cell)
            top.pack(fill="x")
            # Keep enough room for labels like "VTHR_H" without overlapping the value.
            ttk.Label(top, text=name, width=8).pack(side="left", padx=(0, 4))
            tk.Label(top, textvariable=v, font=bold_code_font, width=4, anchor="w").pack(side="left", padx=(0, 10))
            ttk.Checkbutton(
                top,
                text="EN_POW",
                variable=pw,
                command=lambda n=name: self._set_dac_pow(block_id, n),
            ).pack(side="right", padx=(6, 0))
            ttk.Checkbutton(
                top,
                text="EN",
                variable=en,
                command=lambda n=name: self._set_dac_enable(block_id, n),
            ).pack(side="right")

            bottom = ttk.Frame(cell)
            bottom.pack(fill="x", pady=(1, 0))
            sv = tk.StringVar(value="")
            self._dac_set_vars[name] = sv
            entry = ttk.Entry(bottom, width=5, textvariable=sv)
            entry.pack(side="left")
            ttk.Button(bottom, text="Set", width=5, command=lambda n=name, e=entry: self._set_dac_code(block_id, n, e.get())).pack(
                side="left", padx=(4, 0)
            )
            # Optional measurement / extra info line (usually empty unless you "Set")
            if mv is not None:
                tk.Label(cell, textvariable=mv, font=meas_font, fg="#5a5a5a", anchor="w").pack(anchor="w")

        # Mux + connect2pad
        mux = ttk.Labelframe(frm, text="VinjMux / C2P", padding=3)
        mux.pack(fill="x", pady=(4, 2))

        r1 = ttk.Frame(mux)
        r1.pack(fill="x", pady=2)
        ttk.Label(r1, text="VinjH:", width=6).pack(side="left")
        ttk.Label(r1, textvariable=self._vinjh_var, width=6).pack(side="left")
        b = tk.Button(r1, text="dac", width=6, command=lambda: self._set_vinj(block_id, "VinjH", "dac"))
        b.pack(side="left", padx=3)
        self._vinj_btns["VinjH"]["dac"] = b
        b = tk.Button(r1, text="VDDA", width=6, command=lambda: self._set_vinj(block_id, "VinjH", "VDDA"))
        b.pack(side="left", padx=3)
        self._vinj_btns["VinjH"]["VDDA"] = b

        r2 = ttk.Frame(mux)
        r2.pack(fill="x", pady=2)
        ttk.Label(r2, text="VinjL:", width=6).pack(side="left")
        ttk.Label(r2, textvariable=self._vinjl_var, width=6).pack(side="left")
        b = tk.Button(r2, text="dac", width=6, command=lambda: self._set_vinj(block_id, "VinjL", "dac"))
        b.pack(side="left", padx=3)
        self._vinj_btns["VinjL"]["dac"] = b
        b = tk.Button(r2, text="GNDA", width=6, command=lambda: self._set_vinj(block_id, "VinjL", "GNDA"))
        b.pack(side="left", padx=3)
        self._vinj_btns["VinjL"]["GNDA"] = b

        ttk.Checkbutton(
            mux,
            text="Connect2Pad (only this block)",
            variable=self._c2p_var,
            command=lambda: self._set_connect2pad(block_id, self._c2p_var.get()),
        ).pack(anchor="w", pady=(6, 0))

        vdda = ttk.Labelframe(frm, text="VDDA", padding=3)
        vdda.pack(fill="x", pady=(6, 0))
        vrow = ttk.Frame(vdda)
        vrow.pack(fill="x", pady=(0, 4))
        ttk.Label(vrow, text="VDDA =").pack(side="left")
        tk.Label(vrow, textvariable=self._vdda_var, font=("Segoe UI", 12, "bold")).pack(side="left", padx=(6, 0))
        ttk.Label(vrow, text="mV").pack(side="left", padx=(4, 0))
        ttk.Button(vdda, text="Measure VDDA", command=lambda: self._measure_vdda(block_id)).pack(anchor="w", pady=(2, 0))

        return frm

    def _open_mat(self, mat_id: int) -> None:
        self._push_view(self._build_mat_view(self.content, mat_id))

    def _build_mat_view(self, parent: ttk.Frame, mat_id: int) -> ttk.Frame:
        frm = ttk.Frame(parent)
        quad = self.quad_var.get()
        ttk.Label(frm, text=f"Quadrant {quad} → MAT {mat_id}", font=("Segoe UI", 16, "bold")).pack(
            anchor="w", pady=(0, 10)
        )

        toolbar = ttk.Frame(frm)
        toolbar.pack(fill="x", pady=(0, 8))
        ttk.Button(toolbar, text="Refresh", command=lambda: self._refresh_pixels(mat_id)).pack(side="left", padx=6)

        self._pix_cells: list[tk.Label] = []
        grid = ttk.Frame(frm)
        grid.pack(anchor="w")

        for r in range(8):
            for c in range(8):
                pix_id = r * 8 + c
                lbl = tk.Label(
                    grid,
                    text=f"{pix_id:02d}",
                    width=4,
                    relief="solid",
                    borderwidth=1,
                    bg="#aa2222",  # default OFF
                    fg="white",
                )
                lbl.grid(row=r, column=c, padx=2, pady=2)
                lbl.bind("<Button-1>", lambda _ev, p=pix_id: self._toggle_pixel(mat_id, p))
                self._pix_cells.append(lbl)

        self._refresh_pixels(mat_id)
        return frm

    def _refresh_pixels(self, mat_id: int) -> None:
        quad = self.quad_var.get()
        # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
        if 4 <= int(mat_id) <= 7:
            self._set_status(f"MAT {mat_id}: refresh disabled (I2C stack issue). Use broadcast / Calib DCO.")
            return

        def do() -> list[bool]:
            self.hw.select_quadrant(quad)
            states: list[bool] = []
            for pix in range(64):
                states.append(bool(self.hw.readAnalogChannelON(quad, mattonella=mat_id, canale=pix)))
            return states

        states = self._with_hw(do, busy=f"Refreshing pixels (Q={quad} MAT={mat_id})")
        if states is None:
            return
        for pix, on in enumerate(states):
            self._pix_cells[pix].configure(bg=("#1f9d55" if on else "#aa2222"))
        self._set_status(f"Pixels refreshed (Q={quad} MAT={mat_id})")

    def _toggle_pixel(self, mat_id: int, pix_id: int) -> None:
        quad = self.quad_var.get()
        # Known chip/bus issue: MAT 4..7 addressed individually may stack the I2C bus.
        if 4 <= int(mat_id) <= 7:
            self._set_status(f"MAT {mat_id}: toggle disabled (I2C stack issue). Use broadcast / Calib DCO.")
            return

        def do() -> bool:
            self.hw.select_quadrant(quad)
            cur = bool(self.hw.readAnalogChannelON(quad, mattonella=mat_id, canale=pix_id))
            if cur:
                self.hw.AnalogChannelOFF(quad, mattonella=mat_id, canale=pix_id)
            else:
                self.hw.AnalogChannelON(quad, mattonella=mat_id, canale=pix_id)
            return not cur

        new_state = self._with_hw(do, busy=f"Toggling pixel {pix_id} (Q={quad} MAT={mat_id})")
        if new_state is None:
            return
        self._pix_cells[pix_id].configure(bg=("#1f9d55" if new_state else "#aa2222"))
        self._set_status(f"Pixel {pix_id}={'ON' if new_state else 'OFF'} (Q={quad} MAT={mat_id})")

    def _open_analog(self, block_id: int) -> None:
        self._push_view(self._build_analog_view(self.content, block_id))

    def _build_analog_view(self, parent: ttk.Frame, block_id: int) -> ttk.Frame:
        frm = ttk.Frame(parent)
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)
        ttk.Label(frm, text=f"Quadrant {quad} → Block {block_id} → Analog Column", font=("Segoe UI", 16, "bold")).pack(
            anchor="w", pady=(0, 10)
        )
        ttk.Label(frm, text=f"Analog owner MAT = {owner}").pack(anchor="w", pady=(0, 10))

        toolbar = ttk.Frame(frm)
        toolbar.pack(fill="x", pady=(0, 10))
        ttk.Button(toolbar, text="Read", command=lambda: self._refresh_analog(block_id)).pack(side="left", padx=6)

        body = ttk.Frame(frm)
        body.pack(fill="both", expand=True)

        left = ttk.Labelframe(body, text="DACs", padding=10)
        left.pack(side="left", fill="y", padx=(0, 10))

        right = ttk.Labelframe(body, text="Mux / Connect2Pad", padding=10)
        right.pack(side="left", fill="both", expand=True)

        self._dac_vars: dict[str, tk.StringVar] = {}
        self._dac_en_vars: dict[str, tk.BooleanVar] = {}
        self._dac_pow_vars: dict[str, tk.BooleanVar] = {}
        self._dac_meas_vars: dict[str, tk.StringVar] = {}
        self._dac_set_vars: dict[str, tk.StringVar] = {}

        dacs = ["VTHR_H", "VTHR_L", "VINJ_H", "VINJ_L", "VLDO", "VFB"]
        for i, name in enumerate(dacs):
            row = ttk.Frame(left)
            row.pack(fill="x", pady=4)
            ttk.Label(row, text=name, width=9).pack(side="left")
            v = tk.StringVar(value="?")
            self._dac_vars[name] = v
            ttk.Label(row, textvariable=v, width=10).pack(side="left", padx=(6, 6))
            en = tk.BooleanVar(value=False)
            self._dac_en_vars[name] = en
            pw = tk.BooleanVar(value=False)
            self._dac_pow_vars[name] = pw
            ttk.Checkbutton(row, text="EN", variable=en, command=lambda n=name: self._set_dac_enable(block_id, n)).pack(
                side="left", padx=(6, 0)
            )
            ttk.Checkbutton(row, text="EN_POW", variable=pw, command=lambda n=name: self._set_dac_pow(block_id, n)).pack(
                side="left", padx=(6, 0)
            )
            sv = tk.StringVar(value="")
            self._dac_set_vars[name] = sv
            entry = ttk.Entry(row, width=6, textvariable=sv)
            entry.pack(side="left", padx=(10, 6))
            ttk.Button(
                row,
                text="Set",
                command=lambda n=name, e=entry: self._set_dac_code(block_id, n, e.get()),
            ).pack(side="left")
            mv = tk.StringVar(value="")
            self._dac_meas_vars[name] = mv
            ttk.Label(row, textvariable=mv, width=12).pack(side="left", padx=(10, 0))

        # Vinj mux + connect2pad
        self._vinjh_var = tk.StringVar(value="?")
        self._vinjl_var = tk.StringVar(value="?")
        self._c2p_var = tk.BooleanVar(value=False)
        # Keep references to mux buttons to highlight selected state.
        self._vinj_btns: dict[str, dict[str, tk.Button]] = {"VinjH": {}, "VinjL": {}}

        mux_row = ttk.Frame(right)
        mux_row.pack(fill="x", pady=6)
        ttk.Label(mux_row, text="VinjH mux:", width=10).pack(side="left")
        ttk.Label(mux_row, textvariable=self._vinjh_var, width=10).pack(side="left", padx=(6, 10))
        b = tk.Button(mux_row, text="dac", width=6, command=lambda: self._set_vinj(block_id, "VinjH", "dac"))
        b.pack(side="left")
        self._vinj_btns["VinjH"]["dac"] = b
        b = tk.Button(mux_row, text="VDDA", width=6, command=lambda: self._set_vinj(block_id, "VinjH", "VDDA"))
        b.pack(side="left", padx=(6, 0))
        self._vinj_btns["VinjH"]["VDDA"] = b

        mux_row2 = ttk.Frame(right)
        mux_row2.pack(fill="x", pady=6)
        ttk.Label(mux_row2, text="VinjL mux:", width=10).pack(side="left")
        ttk.Label(mux_row2, textvariable=self._vinjl_var, width=10).pack(side="left", padx=(6, 10))
        b = tk.Button(mux_row2, text="dac", width=6, command=lambda: self._set_vinj(block_id, "VinjL", "dac"))
        b.pack(side="left")
        self._vinj_btns["VinjL"]["dac"] = b
        b = tk.Button(mux_row2, text="GNDA", width=6, command=lambda: self._set_vinj(block_id, "VinjL", "GNDA"))
        b.pack(side="left", padx=(6, 0))
        self._vinj_btns["VinjL"]["GNDA"] = b

        c2p_row = ttk.Frame(right)
        c2p_row.pack(fill="x", pady=10)
        ttk.Checkbutton(
            c2p_row,
            text="Connect2Pad (only this block)",
            variable=self._c2p_var,
            command=lambda: self._set_connect2pad(block_id, self._c2p_var.get()),
        ).pack(side="left")

        ttk.Separator(right).pack(fill="x", pady=10)
        # VDDA measurement (shows result in mV below)
        if not hasattr(self, "_vdda_var"):
            self._vdda_var = tk.StringVar(value="?")  # type: ignore[attr-defined]
        vdda_box = ttk.Labelframe(right, text="VDDA", padding=8)
        vdda_box.pack(fill="x", pady=(0, 6))
        vrow = ttk.Frame(vdda_box)
        vrow.pack(fill="x", pady=(0, 4))
        ttk.Label(vrow, text="VDDA =").pack(side="left")
        tk.Label(vrow, textvariable=self._vdda_var, font=("Segoe UI", 12, "bold")).pack(side="left", padx=(6, 0))
        ttk.Label(vrow, text="mV").pack(side="left", padx=(4, 0))
        ttk.Button(vdda_box, text="Measure VDDA", command=lambda: self._measure_vdda(block_id)).pack(anchor="w")

        self._refresh_analog(block_id)
        return frm

    def _refresh_analog(self, block_id: int) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)

        def do() -> dict[str, object]:
            self.hw.select_quadrant(quad)
            out: dict[str, object] = {}
            pow_out: dict[str, bool] = {}
            for name in ["VTHR_H", "VTHR_L", "VINJ_H", "VINJ_L", "VLDO", "VFB"]:
                out[name] = self.hw.readAnalogColumnDAC(quad, block=owner, dac=name)
                try:
                    pow_out[name] = bool(self.hw.readAnalogColumnENPOW(quad, block=owner, dac=name))
                except Exception:
                    pow_out[name] = False
            out["pow"] = pow_out
            out["vinj"] = self.hw.readAnalogColumnVinjMux(quad, block=owner)
            out["c2p"] = bool(self.hw.readAnalogColumnConnect2PAD(quad, block=owner))
            if hasattr(self, "_afe_csa_var"):
                try:
                    _ = self._afe_csa_var.get()
                    out["bias"] = self.hw.readAnalogColumnBiasCell(quad, block=owner)
                except Exception:
                    pass
            return out

        r = self._with_hw(do, busy=f"Refreshing analog column (Q={quad} block={block_id} ownerMAT={owner})")
        if r is None:
            return
        for name, val in r.items():
            if name in {"vinj", "c2p", "bias", "pow"} or str(name).startswith("meas_"):
                continue
            d = val  # type: ignore[assignment]
            try:
                enable = bool(d["enable"])  # type: ignore[index]
                code = int(d["code"])  # type: ignore[index]
                self._dac_vars[name].set(f"{code}")
                self._dac_en_vars[name].set(enable)
                # Prefill the Set-entry with current code for quick repeated Set clicks.
                try:
                    sv = getattr(self, "_dac_set_vars", {}).get(str(name))
                    if isinstance(sv, tk.StringVar):
                        sv.set(str(int(code)))
                except Exception:
                    pass
            except Exception:
                self._dac_vars[name].set("?")
                self._dac_en_vars[name].set(False)
                try:
                    self._dac_pow_vars[name].set(False)
                except Exception:
                    pass
                try:
                    self._dac_meas_vars[name].set("")  # type: ignore[index]
                except Exception:
                    pass

        # EN_POW flags
        pow_d = r.get("pow")
        if isinstance(pow_d, dict):
            for name, v in pow_d.items():
                if str(name) in getattr(self, "_dac_pow_vars", {}):
                    try:
                        self._dac_pow_vars[str(name)].set(bool(v))
                    except Exception:
                        pass

        vinj = r.get("vinj", {})  # type: ignore[assignment]
        try:
            vinjh = str(vinj["VinjH"])  # type: ignore[index]
            vinjl = str(vinj["VinjL"])  # type: ignore[index]
            self._vinjh_var.set(vinjh)
            self._vinjl_var.set(vinjl)
        except Exception:
            vinjh = "?"
            vinjl = "?"
            self._vinjh_var.set("?")
            self._vinjl_var.set("?")
        # Highlight selected mux buttons (best-effort).
        try:
            on_bg = "#1f9d55"
            off_bg = "#e0e0e0"
            for k, b in getattr(self, "_vinj_btns", {}).get("VinjH", {}).items():
                b.configure(bg=(on_bg if str(k) == str(vinjh) else off_bg))
            for k, b in getattr(self, "_vinj_btns", {}).get("VinjL", {}).items():
                b.configure(bg=(on_bg if str(k) == str(vinjl) else off_bg))
        except Exception:
            pass
        self._c2p_var.set(bool(r.get("c2p", False)))

        bias = r.get("bias")
        if isinstance(bias, dict) and hasattr(self, "_afe_csa_var"):
            try:
                self._afe_csa_var.set(str(int(bias["csa"])))
                self._afe_disc_var.set(str(int(bias["disc"])))
                self._afe_krum_var.set(str(int(bias["krum"])))
            except Exception:
                pass

        self._set_status(f"Analog refreshed (Q={quad} block={block_id})")

    def _set_afe_bias(self, block_id: int) -> None:
        if not hasattr(self, "_afe_csa_var"):
            return
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)
        try:
            csa = int(str(self._afe_csa_var.get()).strip())
            disc = int(str(self._afe_disc_var.get()).strip())
            krum = int(str(self._afe_krum_var.get()).strip())
        except Exception:
            self._set_status("I_CSA / I_DISC / I_KRUM: valori non numerici")
            return
        if csa < 0 or csa > 7 or disc < 0 or disc > 7 or krum < 0 or krum > 15:
            self._set_status("Range: I_CSA e I_DISC 0..7 (3 bit), I_KRUM 0..15 (4 bit)")
            return

        def do() -> None:
            self.hw.AnalogColumnBiasCell(quad, block=owner, csa=csa, disc=disc, krum=krum)

        self._with_hw(do, busy=f"AFE bias MAT {owner} (Q={quad})")
        self._refresh_analog(block_id)

    def _set_dac_code(self, block_id: int, dac: str, code_s: str) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)
        try:
            code = int(str(code_s).strip())
        except Exception:
            self._set_status("Invalid DAC code")
            return

        def do() -> dict[str, object]:
            self.hw.AnalogColumnSetDAC(quad, block=owner, dac=dac, valore=code)
            rb = self.hw.readAnalogColumnDAC(quad, block=owner, dac=dac)
            ch = self._adc_channel_for_dac(dac)
            meas_v = None
            meas_mv = None
            if ch is not None:
                # Do NOT force "only this block" probe routing here:
                # it can unexpectedly disable EN_CON_PAD / EN_POW on other blocks.
                # Give analog mux/probe some settle time before sampling.
                time.sleep(0.02)
                r = self.hw._adc_quad_oneshot(channel=ch, gain=0, res_bits=16, delay_s=0.03)  # type: ignore[attr-defined]
                try:
                    meas_v = float(r["value"])
                except Exception:
                    meas_v = None
                try:
                    meas_mv = float(r.get("value_mv"))  # type: ignore[arg-type]
                except Exception:
                    meas_mv = None
            return {"readback": rb, "meas_v": meas_v, "meas_mv": meas_mv}

        r = self._with_hw(do, busy=f"Set {dac}={code} + verify (Q={quad} ownerMAT={owner})")
        if isinstance(r, dict):
            try:
                rb = r.get("readback", {})
                rb_code = int(rb.get("code"))  # type: ignore[arg-type]
                meas_v = r.get("meas_v", None)
                meas_mv = r.get("meas_mv", None)
                if meas_v is None:
                    self._set_status(f"{dac}: set={code} readback={rb_code} (no ADC mapping)")
                else:
                    if meas_mv is None:
                        self._set_status(f"{dac}: set={code} readback={rb_code} meas={float(meas_v):.6g} V")
                    else:
                        self._set_status(
                            f"{dac}: set={code} readback={rb_code} meas={float(meas_v):.6g} V ({float(meas_mv):.6g} mV)"
                        )
                    try:
                        if meas_mv is None:
                            self._dac_meas_vars[dac.strip().upper()].set(f"{float(meas_v):.4g} V")  # type: ignore[index]
                        else:
                            self._dac_meas_vars[dac.strip().upper()].set(
                                f"{float(meas_v):.4g} V / {float(meas_mv):.4g} mV"
                            )  # type: ignore[index]
                    except Exception:
                        pass
            except Exception:
                pass
        self._refresh_analog(block_id)

    def _set_dac_enable(self, block_id: int, dac: str) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)
        en = bool(self._dac_en_vars[dac].get())

        def do() -> None:
            self.hw.AnalogColumnDACon(quad, block=owner, dac=dac, valore=en)

        self._with_hw(do, busy=f"{'Enable' if en else 'Disable'} {dac} (Q={quad} ownerMAT={owner})")
        self._refresh_analog(block_id)

    def _set_dac_pow(self, block_id: int, dac: str) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)
        pw = bool(self._dac_pow_vars[dac].get())

        def do() -> None:
            self.hw.AnalogColumnENPOW(quad, block=owner, dac=dac, valore=pw)

        self._with_hw(do, busy=f"{'Enable' if pw else 'Disable'} EN_POW {dac} (Q={quad} ownerMAT={owner})")
        self._refresh_analog(block_id)

    def _set_vinj(self, block_id: int, vinj: str, valore: str) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)

        def do() -> None:
            self.hw.AnalogColumnVinjMux(quad, block=owner, vinj=vinj, valore=valore)

        self._with_hw(do, busy=f"Set {vinj} mux={valore} (Q={quad} ownerMAT={owner})")
        self._refresh_analog(block_id)

    def _set_connect2pad(self, block_id: int, enable: bool) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)

        def do() -> None:
            # Do not enforce "only this block" exclusivity: it may disable other blocks' analog probing.
            # Only touch the current quadrant + owner MAT.
            self.hw.AnalogColumnConnect2PAD(quad, block=owner, valore=bool(enable))

        self._with_hw(do, busy=f"Connect2Pad={'ON' if enable else 'OFF'} (Q={quad} ownerMAT={owner})")
        self._refresh_analog(block_id)

    def _measure_vdda(self, block_id: int) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)

        def do() -> float:
            return float(self.hw.measureVDDA(quad, block=owner))

        v = self._with_hw(do, busy=f"Measuring VDDA (Q={quad} ownerMAT={owner})")
        if v is None:
            return
        v_mv = float(v) * 1000.0
        try:
            self._vdda_var.set(f"{float(v_mv):.6g}")  # type: ignore[attr-defined]
        except Exception:
            pass
        self._set_status(f"VDDA={float(v_mv):.6g} mV (Q={quad} block={block_id})")


def run_gui(
    *,
    start_config: object = True,
    default_quad: str = "SW",
    base_config_file: Optional[str] = None,
    si5340_config_file: Optional[str] = None,
) -> None:
    offline = _env_truthy("OFFLINE", "0")
    # Ensure native DLLs can be resolved: prefer the repo `icboost/` directory.
    # (Both TCPtoI2C.dll and USBtoI2C32.dll live there in this repo.)
    try:
        _repo_dir = Path(__file__).resolve().parents[1]
    except Exception:
        _repo_dir = None
    hw = Ignite64(dll_dir=(str(_repo_dir) if _repo_dir is not None else None))
    q0 = str(default_quad).strip().upper()

    def _resolve_full_cfg_path() -> Optional[Path]:
        try:
            cfg_base = Path(__file__).resolve().parents[1] / "ConfigurationFiles"
            default_full_cfg_name = "IGNITE64_configSW_26.04.29.12.31.45.txt"
            if base_config_file:
                p = Path(str(base_config_file))
                return p if (p.is_absolute() or len(p.parts) > 1) else (cfg_base / p)
            candidates = sorted(
                cfg_base.glob("IGNITE64_configSW_*.txt"),
                key=lambda x: x.stat().st_mtime,
                reverse=True,
            )
            return candidates[0] if candidates else (cfg_base / default_full_cfg_name)
        except Exception:
            return None

    def _hw_reachable() -> bool:
        """
        Best-effort: if we can read a few registers, treat chip as "reachable" and in AUTO mode
        we will not rewrite configuration (preserve current chip state).
        """
        try:
            hw.select_quadrant(q0)
            # Minimal reachability check: TOP must respond.
            # Do NOT require MAT reads here: they can fail if readout path is not I2C yet.
            _ = int(hw.i2c_read_byte(hw.addr.top_addr, 4)) & 0xFF  # driver strength / CMM mode
            _ = int(hw.i2c_read_byte(hw.addr.top_addr, 5)) & 0xFF  # IO set selection (invtx/invrx/fe_pol…)
            return True
        except Exception:
            return False

    sc_mode = str(start_config).strip().lower() if isinstance(start_config, str) else ""
    do_start_config = bool(start_config) if not isinstance(start_config, str) else (sc_mode not in {"0", "false", "no", "off", ""})
    auto_mode = isinstance(start_config, str) and sc_mode == "auto"
    _dbg(
        f"START_CONFIG decision: arg={start_config!r} offline={int(offline)} do_start_config={int(do_start_config)} auto_mode={int(auto_mode)}"
    )

    did_write_config = False
    if (not offline) and do_start_config:
        reachable = False
        if auto_mode:
            # AUTO mode: init HW transport first, then decide.
            try:
                hw.init_hw(
                    q0,
                    full_cfg=base_config_file,
                    si5340_cfg=str(si5340_config_file or "Si5340-RevD_Crystal-Registers_bis.txt"),
                    # IMPORTANT: AUTO must be non-invasive.
                    # If the setup is already configured (clock + TOP/MAT), do not reprogram IOext/clock/unlock.
                    do_ioext_defaults=False,
                    do_clock=False,
                    do_unlock_top_dc=False,
                )
            except Exception as e:
                _dbg(f"AUTO init_hw failed: {e!r}")
            reachable = _hw_reachable()
            _dbg(f"AUTO reachability: {int(reachable)} (default_quad={q0})")
            if reachable:
                _dbg(f"start config skipped (AUTO: preserve current chip state) (default_quad={q0})")
            else:
                _dbg(f"start config will run (AUTO: chip not reachable) (default_quad={q0})")
        if auto_mode and reachable:
            pass
        else:
            kwargs: dict[str, object] = {}
            if base_config_file:
                kwargs["full_cfg"] = base_config_file
            if si5340_config_file:
                kwargs["si5340_cfg"] = si5340_config_file
            _dbg(f"start config loaded (default_quad={default_quad})")
            hw.start_config(default_quad, **kwargs)  # type: ignore[arg-type]
            did_write_config = True
    app = Ignite64Gui(hw, default_quad=default_quad, offline=offline)

    # Manual mode by default: keep I2C bus idle unless user explicitly refreshes.
    # Set ICBOOST_AUTO_SNAPSHOT=1 to re-enable startup snapshot activity.
    if (not offline) and _env_truthy("ICBOOST_AUTO_SNAPSHOT", "0"):
        if did_write_config:
            app.after(50, lambda: app._start_initial_snapshot_prefill(default_quad, base_config_file))
        else:
            app.after(50, lambda: app._start_initial_snapshot_refresh_from_hw(default_quad))
    app.mainloop()

