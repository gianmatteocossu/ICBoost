# IGNITE64 — sorgenti testbench e tooling

Repository che raccoglie:

| Cartella | Contenuto |
|----------|-----------|
| **`ignite64py/`** | Wrapper **Python** per le DLL di comunicazione I2C (`TCPtoI2C.dll`, `USBtoI2C32.dll`), API alto livello (`api.py`), GUI **Tk** (`gui_tk.py`), macro, documentazione e file di configurazione esempio. |
| **`tb_Ignite64/`**, `tb_Ignite64.csproj` | Applicazione **C# .NET Framework** di riferimento (GUI legacy): utile per confronto comportamento con il wrapper Python. |

## Avvio rapido (Python)

```text
cd ignite64py
python -m venv .venv
.venv\Scripts\activate
python -m pip install -e .
python examples\gui_monitor.py
```

Documentazione dettagliata:

- **[ignite64py/docs/INSTALLAZIONE_WINDOWS.md](ignite64py/docs/INSTALLAZIONE_WINDOWS.md)** — Python, Pillow, DLL, variabili d’ambiente, uso GUI.
- **[ignite64py/docs/GUIDA_HW_E_MACRO.md](ignite64py/docs/GUIDA_HW_E_MACRO.md)** — API `hw`, macro operative, esempi.
- **[ignite64py/README.md](ignite64py/README.md)** — panoramica pacchetto e note di mappatura dai registri C#.

## Sviluppo con Cursor / clone

Dopo il clone, apri la **cartella radice di questo repository** in Cursor: il file **`AGENTS.md`** e la cartella **`.cursor/rules/`** forniscono contesto e convenzioni all’assistente (non sostituiscono la lettura del codice).

Per contribuire o pubblicare su Git, vedi **[CONTRIBUTING.md](CONTRIBUTING.md)**.

## DLL

Le DLL del vendor non sono incluse nel repository (vedi `.gitignore`). Copiale nella cartella `ignite64py/` o nel `PATH` come descritto nella guida d’installazione.
