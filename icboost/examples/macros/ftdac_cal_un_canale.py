"""
Macro: calibrazione FTDAC su un canale (FIFO)
Calibrazione soglia FineTune per un solo indice pixel sulla mattonella impostata.
"""

from icboost.macros_library import builtin_ftdac_cal_ch

# --- Parametri operativi ---
MAT = 0
CHANNEL = 0  # 0..63


def ftdac_cal_un_canale(hw, quad):
    print(f"[ftdac_cal_un_canale]  quad={quad!r}  MAT={MAT}  CH={CHANNEL}")
    r = builtin_ftdac_cal_ch(hw, quad, mat=MAT, channel=CHANNEL)
    print(f"[ftdac_cal_un_canale]  OK  codice={r.get('calibrated_code')!r}  dettagli={r}")
    return r
