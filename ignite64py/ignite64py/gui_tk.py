from __future__ import annotations

import io
import os
import pkgutil
import threading
import tkinter as tk
from dataclasses import dataclass
from tkinter import ttk, filedialog
from pathlib import Path
from typing import Optional

from .api import Ignite64
from .calib_dco import CalibDCOParams

# Prefer user-supplied JPEG in assets/, then legacy PNG name.
_DIE_PHOTO_NAMES: tuple[str, ...] = ("ignite64.jpg", "ignite64.jpeg", "ignite64_die_photo.png")

# Centered square on bitmap: linear extent ½ × ½ → area = ¼ of photo (NW/NE/SW/SE each = ¹⁄₁₆ of bitmap).
_DEFAULT_DIE_CHIP_BOX: tuple[float, float, float, float] = (0.25, 0.25, 0.75, 0.75)

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


def _bootstrap_die_photo_env() -> None:
    """
    If IGNITE_DIE_PHOTO is missing or points nowhere, set it to ignite64.jpg next to *this*
    source tree or typical checkout paths.

    When Python loads ``ignite64py`` from site-packages, ``Path(__file__).parent/assets`` may be
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
            candidates.append(cw / "ignite64py" / "ignite64py" / "assets" / name)
            candidates.append(cw / "ignite64py" / "assets" / name)
            candidates.append(cw / "assets" / name)
            candidates.append(cw / name)
    except Exception:
        pass
    for depth in (2, 3, 4):
        try:
            r = gt.parents[depth]
            for name in _DIE_PHOTO_NAMES:
                candidates.append(r / name)
                candidates.append(r / "ignite64py" / "ignite64py" / "assets" / name)
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
        self.title("IGNITE64 monitor (tk)")
        self.geometry("1200x820")

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

        self._nav_stack: list[ttk.Frame] = []

        self.quad_var = tk.StringVar(value=str(default_quad).strip().upper())
        self._top_apply_quad_var = tk.StringVar(value=str(default_quad).strip().upper())
        self.cmd_var = tk.StringVar(value="")
        self._fifo_auto_after_id: Optional[str] = None
        self._quadrants_mon_job: Optional[str] = None
        self._quadrants_mon_seq: int = 0
        self._quad_monitor_canvas: dict[str, str] = {q: "" for q in ("NW", "NE", "SW", "SE")}
        self._die_canvas_redraw: Optional[object] = None
        self._top_snapshot_texts: dict[str, tk.Text] = {}
        self._quadrants_top_mon_var = tk.StringVar(value="TOP (lettura): —")
        self._quadrants_top_mon_job: Optional[str] = None
        self._quadrants_top_mon_seq: int = 0
        self._macro_script_paths: list[Path] = []

        root = ttk.Frame(self, padding=10)
        root.pack(fill="both", expand=True)

        topbar = ttk.Frame(root)
        topbar.pack(fill="x", pady=(0, 4))

        top_left = ttk.Frame(topbar)
        top_left.pack(side="left")

        ttk.Label(top_left, text="Quadrant:").pack(side="left")
        quad_box = ttk.Combobox(top_left, textvariable=self.quad_var, values=["SW", "NW", "SE", "NE"], width=6)
        quad_box.pack(side="left", padx=(6, 12))
        quad_box.state(["readonly"])

        self.back_btn = ttk.Button(top_left, text="Back", command=self.nav_back, state="disabled")
        self.back_btn.pack(side="left", padx=(0, 8))

        ttk.Button(top_left, text="Home", command=self.nav_home).pack(side="left", padx=(0, 8))

        ttk.Separator(top_left, orient="vertical").pack(side="left", fill="y", padx=8)
        self.offline_lbl = ttk.Label(top_left, text=("OFFLINE" if self.offline else "HW"))
        self.offline_lbl.pack(side="left")

        ttk.Separator(topbar, orient="vertical").pack(side="left", fill="y", padx=8)

        # Cmd + Macro: due righe, colonna centrale larga; Calib DCO solo in alto a destra (no testo lungo qui)
        cmd_macro = ttk.Frame(topbar)
        cmd_macro.pack(side="left", fill="both", expand=True, padx=(0, 12))
        cmd_macro.columnconfigure(1, weight=1, minsize=480)

        ttk.Label(cmd_macro, text="Cmd:").grid(row=0, column=0, sticky="e", padx=(0, 6))
        cmd_entry = ttk.Entry(cmd_macro, textvariable=self.cmd_var, width=72)
        cmd_entry.grid(row=0, column=1, sticky="ew")
        ttk.Button(cmd_macro, text="Run", command=self.run_command).grid(row=0, column=2, padx=(8, 0))
        cmd_entry.bind("<Return>", lambda _ev: self.run_command())

        ttk.Label(cmd_macro, text="Macro:").grid(row=1, column=0, sticky="e", padx=(0, 6), pady=(6, 0))
        self._macro_file_cb = ttk.Combobox(cmd_macro, state="readonly", width=70)
        self._macro_file_cb.grid(row=1, column=1, sticky="ew", pady=(6, 0))
        ttk.Button(cmd_macro, text="Source", command=self._run_macro_source_button).grid(row=1, column=2, padx=(8, 0), pady=(6, 0))

        self._refresh_macro_file_list()
        self._macro_file_cb.bind("<<ComboboxSelected>>", self._on_macro_file_selected)

        ttk.Button(topbar, text="Calib DCO…", command=self._calib_dco_dialog).pack(side="right", padx=(4, 0))

        self.content = ttk.Frame(root)
        self.content.pack(fill="both", expand=True)

        self.quad_var.trace_add("write", lambda *_a: self.after_idle(self._touch_quadrants_top_mon_from_trace))

        self.nav_home()

    def _iter_die_photo_asset_dirs(self) -> list[Path]:
        """Directories that may contain die photos (order matters)."""
        dirs: list[Path] = []
        here = Path(__file__).resolve().parent
        dirs.append(here / "assets")

        try:
            import ignite64py as _pkg

            dirs.append(Path(_pkg.__file__).resolve().parent / "assets")
        except Exception:
            pass

        d = here
        for _ in range(10):
            dirs.append(d / "assets")
            dirs.append(d / "ignite64py" / "ignite64py" / "assets")
            if d.parent == d:
                break
            d = d.parent

        cw = Path.cwd()
        dirs.extend(
            (
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
                ref = ir.files("ignite64py").joinpath("assets", fn)
                if ref.is_file():
                    im = Image_mod.open(io.BytesIO(ref.read_bytes())).convert("RGBA")
                    return im, f"ignite64py/assets/{fn} (importlib)"
        except Exception:
            pass

        for fn in _DIE_PHOTO_NAMES:
            blob = pkgutil.get_data("ignite64py", f"assets/{fn}")
            if blob:
                try:
                    im = Image_mod.open(io.BytesIO(blob)).convert("RGBA")
                    return im, f"ignite64py/assets/{fn} (pkgutil)"
                except Exception:
                    continue

        return (
            None,
            "no die image — add ignite64py/assets/ignite64.jpg (or ignite64_die_photo.png) or set IGNITE_DIE_PHOTO",
        )

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
        self._macro_file_cb["values"] = ["— scegli —"] + labels
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
            self._set_status("Scegli un file .py nella lista Macro")
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
        """
        if self.offline:
            self._set_status("Calib DCO: richiede hardware (offline)")
            return

        win = tk.Toplevel(self)
        win.title("DCO Calibration Settings")
        win.transient(self)
        frm = ttk.Frame(win, padding=12)
        frm.pack(fill="both", expand=True)

        quad_labels = (
            "SW (South-West)",
            "NW (North-West)",
            "SE (South-East)",
            "NE (North-East)",
            "ALL Quadrants",
            "BROADCAST",
        )
        ttk.Label(frm, text="Quadrante").grid(row=0, column=0, sticky="w", pady=2)
        quad_cb = ttk.Combobox(frm, state="readonly", width=26, values=quad_labels)
        qm = {"SW": 0, "NW": 1, "SE": 2, "NE": 3}
        try:
            quad_cb.current(qm.get(str(self.quad_var.get()).strip().upper(), 0))
        except Exception:
            quad_cb.current(0)
        quad_cb.grid(row=0, column=1, sticky="ew", pady=2)

        mat_vals = [f"MAT {i:02d}" for i in range(16)] + ["MAT ALL"]
        ttk.Label(frm, text="MAT").grid(row=1, column=0, sticky="w", pady=2)
        mat_cb = ttk.Combobox(frm, state="readonly", width=14, values=mat_vals)
        mat_cb.current(0)
        mat_cb.grid(row=1, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="PIX min").grid(row=2, column=0, sticky="w", pady=2)
        pix_min_sb = tk.Spinbox(frm, from_=0, to=63, width=8)
        pix_min_sb.delete(0, "end")
        pix_min_sb.insert(0, "0")
        pix_min_sb.grid(row=2, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="PIX max").grid(row=3, column=0, sticky="w", pady=2)
        pix_max_sb = tk.Spinbox(frm, from_=0, to=63, width=8)
        pix_max_sb.delete(0, "end")
        pix_max_sb.insert(0, "63")
        pix_max_sb.grid(row=3, column=1, sticky="w", pady=2)

        all_pix_var = tk.BooleanVar(value=False)

        def _toggle_pix_span(*_a: object) -> None:
            if all_pix_var.get():
                pix_min_sb.configure(state="disabled")
                pix_max_sb.configure(state="disabled")
            else:
                pix_min_sb.configure(state="normal")
                pix_max_sb.configure(state="normal")

        ttk.Checkbutton(frm, text="All PIX (0–63)", variable=all_pix_var, command=_toggle_pix_span).grid(
            row=4, column=1, sticky="w", pady=2
        )

        ttk.Label(frm, text="Resolution target (ps)").grid(row=5, column=0, sticky="w", pady=2)
        res_sb = tk.Spinbox(frm, from_=10, to=100, width=8)
        res_sb.delete(0, "end")
        res_sb.insert(0, "30")
        res_sb.grid(row=5, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="Calibration time (reg 0..3)").grid(row=6, column=0, sticky="w", pady=2)
        cal_t_sb = tk.Spinbox(frm, from_=0, to=3, width=8)
        cal_t_sb.delete(0, "end")
        cal_t_sb.insert(0, "3")
        cal_t_sb.grid(row=6, column=1, sticky="w", pady=2)

        de_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(frm, text="Double edge (DE)", variable=de_var).grid(row=7, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="DCO-0 Adj (0..3)").grid(row=8, column=0, sticky="w", pady=2)
        adj_sb = tk.Spinbox(frm, from_=0, to=3, width=8)
        adj_sb.delete(0, "end")
        adj_sb.insert(0, "1")
        adj_sb.grid(row=8, column=1, sticky="w", pady=2)

        ttk.Label(frm, text="DCO-0 Ctrl (0..15)").grid(row=9, column=0, sticky="w", pady=2)
        ctrl_sb = tk.Spinbox(frm, from_=0, to=15, width=8)
        ctrl_sb.delete(0, "end")
        ctrl_sb.insert(0, "0")
        ctrl_sb.grid(row=9, column=1, sticky="w", pady=2)

        cc_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(frm, text="Calibrate MAT 4–7 (broadcast)", variable=cc_var).grid(
            row=10, column=1, sticky="w", pady=2
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
                err_var.set("Seleziona un quadrante diverso da BROADCAST.")
                return

            path = filedialog.asksaveasfilename(
                parent=win,
                title="Salva report calibrazione DCO",
                defaultextension=".txt",
                filetypes=[("Text", "*.txt"), ("All", "*.*")],
            )
            if not path:
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
            self._set_status("Calib DCO in esecuzione…")

            def work() -> None:
                def prog(msg: str) -> None:
                    self.after(0, lambda m=msg: self._set_status(m))

                try:
                    self.hw.run_calib_dco(params, progress=prog)
                except Exception as e:
                    self.after(0, lambda e=e: self._set_status(f"Calib DCO errore: {e}"))
                    return
                self.after(0, lambda: self._set_status(f"Calib DCO completato → {path}"))

            threading.Thread(target=work, daemon=True).start()

        def do_cancel() -> None:
            win.destroy()

        bf = ttk.Frame(frm)
        bf.grid(row=12, column=0, columnspan=2, pady=(12, 0))
        ttk.Button(bf, text="Start", command=do_ok).pack(side="left", padx=(0, 8))
        ttk.Button(bf, text="Cancel", command=do_cancel).pack(side="left")
        ttk.Label(frm, textvariable=err_var, foreground="#b00020").grid(row=11, column=0, columnspan=2, sticky="w")

        frm.grid_columnconfigure(1, weight=1)

    def _with_hw(self, fn, *, busy: str) -> Optional[object]:
        if self.offline:
            self._set_status(f"{busy} (offline)")
            return None
        try:
            self._set_status(busy)
            return fn()
        except Exception as e:
            self._set_status(f"Error: {e}")
            return None
        finally:
            self.update_idletasks()

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
        # No direct ADC mapping currently exposed for these in our API:
        # VINJ_L, VLDO, VFB
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

    def _gather_quad_stats(self, quad: str) -> dict[str, int]:
        """Per-quadrant counts (full MAT×pixel scan — runs in a worker thread)."""
        analog_on = 0
        for mat in range(16):
            for ch in range(64):
                if self.hw.readAnalogChannelON(quad, mattonella=mat, canale=ch):
                    analog_on += 1
        tdc_mats = 0
        for mat in range(16):
            if self.hw.readEnableTDC(quad, Mattonella=mat)["tdc_on"]:
                tdc_mats += 1
        return {"analog_on": analog_on, "digpix_on": analog_on, "tdc_mats": tdc_mats}

    def _format_quad_monitor_block(
        self,
        quad: str,
        st: dict[str, int],
        *,
        power_ok: Optional[bool],
        power_err: Optional[str],
    ) -> str:
        """Testo completo per un quadrante: analog %, PIX %, TDC %, power (globale + placeholder mW)."""
        n_pix = 1024
        n_mat = 16
        a = int(st.get("analog_on", 0))
        d = int(st.get("digpix_on", 0))
        t = int(st.get("tdc_mats", 0))
        pa = round(100.0 * a / n_pix, 1)
        pd = round(100.0 * d / n_pix, 1)
        pt = round(100.0 * t / n_mat, 1)
        if power_err:
            ap = f"err: {power_err[:44]}"
        elif power_ok is None:
            ap = "n/d"
        else:
            ap = "ON" if power_ok else "OFF"
        lines = [
            f"{quad}",
            f"Analog Channel ON: {a} / {n_pix} ({pa}%)",
            f"Dig PIX ON: {d} / {n_pix} ({pd}%)",
            f"TDCon: {t} / {n_mat} MAT ({pt}%)",
            f"Analog Power (globale IOext): {ap}",
            "Power consumption: — mW (per-quadrante: n/d in API)",
            "Analog ch. calibrated: — (n/d)",
        ]
        return "\n".join(lines)

    def _placeholder_quad_monitor_block(self, quad: str, *, offline: bool) -> str:
        tag = "OFFLINE" if offline else "…"
        lines = [
            quad,
            f"Analog Channel ON: {tag}",
            f"Dig PIX ON: {tag}",
            f"TDCon: {tag}",
            f"Analog Power (globale IOext): {tag}",
            "Power consumption: — mW (per-quadrante: n/d in API)",
            "Analog ch. calibrated: — (n/d)",
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

    def _apply_monitor_panels_error(self, msg: str) -> None:
        short = str(msg).replace("\n", " ")[:120]
        for q in ("NW", "NE", "SW", "SE"):
            self._quad_monitor_canvas[q] = f"(monitor)\n{short}"
        self._trigger_die_canvas_refresh()

    def _run_die_monitor_scan_async(self, seq: int) -> None:
        def work() -> None:
            try:
                stats: dict[str, dict[str, int]] = {}
                for q in ("NW", "NE", "SW", "SE"):
                    stats[q] = self._gather_quad_stats(q)
                power_ok: Optional[bool] = None
                power_err: Optional[str] = None
                try:
                    power_ok = bool(self.hw.readAnalogPower())
                except Exception as e:
                    power_err = str(e)

                def apply() -> None:
                    if seq != self._quadrants_mon_seq:
                        return
                    self._apply_monitor_panels_from_stats(stats, power_ok=power_ok, power_err=power_err)

                self.after(0, apply)
            except Exception as e:

                def fail() -> None:
                    if seq != self._quadrants_mon_seq:
                        return
                    self._apply_monitor_panels_error(str(e))

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

        def tick() -> None:
            if not frm.winfo_exists() or seq != self._quadrants_mon_seq:
                return
            self._run_die_monitor_scan_async(seq)
            self._quadrants_mon_job = self.after(8000, tick)

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
        """Lettura TOP (mux = quadrante menu in alto) per il monitor sulla pagina Quadrants."""

        def work() -> None:
            if self.offline:

                def apply_off() -> None:
                    if seq != self._quadrants_top_mon_seq:
                        return
                    self._quadrants_top_mon_var.set("TOP (lettura): OFFLINE — connettere hardware.")

                self.after(0, apply_off)
                return
            try:
                qq = self.quad_var.get().strip().upper()
                if qq not in ("NW", "NE", "SW", "SE"):
                    qq = "SW"
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
                line = f"TOP (lettura): {str(e)[:180]}"

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
            self._quadrants_top_mon_var.set("TOP (lettura): OFFLINE — connettere hardware.")
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
        if getattr(self, "_top_tp_rep_var", None) is None:
            self._top_tp_rep_var = tk.StringVar(value="1")
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
            "Segnale TP (TOP reg 11 — StartTP / repet / EOS):",
            f"  Start TP: {st_on}",
            f"  Repetition (LSB): {tp.get('repetition', '—')}",
            f"  EOS (bit7): {tp.get('eos', '—')}",
            "",
            "Raw TOP byte (AFE pulse — tp_width / start_tp verso firmware / README):",
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
                self._top_snapshot_set_text(txt, f"Quadrant {q}\n\nOFFLINE — nessuna lettura HW.")
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
                height=14,
                width=38,
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
        ttk.Label(head, text="TOP — tutti i quadranti", font=("Segoe UI", 16, "bold")).pack(anchor="w")
        ttk.Label(
            head,
            text="In alto: impostazioni e scrittura registri TOP. Sotto: lettura dettagliata per NW/NE/SW/SE.",
            font=("Segoe UI", 10),
            foreground="#424242",
        ).pack(anchor="w", pady=(4, 0))

        # Striscia scrittura PRIMA della griglia: altrimenti expand sulla griglia spinge i comandi sotto il bordo finestra.
        self._build_top_controls_strip(frm).pack(fill="x", pady=(10, 0))

        ttk.Label(frm, text="Lettura registri TOP (per quadrante)", font=("Segoe UI", 11, "bold")).pack(
            anchor="w", pady=(14, 4)
        )
        bar = ttk.Frame(frm)
        bar.pack(fill="x", pady=(0, 6))
        ttk.Button(bar, text="Aggiorna lettura", command=self._schedule_top_snapshot_refresh).pack(side="left")

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
            text="TOP — scrittura (mux); in Quadrants il riepilogo lettura segue il quadrante nel menu",
            padding=8,
        )
        g = ttk.Frame(lf)
        g.pack(fill="x")

        r = 0
        ttk.Label(g, text="Quadrante (scrittura / readback)").grid(row=r, column=0, sticky="w", padx=(0, 8), pady=2)
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
                self._set_status(f"TOP Driver STR applicato → {self._sel_top_apply_quad()}")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Applica", command=apply_drv).grid(row=r, column=2, padx=8, pady=2)
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
                self._set_status(f"Readout → {self._top_ro_var.get()} ({self._sel_top_apply_quad()})")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Applica readout", command=apply_ro).grid(row=r, column=3, padx=8, pady=2)
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
                self._set_status(f"SLVS → {self._top_slvs_var.get()} ({self._sel_top_apply_quad()})")
                refresh_top_ro()
            except Exception as e:
                self._set_status(str(e))

        ttk.Button(g, text="Applica SLVS", command=apply_slvs).grid(row=r, column=3, padx=8, pady=2)
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

        def refresh_top_ro() -> None:
            if self.offline:
                self._top_status_var.set("OFFLINE — controlli TOP disabilitati")
                return
            try:
                self._before_top_write()
                qq = self._sel_top_apply_quad()
                d = int(self.hw.readTopDriverSTR())
                self._top_drv_var.set(str(d))
                self._top_ro_var.set(self._norm_top_readout(self.hw.readTopReadout()))
                self._top_slvs_var.set(self._norm_top_slvs(self.hw.readTopSLVS()))
                tp = self.hw.readStartTP()
                self._top_tp_rep_var.set(str(int(tp["repetition"])))
                self._top_status_var.set(
                    f"TOP [{qq}]: readout={self._top_ro_var.get()}  SLVS={self._top_slvs_var.get()}  "
                    f"StartTP={tp['start']} repet={tp['repetition']}  "
                    f"(registri completi nei pannelli NW/NE/SW/SE sopra)"
                )
            except Exception as e:
                self._top_status_var.set(str(e)[:200])

        ttk.Button(g, text="Leggi TOP", command=refresh_top_ro).grid(row=r, column=3, padx=8, pady=2)
        r += 1

        ttk.Label(g, textvariable=self._top_status_var, wraplength=920, font=("Segoe UI", 10)).grid(
            row=r, column=0, columnspan=5, sticky="w", pady=(6, 0)
        )

        if not self.offline:
            self.after(400, refresh_top_ro)
        else:
            self._top_status_var.set("OFFLINE")

        return lf

    # -----
    # Views
    # -----
    def _build_quadrants_view(self, parent: ttk.Frame) -> ttk.Frame:
        frm = ttk.Frame(parent)
        ttk.Label(frm, text="Quadrants", font=("Segoe UI", 16, "bold")).pack(anchor="w", pady=(0, 10))

        top_mon = ttk.Labelframe(
            frm,
            text="TOP — valori attuali (lettura sul quadrante selezionato nel menu «Quadrant» sopra)",
            padding=8,
        )
        top_mon.pack(fill="x", pady=(0, 10))
        ttk.Label(
            top_mon,
            textvariable=self._quadrants_top_mon_var,
            font=("Segoe UI", 10),
            wraplength=1050,
            justify="left",
        ).pack(anchor="w")
        ttk.Label(
            top_mon,
            text="Per modificare driver / readout / SLVS / TP apri la pagina TOP cliccando la fascia TOP (blu) sul die.",
            font=("Segoe UI", 9),
            foreground="#555555",
        ).pack(anchor="w", pady=(6, 0))

        win_bg = getattr(self, "_window_bg", "#f0f0f0")
        Image, ImageTk = _maybe_load_pil()
        die_im, die_info = self._load_die_photo_pil(Image)

        use_die = die_im is not None and Image is not None and ImageTk is not None
        if die_im is not None and Image is not None and ImageTk is None:
            del die_info  # foto presente ma senza ImageTk — uso griglia fallback

        if use_die:
            chip_box = _parse_die_chip_box()
            bl, bt, br, bb = chip_box
            chip_outer = tk.Frame(frm, bg=win_bg)
            chip_outer.pack(fill="both", expand=True)

            cv = tk.Canvas(chip_outer, highlightthickness=0, bd=0, bg=win_bg)
            cv.pack(fill="both", expand=True)

            iw, ih = die_im.size

            def redraw_die(_ev=None) -> None:
                cv.delete("all")
                cw = int(cv.winfo_width())
                ch = int(cv.winfo_height())
                if cw < 8 or ch < 8:
                    return
                nw, nh, x_off, y_off = _die_contain_geom(iw, ih, cw, ch)
                rs = _pil_resample_lanczos(Image)
                im2 = die_im.resize((nw, nh), rs)
                ph = ImageTk.PhotoImage(im2.convert("RGBA"))
                cv._die_photo_ref = ph  # type: ignore[attr-defined]
                cv.create_image(x_off, y_off, image=ph, anchor="nw", tags=("die_bg",))

                bx0 = bl * nw + x_off
                by0 = bt * nh + y_off
                bx1 = br * nw + x_off
                by1 = bb * nh + y_off
                mu = (bl + br) * 0.5 * nw + x_off
                mv = (bt + bb) * 0.5 * nh + y_off

                cv.create_rectangle(bx0, by0, bx1, by1, outline="#b71c1c", width=3, tags=("die_overlay",))
                cv.create_line(mu, by0, mu, by1, fill="#4e342e", width=2, tags=("die_overlay",))
                cv.create_line(bx0, mv, bx1, mv, fill="#4e342e", width=2, tags=("die_overlay",))

                hh_px = max(1.0, (bb - bt) * nh)
                fz = max(14, min(96, int(hh_px * 0.12)))

                mu_n = (bl + br) * 0.5
                mv_n = (bt + bb) * 0.5
                centers = {
                    "NW": ((bl + mu_n) * 0.5, (bt + mv_n) * 0.5),
                    "NE": ((mu_n + br) * 0.5, (bt + mv_n) * 0.5),
                    "SW": ((bl + mu_n) * 0.5, (mv_n + bb) * 0.5),
                    "SE": ((mu_n + br) * 0.5, (mv_n + bb) * 0.5),
                }
                for qn, (un, vn) in centers.items():
                    cx = un * nw + x_off
                    cy = vn * nh + y_off
                    _draw_quad_label(cv, cx, cy, qn, fz)

                # Guida visiva zona TOP (rettangolo blu tratteggiato sulla foto — regola con IGNITE_DIE_TOP_BAND).
                tl, tt, tr, tb = _parse_die_top_band()
                tx0 = tl * nw + x_off
                ty0 = tt * nh + y_off
                tx1 = tr * nw + x_off
                ty1 = tb * nh + y_off
                cv.create_rectangle(tx0, ty0, tx1, ty1, outline="#1565c0", width=2, dash=(6, 4), tags=("die_top_band",))
                cv.create_text(
                    (tx0 + tx1) / 2,
                    (ty0 + ty1) / 2,
                    text="TOP",
                    fill="#1565c0",
                    font=("Segoe UI", fz, "bold"),
                    justify="center",
                    tags=("die_top_band",),
                )

                # Monitoring: un blocco per quadrante solo nelle bande grigie (fuori dalla foto / bitmap).
                _draw_quadrant_monitor_panels_outside(
                    cv,
                    canvas_w=cw,
                    canvas_h=ch,
                    inner_left=float(x_off),
                    inner_top=float(y_off),
                    inner_right=float(x_off + nw),
                    inner_bottom=float(y_off + nh),
                    qtxt=self._quad_monitor_canvas,
                )

            def on_die_click(ev: tk.Event) -> None:
                cw = int(cv.winfo_width())
                ch = int(cv.winfo_height())
                if cw < 4 or ch < 4:
                    return
                nw, nh, x_off, y_off = _die_contain_geom(iw, ih, cw, ch)
                u, v = _canvas_to_norm_uv_contain(float(ev.x), float(ev.y), nw, nh, x_off, y_off)
                if not (-1e-6 <= u <= 1.0 + 1e-6 and -1e-6 <= v <= 1.0 + 1e-6):
                    return
                u = max(0.0, min(1.0, u))
                v = max(0.0, min(1.0, v))
                tl, tt, tr, tb = _parse_die_top_band()
                if tl <= u <= tr and tt <= v <= tb:
                    self._open_top_view()
                    return
                qq = _quad_from_uv_in_chip_box(u, v, chip_box)
                if qq:
                    self._open_quadrant(qq)

            cv.bind("<Configure>", redraw_die)
            cv.bind("<Button-1>", on_die_click)
            cv.configure(cursor="hand2")

            self._die_canvas_redraw = redraw_die
            if self.offline:
                self._apply_monitor_panels_offline()
            else:
                self._apply_monitor_panels_placeholder()

            self.after_idle(redraw_die)

            self._schedule_quadrant_monitor_refresh(frm)
            self._schedule_quadrants_top_monitor(frm)

            hint = (
                "Clic sul die (quadrante) o sulla fascia TOP (blu) per aprire la pagina TOP (letture + comandi). "
                "Env: IGNITE_DIE_PHOTO, IGNITE_DIE_CHIP_BOX, IGNITE_DIE_TOP_BAND."
            )
            ttk.Label(frm, text=hint, font=("Segoe UI", 9), foreground="#444444").pack(anchor="w", pady=(8, 0))
            return frm

        # Fallback: griglia 2×2 + stessi testi monitor per quadrante; TOP si apre dalla fascia blu.
        chip_outer = tk.Frame(frm, bg=win_bg)
        chip_outer.pack(fill="both", expand=True)

        cv_fb = tk.Canvas(chip_outer, highlightthickness=0, bd=0, bg=win_bg)
        cv_fb.pack(fill="both", expand=True)

        def redraw_plain(_ev=None) -> None:
            cv_fb.delete("all")
            cw = int(cv_fb.winfo_width())
            ch = int(cv_fb.winfo_height())
            if cw < 24 or ch < 24:
                return
            pad = max(20, min(cw, ch) // 12)
            side = min(cw - 2 * pad, ch - 2 * pad)
            side = max(120, side)
            x0 = (cw - side) // 2
            y0 = (ch - side) // 2
            x1 = x0 + side
            y1 = y0 + side
            mx = (x0 + x1) / 2
            my = (y0 + y1) / 2
            fills = ("#ececef", "#e2e2e8", "#e2e2e8", "#ececef")
            cv_fb.create_rectangle(x0, y0, mx, my, outline="#757575", width=2, fill=fills[0])
            cv_fb.create_rectangle(mx, y0, x1, my, outline="#757575", width=2, fill=fills[1])
            cv_fb.create_rectangle(x0, my, mx, y1, outline="#757575", width=2, fill=fills[2])
            cv_fb.create_rectangle(mx, my, x1, y1, outline="#757575", width=2, fill=fills[3])
            cv_fb.create_line(x0, my, x1, my, fill="#555566", width=2)
            cv_fb.create_line(mx, y0, mx, y1, fill="#555566", width=2)
            fz = max(14, min(72, side // 5))
            _draw_quad_label(cv_fb, (x0 + mx) / 2, (y0 + my) / 2, "NW", fz)
            _draw_quad_label(cv_fb, (mx + x1) / 2, (y0 + my) / 2, "NE", fz)
            _draw_quad_label(cv_fb, (x0 + mx) / 2, (my + y1) / 2, "SW", fz)
            _draw_quad_label(cv_fb, (mx + x1) / 2, (my + y1) / 2, "SE", fz)
            _draw_quadrant_monitor_panels_outside(
                cv_fb,
                canvas_w=cw,
                canvas_h=ch,
                inner_left=float(x0),
                inner_top=float(y0),
                inner_right=float(x1),
                inner_bottom=float(y1),
                qtxt=self._quad_monitor_canvas,
                tag="quad_mon_plain",
            )
            tl, tt, tr, tb = _parse_die_top_band()
            pxa = x0 + tl * (x1 - x0)
            pya = y0 + tt * (y1 - y0)
            pxb = x0 + tr * (x1 - x0)
            pyb = y0 + tb * (y1 - y0)
            cv_fb.create_rectangle(pxa, pya, pxb, pyb, outline="#1565c0", width=2, dash=(6, 4), tags=("plain_top_band",))
            cv_fb.create_text(
                (pxa + pxb) / 2,
                (pya + pyb) / 2,
                text="TOP",
                fill="#1565c0",
                font=("Segoe UI", fz, "bold"),
                justify="center",
                tags=("plain_top_band",),
            )
            cv_fb._plain_geom = (x0, y0, x1, y1, mx, my)  # type: ignore[attr-defined]

        def on_plain_click(ev: tk.Event) -> None:
            g = getattr(cv_fb, "_plain_geom", None)
            if not g:
                return
            x0, y0, x1, y1, mx, my = g
            if not (x0 <= ev.x <= x1 and y0 <= ev.y <= y1):
                return
            u = (ev.x - x0) / max(1e-6, (x1 - x0))
            v = (ev.y - y0) / max(1e-6, (y1 - y0))
            tl, tt, tr, tb = _parse_die_top_band()
            if tl <= u <= tr and tt <= v <= tb:
                self._open_top_view()
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
        if self.offline:
            self._apply_monitor_panels_offline()
        else:
            self._apply_monitor_panels_placeholder()

        self.after_idle(redraw_plain)

        self._schedule_quadrant_monitor_refresh(frm)
        self._schedule_quadrants_top_monitor(frm)

        hint_fb = "Senza foto: griglia semplice. Clic fascia TOP (blu) per la pagina TOP, o un quadrante. Env: IGNITE_DIE_PHOTO."
        ttk.Label(frm, text=hint_fb, font=("Segoe UI", 9), foreground="#444444").pack(anchor="w", pady=(8, 0))
        return frm

    def _open_quadrant(self, q: str) -> None:
        self.quad_var.set(str(q).strip().upper())
        self._push_view(self._build_blocks_view(self.content))

    def _build_blocks_view(self, parent: ttk.Frame) -> ttk.Frame:
        frm = ttk.Frame(parent)
        quad = self.quad_var.get()
        ttk.Label(frm, text=f"Quadrant {quad} → Blocks", font=("Segoe UI", 16, "bold")).pack(anchor="w", pady=(0, 4))
        ttk.Label(
            frm,
            text="Navigazione al block di interesse · MAT 8×8 = canali ON (verde) / OFF (grigio) · sotto: stato FTDAC · FIFO →",
            font=("Segoe UI", 9),
            foreground="#424242",
        ).pack(anchor="w", pady=(0, 10))

        top = ttk.Frame(frm)
        top.pack(fill="both", expand=True)

        grid = ttk.Frame(top)
        grid.pack(side="left", fill="both", expand=True)

        def make_block_widget(parent_: ttk.Frame, *, block_id: int) -> ttk.Frame:
            mats = self.mapping.mats_in_block(block_id)
            owner = self.mapping.analog_owner_mat(block_id)
            kind = self.mapping.block_kind(block_id)

            outer = ttk.Frame(parent_, padding=6)
            outer.configure(cursor="hand2")

            title = ttk.Label(outer, text=f"Block {block_id} ({kind})", font=("Segoe UI", 10, "bold"))
            title.pack(anchor="w")

            # Stylized diagram: responsive and always drawn inside a centered square.
            c = tk.Canvas(
                outer,
                highlightthickness=1,
                highlightbackground="#bdbdbd",
                bg="white",
            )
            c.pack(pady=(6, 0), fill="both", expand=True)

            mat_tl, mat_tr, mat_bl, mat_br = mats
            kind_u = str(kind).upper()
            an_fill = "#ffe8e8" if "NOLDO" in kind_u else "#e8fff0"

            mat_cache: dict[int, Optional[dict[str, object]]] = {mid: None for mid in mats}
            _fetch_gen: list[int] = [0]
            _fetch_after: list[Optional[str]] = [None]

            def redraw_display(_ev=None) -> None:
                c.delete("all")
                w = int(c.winfo_width())
                h = int(c.winfo_height())
                if w <= 2 or h <= 2:
                    return

                side = min(w, h)
                ox = int((w - side) / 2)
                oy = int((h - side) / 2)

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

                font_an = ("Segoe UI", max(8, int(side * 0.035)), "bold")
                font_owner = ("Segoe UI", max(8, int(side * 0.03)))
                font_mat_lbl = ("Segoe UI", max(7, int(side * 0.028)), "bold")
                font_ft = ("Segoe UI", max(6, int(side * 0.021)))

                def draw_mat(rect: tuple[float, float, float, float], mat_id: int) -> None:
                    rx0_, ry0, rx1_, ry1 = rect
                    rw = rx1_ - rx0_
                    rh = ry1 - ry0
                    ft_h = max(11.0, min(rh * 0.22, 28.0))
                    gy1 = ry1 - ft_h
                    outline_col = "#2f2f2f"
                    entry = mat_cache.get(mat_id)
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
                    ftdac: Optional[list[int]] = None
                    if isinstance(entry, dict) and not entry.get("_err"):
                        po = entry.get("pix_on")
                        ft = entry.get("ftdac")
                        if isinstance(po, list) and isinstance(ft, list) and len(po) >= 64 and len(ft) >= 64:
                            pix_on = [bool(x) for x in po[:64]]
                            ftdac = [int(x) for x in ft[:64]]

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
                                fill = "#2e7d32" if pix_on[ch] else "#eceff1"
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
                        ft_msg = "lettura MAT errore"
                        ft_fill = "#b71c1c"
                    elif self.offline:
                        ft_msg = "OFFLINE"
                        ft_fill = "#616161"
                    elif ftdac is not None:
                        nd = sum(1 for x in ftdac if int(x) != 15)
                        if nd > 0:
                            ft_msg = "CalibrazioneFTDAC DONE!"
                            ft_fill = "#1565c0"
                        else:
                            ft_msg = "CalibrazioneFTDAC: solo FT=15"
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
                    quad_q = self.quad_var.get()

                    def work() -> None:
                        try:
                            out: dict[int, dict[str, object]] = {}
                            for mid in mats:
                                out[mid] = self.hw.readMatPixelsAndFTDAC(quad_q, mattonella=mid)

                            def apply_ok() -> None:
                                if gen != _fetch_gen[0]:
                                    return
                                for mid in mats:
                                    mat_cache[mid] = out[mid]
                                redraw_display()

                            self.after(0, apply_ok)
                        except Exception as e:

                            def apply_err() -> None:
                                if gen != _fetch_gen[0]:
                                    return
                                for mid in mats:
                                    mat_cache[mid] = {"_err": str(e)}
                                redraw_display()

                            self.after(0, apply_err)

                    threading.Thread(target=work, daemon=True).start()

                _fetch_after[0] = self.after(260, fire)

            def on_configure(_ev=None) -> None:
                redraw_display()
                schedule_fetch()

            c.bind("<Configure>", on_configure)
            redraw_display()
            schedule_fetch()

            # click anywhere to open
            def go(_ev=None) -> None:
                self._open_block(block_id)

            outer.bind("<Button-1>", go)
            title.bind("<Button-1>", go)
            c.bind("<Button-1>", go)
            outer.bind("<Return>", go)

            return outer

        for block_id in range(4):
            w = make_block_widget(grid, block_id=block_id)
            r, c = divmod(block_id, 2)
            w.grid(row=r, column=c, padx=12, pady=12, sticky="nsew")
            grid.grid_columnconfigure(c, weight=1)
            grid.grid_rowconfigure(r, weight=1)

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
            out.configure(state="normal")
            out.delete("1.0", "end")
            out.configure(state="disabled")
            fifo_summary_var.set("Log FIFO cancellato.")

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
            if not decoded:
                self._fifo_log(out, f"0x{w:016X}")
                fifo_summary_var.set(f"Ultimo read Q={quad2}: word raw 0x{w:016X}")
                return
            d = self._fifo_decode_word(w)
            self._fifo_log(
                out,
                f"0x{w:016X}  MAT={d['mat']:2d}  CH={d['channel']:2d}  "
                f"cnt={d['fifo_cnt']:3d} empty={d['fifo_empty']} full={d['fifo_full']} half={d['fifo_halffull']}",
            )
            fifo_summary_var.set(
                f"Ultimo Q={quad2}: MAT={d['mat']:02d} CH={d['channel']:02d}  "
                f"cnt={d['fifo_cnt']} empty={d['fifo_empty']} hf={d['fifo_halffull']}"
            )

        def drain(decoded: bool) -> None:
            quad2 = self.quad_var.get()

            def do() -> list[int]:
                self._fifo_ensure_i2c()
                self.hw.select_quadrant(quad2)
                return list(self.hw.FifoDrain(max_words=4096))

            ws = self._with_hw(do, busy=f"FIFO drain (Q={quad2})")
            if ws is None:
                return
            words = [int(x) for x in ws]  # type: ignore[arg-type]
            if not words:
                self._fifo_log(out, "DRAIN: empty")
                fifo_summary_var.set(f"Drain Q={quad2}: FIFO vuota")
                return
            self._fifo_log(out, f"DRAIN: {len(words)} words")
            fifo_summary_var.set(f"Drain Q={quad2}: {len(words)} parole lette")
            for w in words:
                if w == 0:
                    continue
                if not decoded:
                    self._fifo_log(out, f"0x{w:016X}")
                else:
                    d = self._fifo_decode_word(w)
                    self._fifo_log(
                        out,
                        f"0x{w:016X}  MAT={d['mat']:2d}  CH={d['channel']:2d}  "
                        f"cnt={d['fifo_cnt']:3d} empty={d['fifo_empty']} full={d['fifo_full']} half={d['fifo_halffull']}",
                    )

        ttk.Button(btns, text="Read 1 (raw)", command=lambda: read_one(False)).pack(side="left", padx=3)
        ttk.Button(btns, text="Read 1 (decoded)", command=lambda: read_one(True)).pack(side="left", padx=3)
        ttk.Button(btns, text="Drain (decoded)", command=lambda: drain(True)).pack(side="left", padx=3)
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
        ttk.Button(toolbar, text="Open analog details", command=lambda: self._open_analog(block_id)).pack(side="left", padx=6)

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

        self._refresh_block(block_id)
        return frm

    def _build_mat_mini(self, parent: ttk.Frame, mat_id: int, *, title: str) -> ttk.Frame:
        frm = ttk.Labelframe(parent, text=title, padding=4)
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
                self._toggle_pixel_block(int(mid), row * 8 + col)

        cv.bind("<Configure>", redraw_pixels)
        cv.bind("<Button-1>", on_click)
        redraw_pixels()

        return frm

    def _toggle_pixel_block(self, mat_id: int, pix_id: int) -> None:
        quad = self.quad_var.get()

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
        canvas = self._block_pix_canvas.get(mat_id)
        rects = self._block_pix_cells.get(mat_id)
        if canvas is not None and rects and pix_id < len(rects):
            fill = "#1f9d55" if new_state else "#aa2222"
            try:
                canvas.itemconfigure(rects[pix_id], fill=fill)
            except tk.TclError:
                pass
        self._set_status(f"Pixel {pix_id}={'ON' if new_state else 'OFF'} (Q={quad} MAT={mat_id})")

    def _refresh_mat_mini(self, mat_id: int) -> None:
        quad = self.quad_var.get()

        def do() -> dict[str, object]:
            return self.hw.readMatPixelsAndFTDAC(quad, mattonella=mat_id)

        r = self._with_hw(do, busy=f"Refreshing MAT {mat_id} (Q={quad})")
        if r is None or not isinstance(r, dict):
            return
        pix_on = r.get("pix_on")
        ftdac = r.get("ftdac")
        if not isinstance(pix_on, list) or not isinstance(ftdac, list) or len(pix_on) != 64 or len(ftdac) != 64:
            return
        canvas = self._block_pix_canvas.get(mat_id)
        rects = self._block_pix_cells.get(mat_id)
        if canvas is None or not rects or len(rects) != 64:
            return
        txts = self._block_pix_txt_ids.get(mat_id) or []
        for pix in range(64):
            on = bool(pix_on[pix])
            code = int(ftdac[pix])
            fill = "#1f9d55" if on else "#aa2222"
            try:
                canvas.itemconfigure(rects[pix], fill=fill)
            except tk.TclError:
                pass
            if pix < len(txts) and txts[pix]:
                try:
                    canvas.itemconfigure(txts[pix], text=str(code))
                except tk.TclError:
                    pass

    def _refresh_block(self, block_id: int) -> None:
        quad = self.quad_var.get()
        mats = self.mapping.mats_in_block(block_id)
        self._set_status(f"Refreshing block {block_id} (Q={quad})")
        for mat_id in mats:
            self._refresh_mat_mini(mat_id)
        # refresh analog mini (uses the same underlying refresh function)
        try:
            self._refresh_analog(block_id)
        except Exception:
            pass
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
            pady=4,
            font=("Segoe UI", 9, "bold"),
        )
        hdr.pack(fill="x", pady=(0, 6))

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
            ttk.Button(br, text="Applica", command=lambda: self._set_afe_bias(block_id)).pack(side="left", padx=6)

        # Reuse the same state variables used by the full analog view.
        self._dac_vars = {}
        self._dac_en_vars = {}
        self._dac_meas_vars = {}
        self._vinjh_var = tk.StringVar(value="?")
        self._vinjl_var = tk.StringVar(value="?")
        self._c2p_var = tk.BooleanVar(value=False)
        self._vdda_var = tk.StringVar(value="?")

        # DAC boxes
        dacs = ["VTHR_H", "VTHR_L", "VINJ_H", "VINJ_L", "VLDO", "VFB"]
        for name in dacs:
            box = ttk.Labelframe(frm, text=name, padding=3)
            box.pack(fill="x", pady=2)
            v = tk.StringVar(value="?")
            self._dac_vars[name] = v
            en = tk.BooleanVar(value=False)
            self._dac_en_vars[name] = en
            mv = tk.StringVar(value="")
            self._dac_meas_vars[name] = mv

            top = ttk.Frame(box)
            top.pack(fill="x")
            ttk.Label(top, text="code:", width=5).pack(side="left")
            ttk.Label(top, textvariable=v, width=6).pack(side="left")
            ttk.Checkbutton(top, text="EN", variable=en, command=lambda n=name: self._set_dac_enable(block_id, n)).pack(
                side="right"
            )

            bottom = ttk.Frame(box)
            bottom.pack(fill="x", pady=(2, 0))
            entry = ttk.Entry(bottom, width=4)
            entry.pack(side="left")
            ttk.Button(bottom, text="Set", command=lambda n=name, e=entry: self._set_dac_code(block_id, n, e.get())).pack(
                side="left", padx=(6, 0)
            )
            ttk.Label(box, textvariable=mv).pack(anchor="w", pady=(2, 0))

        # Mux + connect2pad
        mux = ttk.Labelframe(frm, text="VinjMux / C2P", padding=3)
        mux.pack(fill="x", pady=(6, 2))

        r1 = ttk.Frame(mux)
        r1.pack(fill="x", pady=2)
        ttk.Label(r1, text="VinjH:", width=6).pack(side="left")
        ttk.Label(r1, textvariable=self._vinjh_var, width=6).pack(side="left")
        ttk.Button(r1, text="dac", command=lambda: self._set_vinj(block_id, "VinjH", "dac")).pack(side="left", padx=3)
        ttk.Button(r1, text="VDDA", command=lambda: self._set_vinj(block_id, "VinjH", "VDDA")).pack(side="left", padx=3)

        r2 = ttk.Frame(mux)
        r2.pack(fill="x", pady=2)
        ttk.Label(r2, text="VinjL:", width=6).pack(side="left")
        ttk.Label(r2, textvariable=self._vinjl_var, width=6).pack(side="left")
        ttk.Button(r2, text="dac", command=lambda: self._set_vinj(block_id, "VinjL", "dac")).pack(side="left", padx=3)
        ttk.Button(r2, text="GNDA", command=lambda: self._set_vinj(block_id, "VinjL", "GNDA")).pack(side="left", padx=3)

        ttk.Checkbutton(
            mux,
            text="Connect2Pad (only this block)",
            variable=self._c2p_var,
            command=lambda: self._set_connect2pad(block_id, self._c2p_var.get()),
        ).pack(anchor="w", pady=(6, 0))

        vdda = ttk.Labelframe(frm, text="VDDA", padding=3)
        vdda.pack(fill="x", pady=(6, 0))
        ttk.Label(vdda, textvariable=self._vdda_var).pack(anchor="w")
        ttk.Button(vdda, text="Measure", command=lambda: self._measure_vdda(block_id)).pack(anchor="w", pady=(4, 0))

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
        ttk.Button(toolbar, text="Refresh", command=lambda: self._refresh_analog(block_id)).pack(side="left", padx=6)

        body = ttk.Frame(frm)
        body.pack(fill="both", expand=True)

        left = ttk.Labelframe(body, text="DACs", padding=10)
        left.pack(side="left", fill="y", padx=(0, 10))

        right = ttk.Labelframe(body, text="Mux / Connect2Pad", padding=10)
        right.pack(side="left", fill="both", expand=True)

        self._dac_vars: dict[str, tk.StringVar] = {}
        self._dac_en_vars: dict[str, tk.BooleanVar] = {}
        self._dac_meas_vars: dict[str, tk.StringVar] = {}

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
            ttk.Checkbutton(row, text="EN", variable=en, command=lambda n=name: self._set_dac_enable(block_id, n)).pack(
                side="left", padx=(6, 0)
            )
            entry = ttk.Entry(row, width=6)
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

        mux_row = ttk.Frame(right)
        mux_row.pack(fill="x", pady=6)
        ttk.Label(mux_row, text="VinjH mux:", width=10).pack(side="left")
        ttk.Label(mux_row, textvariable=self._vinjh_var, width=10).pack(side="left", padx=(6, 10))
        ttk.Button(mux_row, text="dac", command=lambda: self._set_vinj(block_id, "VinjH", "dac")).pack(side="left")
        ttk.Button(mux_row, text="VDDA", command=lambda: self._set_vinj(block_id, "VinjH", "VDDA")).pack(
            side="left", padx=(6, 0)
        )

        mux_row2 = ttk.Frame(right)
        mux_row2.pack(fill="x", pady=6)
        ttk.Label(mux_row2, text="VinjL mux:", width=10).pack(side="left")
        ttk.Label(mux_row2, textvariable=self._vinjl_var, width=10).pack(side="left", padx=(6, 10))
        ttk.Button(mux_row2, text="dac", command=lambda: self._set_vinj(block_id, "VinjL", "dac")).pack(side="left")
        ttk.Button(mux_row2, text="GNDA", command=lambda: self._set_vinj(block_id, "VinjL", "GNDA")).pack(
            side="left", padx=(6, 0)
        )

        c2p_row = ttk.Frame(right)
        c2p_row.pack(fill="x", pady=10)
        ttk.Checkbutton(
            c2p_row,
            text="Connect2Pad (only this block)",
            variable=self._c2p_var,
            command=lambda: self._set_connect2pad(block_id, self._c2p_var.get()),
        ).pack(side="left")

        ttk.Separator(right).pack(fill="x", pady=10)
        ttk.Button(right, text="Measure VDDA", command=lambda: self._measure_vdda(block_id)).pack(anchor="w")

        self._refresh_analog(block_id)
        return frm

    def _refresh_analog(self, block_id: int) -> None:
        quad = self.quad_var.get()
        owner = self.mapping.analog_owner_mat(block_id)

        def do() -> dict[str, object]:
            self.hw.select_quadrant(quad)
            out: dict[str, object] = {}
            for name in ["VTHR_H", "VTHR_L", "VINJ_H", "VINJ_L", "VLDO", "VFB"]:
                out[name] = self.hw.readAnalogColumnDAC(quad, block=owner, dac=name)
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
            if name in {"vinj", "c2p", "bias"}:
                continue
            d = val  # type: ignore[assignment]
            try:
                enable = bool(d["enable"])  # type: ignore[index]
                code = int(d["code"])  # type: ignore[index]
                self._dac_vars[name].set(f"{code}")
                self._dac_en_vars[name].set(enable)
            except Exception:
                self._dac_vars[name].set("?")
                self._dac_en_vars[name].set(False)
                try:
                    self._dac_meas_vars[name].set("")  # type: ignore[index]
                except Exception:
                    pass

        vinj = r.get("vinj", {})  # type: ignore[assignment]
        try:
            self._vinjh_var.set(str(vinj["VinjH"]))  # type: ignore[index]
            self._vinjl_var.set(str(vinj["VinjL"]))  # type: ignore[index]
        except Exception:
            self._vinjh_var.set("?")
            self._vinjl_var.set("?")
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
            if ch is not None:
                r = self.hw._adc_quad_oneshot(channel=ch, gain=0, res_bits=16, delay_s=0.01)  # type: ignore[attr-defined]
                meas_v = float(r["value"])
            return {"readback": rb, "meas_v": meas_v}

        r = self._with_hw(do, busy=f"Set {dac}={code} + verify (Q={quad} ownerMAT={owner})")
        if isinstance(r, dict):
            try:
                rb = r.get("readback", {})
                rb_code = int(rb.get("code"))  # type: ignore[arg-type]
                meas_v = r.get("meas_v", None)
                if meas_v is None:
                    self._set_status(f"{dac}: set={code} readback={rb_code} (no ADC mapping)")
                else:
                    self._set_status(f"{dac}: set={code} readback={rb_code} meas={float(meas_v):.6g} V")
                    try:
                        self._dac_meas_vars[dac.strip().upper()].set(f"{float(meas_v):.4g} V")  # type: ignore[index]
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
            # Use helper which ensures only one is enabled if available
            if enable:
                self.hw._set_connect2pad_only(quad, block=owner)  # type: ignore[attr-defined]
            else:
                self.hw.AnalogColumnConnect2PAD(quad, block=owner, valore=False)

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
        try:
            self._vdda_var.set(f"{float(v):.6g} V")  # type: ignore[attr-defined]
        except Exception:
            pass
        self._set_status(f"VDDA={float(v):.6g} (Q={quad} block={block_id})")


def run_gui(*, start_config: bool = True, default_quad: str = "SW") -> None:
    offline = _env_truthy("OFFLINE", "0")
    hw = Ignite64()
    if (not offline) and start_config:
        hw.start_config(default_quad)
    app = Ignite64Gui(hw, default_quad=default_quad, offline=offline)
    app.mainloop()

