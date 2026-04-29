"""
Macro: spegnimento completo quadrante
Spegne ogni canale (bit PIXON) su tutte le mattonelle 0..15 del quadrante attivo nel menu «Quadrant».
Nessun altro parametro: selezionare il quadrante corretto nel menu prima di Source.
"""

from icboost.macros_library import builtin_pixels_all_off_quad


def spegnitutto(hw, quad):
    print(f"[spegnitutto] Inizio  quad={quad!r}")
    r = builtin_pixels_all_off_quad(hw, quad)
    print(f"[spegnitutto] Completato  {r}")
    return r
