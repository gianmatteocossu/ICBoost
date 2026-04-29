"""
Macro: lettura stato alimentazione analogica globale (IOext)
Il risultato non dipende dal quadrante del menu; ``quad`` è comunque passato dalla GUI.
"""

from ignite64py.macros_library import builtin_read_analog_power_state


def verifica_power_analogico(hw, quad):
    print(f"[verifica_power_analogico]  (quad menu={quad!r})")
    r = builtin_read_analog_power_state(hw, quad)
    on = r.get("analog_power_on")
    print(f"[verifica_power_analogico]  Analog Power ON = {on}")
    return r
