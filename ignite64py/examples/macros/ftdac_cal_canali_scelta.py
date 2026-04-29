"""
Macro: calibrazione FTDAC su più canali (stessa mattonella, FIFO)
Esegue una calibrazione FIFO per ogni indice in ``CHANNELS`` sulla ``MAT`` indicata.
Il quadrante è quello del menu «Quadrant».
"""

from icboost.macros_library import builtin_ftdac_cal_channels

# --- Parametri operativi ---
MAT = 0
CHANNELS = [0, 1, 2]  # lista non vuota; indici 0..63


def ftdac_cal_canali_scelta(hw, quad):
    print(f"[ftdac_cal_canali_scelta]  quad={quad!r}  MAT={MAT}  canali={CHANNELS}")
    r = builtin_ftdac_cal_channels(hw, quad, mat=MAT, channels=CHANNELS)
    results = r.get("results", {})
    for ch in r.get("channels", []):
        res = results.get(ch)
        cc = res.get("calibrated_code") if isinstance(res, dict) else None
        print(f"  canale {ch}:  calibrated_code={cc!r}")
    print(f"[ftdac_cal_canali_scelta]  Completato  n={len(results)}")
    return r
