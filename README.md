# IGNITE64 — sorgenti testbench e tooling

Repository che raccoglie:

| Cartella | Contenuto |
|----------|-----------|
| **`icboost/`** | Pacchetto **ICBoost** (import `icboost`) — wrapper **Python** per le DLL di comunicazione I2C (`TCPtoI2C.dll`, `USBtoI2C32.dll`), API alto livello (`api.py`), GUI **Tk** (`gui_tk.py`), macro, documentazione e file di configurazione esempio. *Clone molto vecchi possono avere ancora la cartella come `ignite64py/`; è lo stesso contenuto.* |
| **`tb_Ignite64/`**, `tb_Ignite64.csproj` | Applicazione **C# .NET Framework** di riferimento (GUI legacy): utile per confronto comportamento con il wrapper Python. |

## Avvio rapido (Python)

```text
cd icboost
python -m venv .venv
.venv\Scripts\activate
python -m pip install -e .
python examples\gui_monitor.py
```

Se la cartella progetto sul disco si chiama ancora `ignite64py`, usa `cd ignite64py` al posto di `cd icboost`.

Documentazione dettagliata:

- **[icboost/docs/INSTALLAZIONE_WINDOWS.md](icboost/docs/INSTALLAZIONE_WINDOWS.md)** — Python, Pillow, DLL, variabili d’ambiente, uso GUI.
- **[icboost/docs/GUIDA_HW_E_MACRO.md](icboost/docs/GUIDA_HW_E_MACRO.md)** — API `hw`, macro operative, esempi.
- **[icboost/README.md](icboost/README.md)** — panoramica pacchetto e note di mappatura dai registri C#.

## Sviluppo con Cursor / clone

Dopo il clone, apri la **cartella radice di questo repository** in Cursor: il file **`AGENTS.md`** e la cartella **`.cursor/rules/`** forniscono contesto e convenzioni all’assistente (non sostituiscono la lettura del codice).

Per contribuire o pubblicare su Git, vedi **[CONTRIBUTING.md](CONTRIBUTING.md)**.

## DLL

Il bridge hardware **`TCPtoI2C.dll`** e **`USBtoI2C32.dll`** sono nella cartella **`icboost/`** nel clone; per percorsi alternativi (PATH, `dll_dir=`) vedi **[icboost/docs/INSTALLAZIONE_WINDOWS.md](icboost/docs/INSTALLAZIONE_WINDOWS.md)**.
