"""
Macro: accensione intero quadrante
Accende 16×64 canali (tutte le mattonelle). Operazione lunga; verificare alimentazione e integrità termica.
Il quadrante è quello selezionato nel menu «Quadrant».
"""

from ignite64py.macros_library import builtin_quad_all_pixels_on


def accendi_quad_tutti(hw, quad):
    print(f"[accendi_quad_tutti]  Inizio  quad={quad!r}  (16×64 canali)")
    r = builtin_quad_all_pixels_on(hw, quad)
    print(f"[accendi_quad_tutti]  Completato  {r}")
    return r
