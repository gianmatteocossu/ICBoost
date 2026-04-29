"""
Macro: incremento/decremento globale codici FTDAC (una mattonella)
Applica lo stesso scostamento Δ a tutti i codici FineTune 0..15 (clamp incluso) sui 64 canali della MAT.
"""

from ignite64py.macros_library import builtin_ftdac_delta_mat

# --- Parametri operativi ---
MAT = 0
DELTA = 1  # positivo = incremento, negativo = decremento


def ftdac_incrementa_mat(hw, quad):
    print(f"[ftdac_incrementa_mat]  quad={quad!r}  MAT={MAT}  DELTA={DELTA}")
    r = builtin_ftdac_delta_mat(hw, quad, mat=MAT, delta=DELTA)
    print(f"[ftdac_incrementa_mat]  OK  {r}")
    return r
