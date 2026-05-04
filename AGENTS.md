# Contesto progetto (per assistenti AI / Cursor)

Questo file riassume **cosa contiene il repository** e **dove guardare**, così uno sviluppatore che apre il progetto in Cursor (o un clone dopo mesi) ha contesto senza dipendere da una singola chat.

## Cos’è

- **Monorepo** per lavoro su chip IGNITE64: applicazione **C#** di riferimento (`tb_Ignite64`, .NET Framework) e wrapper **Python** **ICBoost** (`import icboost`) che espone le stesse DLL USB/TCP (`TCPtoI2C.dll`, `USBtoI2C32.dll`) con API tipo `Ignite64` e GUI Tk.

## Percorsi chiave (Python)

La cartella progetto si chiama in genere **`icboost/`** (contiene `pyproject.toml`); in working copy non ancora rinominate può comparire come `ignite64py/`.

| Percorso | Ruolo |
|----------|--------|
| `<cartella-progetto>/icboost/api.py` | API alto livello: FIFO, TOP, pixel, DAC, FTDAC, `CalibrateFTDAC`, caricamento config. |
| `<cartella-progetto>/icboost/device.py` | I2C, `select_quadrant`, trasporto DLL. |
| `<cartella-progetto>/icboost/gui_tk.py` | GUI monitor; `run_gui()` avvio; navigazione Quadrants / blocchi / analog. |
| `<cartella-progetto>/icboost/macros_library.py` | Funzioni `builtin_*` usate dalle macro GUI. |
| `<cartella-progetto>/examples/gui_monitor.py` | Entrypoint GUI tipico (`OFFLINE` default 1; `START_CONFIG` default **auto** = `init_hw` senza riscrivere TOP/MAT). |
| `<cartella-progetto>/examples/macros/*.py` | Macro `source nome.py` dalla GUI. |
| `<cartella-progetto>/docs/GUIDA_HW_E_MACRO.md` | Riferimento API e macro (dettagliato). |
| `<cartella-progetto>/docs/INSTALLAZIONE_WINDOWS.md` | Setup Python, Pillow, DLL, variabili ambiente. |

Oggi `<cartella-progetto>` può essere ancora `ignite64py/`; dopo la rinomina in `icboost/` i percorsi relativi alla radice del repo restano gli stessi sostituendo solo quel prefisso.

## Convenzioni

- Quadranti: stringhe `SW`, `NW`, `SE`, `NE`. MAT: `0..15`. Canale pixel: `0..63`.
- Modifiche GUI: preferire patch piccole; non rimuovere commenti utili al mapping C#.
- Macro: parametri in cima al file in `examples/macros/`; logica condivisa in `macros_library`.
- Pixel: **`AnalogChannelON`/`OFF` = solo PIXON (bit6)**; front-end analogico per-pixel = **`setAnalogFEON`/`readAnalogFEON` (bit7)** — vedi `docs/GUIDA_HW_E_MACRO.md` §1 e §5.3 (il nome “AnalogChannel” è legacy C#).

## Binari e cosa resta fuori

- **DLL bridge** del wrapper: **`icboost/TCPtoI2C.dll`** e **`icboost/USBtoI2C32.dll`** sono nel repository per un clone installabile senza copie manuali (dettagli in `INSTALLAZIONE_WINDOWS.md`).
- **Non** versionati: cartelle di build C# (`bin/`, `obj/`), ambienti virtuali Python, altri binari non richiesti dal pacchetto `icboost`.

## Chat storiche

Le conversazioni passate non sono salvate nel repo. Questo `AGENTS.md` + `.cursor/rules/` + `docs/` sono la fonte di verità aggiornabile dal team.
