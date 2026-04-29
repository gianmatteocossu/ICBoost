# Contesto progetto (per assistenti AI / Cursor)

Questo file riassume **cosa contiene il repository** e **dove guardare**, così uno sviluppatore che apre il progetto in Cursor (o un clone dopo mesi) ha contesto senza dipendere da una singola chat.

## Cos’è

- **Monorepo** per lavoro su chip IGNITE64: applicazione **C#** di riferimento (`tb_Ignite64`, .NET Framework) e wrapper **Python** **`ignite64py`** che espone le stesse DLL USB/TCP (`TCPtoI2C.dll`, `USBtoI2C32.dll`) con API tipo `Ignite64` e GUI Tk.

## Percorsi chiave (Python)

| Percorso | Ruolo |
|----------|--------|
| `ignite64py/ignite64py/api.py` | API alto livello: FIFO, TOP, pixel, DAC, FTDAC, `CalibrateFTDAC`, caricamento config. |
| `ignite64py/ignite64py/device.py` | I2C, `select_quadrant`, trasporto DLL. |
| `ignite64py/ignite64py/gui_tk.py` | GUI monitor; `run_gui()` avvio; navigazione Quadrants / blocchi / analog. |
| `ignite64py/ignite64py/macros_library.py` | Funzioni `builtin_*` usate dalle macro GUI. |
| `ignite64py/examples/gui_monitor.py` | Entrypoint GUI tipico (`OFFLINE` default 1). |
| `ignite64py/examples/macros/*.py` | Macro `source nome.py` dalla GUI. |
| `ignite64py/docs/GUIDA_HW_E_MACRO.md` | Riferimento API e macro (dettagliato). |
| `ignite64py/docs/INSTALLAZIONE_WINDOWS.md` | Setup Python, Pillow, DLL, variabili ambiente. |

## Convenzioni

- Quadranti: stringhe `SW`, `NW`, `SE`, `NE`. MAT: `0..15`. Canale pixel: `0..63`.
- Modifiche GUI: preferire patch piccole; non rimuovere commenti utili al mapping C#.
- Macro: parametri in cima al file in `examples/macros/`; logica condivisa in `macros_library`.

## Cosa non è in repo

- **DLL** vendor (spesso ignorate da `.gitignore`): vanno copiate come da `INSTALLAZIONE_WINDOWS.md`.

## Chat storiche

Le conversazioni passate non sono salvate nel repo. Questo `AGENTS.md` + `.cursor/rules/` + `docs/` sono la fonte di verità aggiornabile dal team.
