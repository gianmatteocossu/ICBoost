"""
Macro: accensione singolo canale
Imposta PIXON per un solo pixel (mattonella + indice 0..63) nel quadrante del menu «Quadrant».
"""

from icboost.macros_library import builtin_pixel_on

# --- Parametri operativi ---
MAT = 0
CHANNEL = 0  # 0..63


def accendi_canale(hw, quad):
    print(f"[accendi_canale]  quad={quad!r}  MAT={MAT}  CH={CHANNEL}")
    r = builtin_pixel_on(hw, quad, mat=MAT, channel=CHANNEL)
    print(f"[accendi_canale]  OK  {r}")
    return r
