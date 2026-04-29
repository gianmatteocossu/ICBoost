"""
Macro: spegnimento intera mattonella
Spegne i 64 canali (0..63) della mattonella indicata nel quadrante del menu «Quadrant».
"""

from ignite64py.macros_library import builtin_mat_all_pixels_off

# --- Parametri operativi (0..15) ---
MAT = 0


def spegni_mat(hw, quad):
    print(f"[spegni_mat]  quad={quad!r}  MAT={MAT}")
    r = builtin_mat_all_pixels_off(hw, quad, mat=MAT)
    print(f"[spegni_mat]  OK  {r}")
    return r
