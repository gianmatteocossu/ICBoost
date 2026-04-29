"""
Macro: step su soglia discriminatore (VTHR_H o VTHR_L)
Legge il codice DAC corrente sulla mattonella, applica ``delta`` (somma algebrica, clamp 0..127), rilegge.
"""

from ignite64py.macros_library import builtin_vthr_bump

# --- Parametri operativi ---
MAT = 0
DAC = "VTHR_H"  # oppure "VTHR_L"
DELTA = 2  # positivo o negativo


def soglia_vthr_step(hw, quad):
    print(f"[soglia_vthr_step]  quad={quad!r}  MAT={MAT}  DAC={DAC}  DELTA={DELTA}")
    r = builtin_vthr_bump(hw, quad, mat=MAT, dac=DAC, delta=DELTA)
    print(f"[soglia_vthr_step]  OK  code {r.get('code_before')} → {r.get('code_after')}")
    return r
