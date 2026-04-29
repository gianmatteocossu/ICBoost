# Contribuire e pubblicare su Git

## Identità Git (obbligatoria al primo `commit`)

Dopo aver installato Git, configura nome ed email (una sola volta sul PC):

```text
git config --global user.name "Nome Cognome"
git config --global user.email "tua.email@istituzione.it"
```

Solo per questo repository (senza `--global`):

```text
cd <cartella-repo>
git config user.name "Nome Cognome"
git config user.email "tua.email@istituzione.it"
```

## Primo clone (sviluppatore)

```text
git clone <URL-del-tuo-repository>
cd <cartella-repo>
```

Struttura attesa: radice con **`icboost/`** (pacchetto Python), `tb_Ignite64/`, `README.md`, `AGENTS.md`, `.gitignore`. Il nome della cartella progetto può essere ancora `ignite64py/` finché non viene rinominata.

### Ambiente Python

Seguire **[ignite64py/docs/INSTALLAZIONE_WINDOWS.md](ignite64py/docs/INSTALLAZIONE_WINDOWS.md)** finché la cartella non è rinominata — poi **`icboost/docs/...`** (venv, `pip install -e .` dalla cartella progetto).

### DLL

Posizionare `TCPtoI2C.dll` e `USBtoI2C32.dll` come da documentazione (cartella progetto `icboost/` o `PATH`). Non vengono committate di default (`.gitignore` include `*.dll`). In un fork **privato**, se la licenza lo consente, si può rimuovere o commentare quella riga in `.gitignore`.

## Cosa versionare

| Versionare | Evitare (già in .gitignore) |
|------------|------------------------------|
| Sorgenti `.py`, `.cs`, `pyproject.toml`, `docs/`, `.cursor/rules/`, `AGENTS.md` | `__pycache__`, `.venv`, `*.egg-info`, `obj/`, `bin/` |
| `ConfigurationFiles/` testo, asset immagini in `<cartella-progetto>/icboost/assets/` | DLL vendor (salvo policy diversa) |

## Primo commit (esempio)

Dalla **radice** del repository:

```text
git init
git add .
git status
git commit -m "Importazione sorgenti IGNITE64 (Python + C# reference)"
```

Aggiungere il remote e fare push:

```text
git remote add origin <URL>
git branch -M main
git push -u origin main
```

Repository GitHub previsto: **https://github.com/gianmatteocossu/ICBoost.git**

## Linee guida modifiche

- Modifiche **mirate** al bisogno descritto in issue / task.
- Per il solo Python: preferire coerenza con `api.py` / `gui_tk.py` esistenti.
- Documentazione: aggiornare `docs/` o `README` quando cambiano dipendenze o avvio.

## Cursor

Chi usa **Cursor** trova in **`AGENTS.md`** il contesto progetto; le regole in **`.cursor/rules/*.mdc`** guidano l’AI sullo stile e sui percorsi principali. Non committare cache personale non necessaria.
