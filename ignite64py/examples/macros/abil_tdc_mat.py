"""
Macro: abilitazione / disabilitazione TDC su una mattonella
"""

from ignite64py.macros_library import builtin_tdc_mat

# --- Parametri operativi ---
MAT = 0
ENABLE = True  # False per spegnere solo il TDC (pixel invariati)


def abil_tdc_mat(hw, quad):
    print(f"[abil_tdc_mat]  quad={quad!r}  MAT={MAT}  ENABLE={ENABLE}")
    r = builtin_tdc_mat(hw, quad, mat=MAT, enable=ENABLE)
    print(f"[abil_tdc_mat]  stato letto: {r.get('tdc_state')}")
    return r
