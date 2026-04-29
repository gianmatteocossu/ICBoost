"""
Macro: accensione intera mattonella
Accende tutti i 64 canali della mattonella specificata (quadrante = menu «Quadrant»).
"""

from icboost.macros_library import builtin_mat_all_pixels_on

# --- Parametri operativi (0..15) ---
MAT = 0


def accendi_mat(hw, quad):
    print(f"[accendi_mat]  quad={quad!r}  MAT={MAT}")
    r = builtin_mat_all_pixels_on(hw, quad, mat=MAT)
    print(f"[accendi_mat]  OK  {r}")
    return r
