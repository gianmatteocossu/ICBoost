"""
Macro: riepilogo stato MAT (pixel accesi, min/max FTDAC)
Sola lettura I2C; non modifica la configurazione.
"""

from ignite64py.macros_library import builtin_mat_summary

# --- Parametri operativi ---
MAT = 0


def leggi_stato_mat(hw, quad):
    print(f"[leggi_stato_mat]  quad={quad!r}  MAT={MAT}")
    r = builtin_mat_summary(hw, quad, mat=MAT)
    print(
        f"[leggi_stato_mat]  PIX_on={r.get('n_pix_on')}  "
        f"FTDAC min/max={r.get('ftdac_min')}/{r.get('ftdac_max')}"
    )
    return r
