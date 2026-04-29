"""
Routine macrocostruite per uso da GUI, script e test.

Ogni ``builtin_*`` è un’implementazione completa (validazione, mux, I2C): i file in
``examples/macros/*.py`` le richiamano con i parametri operativi definiti in cima a ciascun file.
Firma comune: ``(hw, quad, **kwargs)`` con keyword espliciti (MAT, Δ, lista canali, ecc.).
"""

from __future__ import annotations

from typing import Any, Callable, Iterable, Optional

# Metadata for the GUI combobox: id, short label, whether to run in a worker thread (FIFO cal can take minutes)
MACRO_REGISTRY: list[tuple[str, str, bool]] = [
    ("ftdac_delta_mat", "FTDAC: sposta tutti i canali (±Δ, chiede MAT)", False),
    ("ftdac_cal_mat", "Soglie FTDAC: calibra MAT intero via FIFO (lungo)", True),
    ("ftdac_cal_ch", "Soglie FTDAC: calibra un solo canale (FIFO)", True),
    ("ftdac_cal_channels", "FTDAC: calibra lista canali su un MAT (FIFO, lungo)", True),
    ("pixels_all_off_quad", "Spegni tutti i pixel (PIXON) nel quadrante", False),
    ("pixel_on", "Accendi un solo canale (MAT, channel)", False),
    ("pixel_off", "Spegni un canale (MAT, channel)", False),
    ("mat_all_pixels_on", "Accendi tutti i 64 canali di una mattonella", False),
    ("mat_all_pixels_off", "Spegni tutti i 64 canali di una mattonella", False),
    ("quad_all_pixels_on", "Accendi tutti i pixel del quadrante (16×64)", False),
    ("vthr_bump", "Soglie analogiche: VTHR_H / VTHR_L ±Δ (chiede MAT, DAC, Δ)", False),
    ("ftdac_dump", "Leggi / stampa codici FTDAC 0..63 per un MAT", False),
    ("prepare_fifo_readout", "TOP readout I2C + mux (prima di FIFO / calib FTDAC)", False),
    ("tdc_mat", "Abilita / disabilita TDC su una mattonella", False),
    ("isolate_one_channel", "Isola un canale sulla MAT (altri PIX off, TDC on)", False),
    ("mat_summary", "Riepilogo PIX accesi e range FTDAC su una MAT", False),
    ("fifo_drain", "Svuota FIFO e conta parole lette", False),
    ("read_analog_power_state", "Legge stato Analog Power (IOext)", False),
]


def _q(quad: str) -> str:
    return str(quad).strip().upper()


def builtin_ftdac_delta_mat(hw, quad: str, *, mat: int, delta: int = 1) -> dict[str, Any]:
    """
    Increment or decrement every FineTune DAC code for one MAT (clamped 0..15).
    """
    q = _q(quad)
    mat = int(mat)
    delta = int(delta)
    if mat < 0 or mat > 15:
        raise ValueError("mat atteso 0..15")
    hw.select_quadrant(q)
    d = hw.readMatPixelsAndFTDAC(q, mattonella=mat)
    ft = d.get("ftdac")
    if not isinstance(ft, list) or len(ft) < 64:
        raise RuntimeError("readMatPixelsAndFTDAC: ftdac non valido")
    before = [int(x) & 0xF for x in ft[:64]]
    after: list[int] = []
    for ch in range(64):
        v = max(0, min(15, before[ch] + delta))
        hw.AnalogChannelFineTune(q, block=0, mattonella=mat, canale=ch, valore=v)
        after.append(v)
    return {"quad": q, "mat": mat, "delta": delta, "before_min": min(before), "after_min": min(after)}


def builtin_ftdac_cal_mat(hw, quad: str, *, mat: int) -> dict[str, Any]:
    """Run FIFO-based FineTune calibration for all 64 channels on one MAT (slow)."""
    q = _q(quad)
    mat = int(mat)
    if mat < 0 or mat > 15:
        raise ValueError("mat atteso 0..15")
    return hw.CalibrateFTDAC(q, Mattonella=mat, Channel="ALL")


def builtin_ftdac_cal_ch(hw, quad: str, *, mat: int, channel: int) -> dict[str, Any]:
    """FIFO-based FTDAC calibration for a single pixel channel."""
    q = _q(quad)
    mat = int(mat)
    ch = int(channel)
    if ch < 0 or ch > 63:
        raise ValueError("channel atteso 0..63")
    return hw.CalibrateFTDAC(q, Mattonella=mat, Channel=ch)


def builtin_ftdac_cal_channels(
    hw, quad: str, *, mat: int, channels: Iterable[int]
) -> dict[str, Any]:
    """
    Calibrazione FIFO FTDAC su più canali della stessa mattonella (una chiamata ``CalibrateFTDAC``
    per indice). ``CalibrateFTDAC`` isola il canale e usa la FIFO; tra un canale e l’altro la
    sequenza è ripetuta automaticamente.
    """
    q = _q(quad)
    mat = int(mat)
    if mat < 0 or mat > 15:
        raise ValueError("mat atteso 0..15")
    chs = [int(c) for c in channels]
    if not chs:
        raise ValueError("channels: passare almeno un canale 0..63")
    for c in chs:
        if c < 0 or c > 63:
            raise ValueError(f"channel {c} fuori 0..63")
    results: dict[int, Any] = {}
    for c in chs:
        results[c] = hw.CalibrateFTDAC(q, Mattonella=mat, Channel=c)
    return {"quad": q, "mat": mat, "channels": chs, "results": results}


def builtin_pixels_all_off_quad(hw, quad: str) -> dict[str, Any]:
    """Spegne tutti i canali (bit PIXON) in tutte le mattonelle del quadrante."""
    q = _q(quad)
    hw.select_quadrant(q)
    for mat in range(16):
        for ch in range(64):
            hw.AnalogChannelOFF(q, mattonella=mat, canale=ch)
    return {"quad": q, "action": "all_pixels_off"}


def builtin_pixel_on(hw, quad: str, *, mat: int, channel: int) -> dict[str, Any]:
    """Accende un singolo canale (stesso meccanismo PIXON di ``AnalogChannelON``)."""
    q = _q(quad)
    mat = int(mat)
    ch = int(channel)
    if mat < 0 or mat > 15 or ch < 0 or ch > 63:
        raise ValueError("mat 0..15, channel 0..63")
    hw.AnalogChannelON(q, mattonella=mat, canale=ch)
    return {"quad": q, "mat": mat, "channel": ch, "on": True}


def builtin_pixel_off(hw, quad: str, *, mat: int, channel: int) -> dict[str, Any]:
    q = _q(quad)
    mat = int(mat)
    ch = int(channel)
    if mat < 0 or mat > 15 or ch < 0 or ch > 63:
        raise ValueError("mat 0..15, channel 0..63")
    hw.AnalogChannelOFF(q, mattonella=mat, canale=ch)
    return {"quad": q, "mat": mat, "channel": ch, "on": False}


def builtin_mat_all_pixels_on(hw, quad: str, *, mat: int) -> dict[str, Any]:
    """Accende tutti i 64 canali della mattonella."""
    q = _q(quad)
    mat = int(mat)
    if mat < 0 or mat > 15:
        raise ValueError("mat atteso 0..15")
    hw.select_quadrant(q)
    for ch in range(64):
        hw.AnalogChannelON(q, mattonella=mat, canale=ch)
    return {"quad": q, "mat": mat, "channels_on": 64}


def builtin_mat_all_pixels_off(hw, quad: str, *, mat: int) -> dict[str, Any]:
    """Spegne tutti i 64 canali della mattonella."""
    q = _q(quad)
    mat = int(mat)
    if mat < 0 or mat > 15:
        raise ValueError("mat atteso 0..15")
    hw.select_quadrant(q)
    for ch in range(64):
        hw.AnalogChannelOFF(q, mattonella=mat, canale=ch)
    return {"quad": q, "mat": mat, "channels_off": 64}


def builtin_quad_all_pixels_on(hw, quad: str) -> dict[str, Any]:
    """Accende tutti i pixel del quadrante (16 mat × 64 canali)."""
    q = _q(quad)
    hw.select_quadrant(q)
    for mat in range(16):
        for ch in range(64):
            hw.AnalogChannelON(q, mattonella=mat, canale=ch)
    return {"quad": q, "action": "all_pixels_on", "total": 16 * 64}


def builtin_vthr_bump(hw, quad: str, *, mat: int, dac: str, delta: int) -> dict[str, Any]:
    """
    Shift global discriminator threshold DAC code (VTHR_H or VTHR_L) for one MAT.
    Codes are 0..127; result is clamped; DAC stays enabled if it already was.
    """
    q = _q(quad)
    mat = int(mat)
    delta = int(delta)
    key = dac.strip().upper()
    if key in ("VTH_H", "VTHR_H", "HIGH", "ALTA"):
        name = "VTHR_H"
    elif key in ("VTH_L", "VTHR_L", "LOW", "BASSA"):
        name = "VTHR_L"
    else:
        raise ValueError("dac: usa VTHR_H o VTHR_L")
    hw.select_quadrant(q)
    r = hw.readAnalogColumnDAC(q, block=mat, dac=name)
    old_c = int(r["code"])
    new_c = max(0, min(127, old_c + delta))
    hw.AnalogColumnSetDAC(q, block=mat, dac=name, valore=new_c)
    rb = hw.readAnalogColumnDAC(q, block=mat, dac=name)
    return {
        "quad": q,
        "mat": mat,
        "dac": name,
        "delta": delta,
        "code_before": old_c,
        "code_after": int(rb["code"]),
        "enable": rb["enable"],
    }


def builtin_ftdac_dump(hw, quad: str, *, mat: int) -> dict[str, Any]:
    """Return FTDAC codes for logging / debugging (no hardware change)."""
    q = _q(quad)
    mat = int(mat)
    hw.select_quadrant(q)
    d = hw.readMatPixelsAndFTDAC(q, mattonella=mat)
    ft = d.get("ftdac")
    if not isinstance(ft, list):
        raise RuntimeError("readMatPixelsAndFTDAC fallita")
    codes = [int(x) & 0xF for x in ft[:64]]
    return {"quad": q, "mat": mat, "ftdac": codes}


def builtin_prepare_fifo_readout(hw, quad: str) -> dict[str, Any]:
    """
    Imposta readout TOP su I2C (necessario per ``FifoReadSingle`` / ``FifoDrain``) e seleziona il mux.
    Chiamare prima di misure FIFO o di ``CalibrateFTDAC`` se il readout non è già su I2C.
    """
    q = _q(quad)
    try:
        hw.TopReadout("i2c")
    except Exception as e:
        return {"quad": q, "ok": False, "error": str(e)}
    hw.select_quadrant(q)
    try:
        ro = str(hw.readTopReadout())
    except Exception:
        ro = "?"
    return {"quad": q, "ok": True, "readout": ro}


def builtin_tdc_mat(hw, quad: str, *, mat: int, enable: bool = True) -> dict[str, Any]:
    """Abilita o disabilita il TDC sulla mattonella; restituisce lettura stato."""
    q = _q(quad)
    mat = int(mat)
    if mat < 0 or mat > 15:
        raise ValueError("mat atteso 0..15")
    hw.EnableTDC(q, Mattonella=mat, enable=bool(enable))
    st = hw.readEnableTDC(q, Mattonella=mat)
    return {"quad": q, "mat": mat, "enable": bool(enable), "tdc_state": st}


def builtin_isolate_one_channel(hw, quad: str, *, mat: int, channel: int) -> dict[str, Any]:
    """
    Sulla mattonella indicata: spegne tutti i pixel, poi accende solo ``channel`` e il TDC.
    Stessa preparazione usata da ``CalibrateFTDAC`` prima dello scan FIFO (senza calibrare).
    """
    q = _q(quad)
    mat = int(mat)
    ch = int(channel)
    if mat < 0 or mat > 15 or ch < 0 or ch > 63:
        raise ValueError("mat 0..15, channel 0..63")
    hw.select_quadrant(q)
    for pix in range(64):
        hw.EnableDigPix(q, Mattonella=mat, Channel=pix, enable=False)
    hw.EnableTDC(q, Mattonella=mat, enable=False)
    hw.EnableDigPix(q, Mattonella=mat, Channel=ch, enable=True)
    hw.EnableTDC(q, Mattonella=mat, enable=True)
    return {"quad": q, "mat": mat, "channel": ch, "isolated": True}


def builtin_mat_summary(hw, quad: str, *, mat: int) -> dict[str, Any]:
    """Conta pixel accesi e statistiche codici FTDAC 0..15 per una MAT."""
    q = _q(quad)
    mat = int(mat)
    if mat < 0 or mat > 15:
        raise ValueError("mat atteso 0..15")
    hw.select_quadrant(q)
    d = hw.readMatPixelsAndFTDAC(q, mattonella=mat)
    px = d.get("pix_on")
    ft = d.get("ftdac")
    if not isinstance(px, list) or not isinstance(ft, list):
        raise RuntimeError("readMatPixelsAndFTDAC fallita")
    n_on = sum(1 for b in px[:64] if b)
    ftc = [int(x) & 0xF for x in ft[:64]]
    return {
        "quad": q,
        "mat": mat,
        "n_pix_on": n_on,
        "ftdac_min": min(ftc),
        "ftdac_max": max(ftc),
    }


def builtin_fifo_drain(hw, quad: str, *, max_words: int = 512) -> dict[str, Any]:
    """Svuota la FIFO fino a vuoto o ``max_words``; utile per pulire prima di una misura."""
    q = _q(quad)
    hw.select_quadrant(q)
    mw = max(1, int(max_words))
    words = hw.FifoDrain(max_words=mw)
    sample = [int(w) for w in words[:8]]
    return {"quad": q, "n_words_read": len(words), "max_words_cap": mw, "first_words_u64": sample}


def builtin_read_analog_power_state(hw, quad: str) -> dict[str, Any]:
    """Legge se l’alimentazione analogica risulta ON (globale IOext, non per quadrante)."""
    _ = _q(quad)
    on = bool(hw.readAnalogPower())
    return {"analog_power_on": on}


BUILTIN_FUNCS: dict[str, Callable[..., Any]] = {
    "ftdac_delta_mat": builtin_ftdac_delta_mat,
    "ftdac_cal_mat": builtin_ftdac_cal_mat,
    "ftdac_cal_ch": builtin_ftdac_cal_ch,
    "ftdac_cal_channels": builtin_ftdac_cal_channels,
    "pixels_all_off_quad": builtin_pixels_all_off_quad,
    "pixel_on": builtin_pixel_on,
    "pixel_off": builtin_pixel_off,
    "mat_all_pixels_on": builtin_mat_all_pixels_on,
    "mat_all_pixels_off": builtin_mat_all_pixels_off,
    "quad_all_pixels_on": builtin_quad_all_pixels_on,
    "vthr_bump": builtin_vthr_bump,
    "ftdac_dump": builtin_ftdac_dump,
    "prepare_fifo_readout": builtin_prepare_fifo_readout,
    "tdc_mat": builtin_tdc_mat,
    "isolate_one_channel": builtin_isolate_one_channel,
    "mat_summary": builtin_mat_summary,
    "fifo_drain": builtin_fifo_drain,
    "read_analog_power_state": builtin_read_analog_power_state,
}


def run_builtin(
    macro_id: str,
    hw: Any,
    quad: str,
    *,
    progress: Optional[Callable[[str], None]] = None,
    **kwargs: Any,
) -> Any:
    fn = BUILTIN_FUNCS.get(macro_id)
    if fn is None:
        raise ValueError(f"Macro sconosciuta: {macro_id!r}")
    p = progress or (lambda _m: None)
    p(f"Macro {macro_id} …")
    out = fn(hw, quad, **kwargs)
    p("Macro completata.")
    return out
