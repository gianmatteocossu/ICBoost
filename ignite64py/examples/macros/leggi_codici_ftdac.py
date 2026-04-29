"""
Macro: lettura e stampa codici FineTune (0..15) per tutti i 64 canali della MAT
Sola lettura; utile per log prima/dopo calibrazione o dopo ``ftdac_incrementa_mat``.
"""

from icboost.macros_library import builtin_ftdac_dump

# --- Parametri operativi ---
MAT = 0
# stampa a blocchi di N codici per riga
PER_LINE = 16


def leggi_codici_ftdac(hw, quad):
    print(f"[leggi_codici_ftdac]  quad={quad!r}  MAT={MAT}")
    r = builtin_ftdac_dump(hw, quad, mat=MAT)
    codes = r.get("ftdac", [])
    for row in range(0, 64, PER_LINE):
        chunk = codes[row : row + PER_LINE]
        line = " ".join(f"{int(c):2d}" for c in chunk)
        print(f"  ch {row:2d}-{row + len(chunk) - 1:2d}:  {line}")
    return r
