"""
Macro: calibrazione FTDAC su tutta la mattonella (FIFO)
Esegue ``CalibrateFTDAC(..., Channel='ALL')``: tutti i 64 canali in sequenza.
Durata elevata; richiede readout TOP su I2C, FIFO accessibile, banco stabile.
"""

from icboost.macros_library import builtin_ftdac_cal_mat

# --- Parametri operativi (0..15) ---
MAT = 0


def calib_ftdac_mat(hw, quad):
    print(f"[calib_ftdac_mat]  Inizio  quad={quad!r}  MAT={MAT}  (64 canali, lungo)")
    r = builtin_ftdac_cal_mat(hw, quad, mat=MAT)
    print(f"[calib_ftdac_mat]  Completato  chiavi risultato: {list(r.keys()) if isinstance(r, dict) else type(r)}")
    return r
