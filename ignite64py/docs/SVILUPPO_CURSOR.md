# Sviluppo con Cursor e contesto persistente

Una **chat** in Cursor non viene salvata automaticamente nel repository. Per lavorare in team o dopo un clone, il contesto utile è versionato così:

| Elemento | Posizione | Scopo |
|----------|-----------|--------|
| **AGENTS.md** | Radice del repository (cartella che contiene `ignite64py/` e `tb_Ignite64/`) | Panoramica percorsi, convenzioni, cosa non è in repo (DLL). Cursor lo usa come contesto progetto. |
| **.cursor/rules/*.mdc** | Stessa radice | Regole per l’assistente (panoramica repo, convenzioni Python). Modificabili dal team. |
| **Documentazione** | `docs/` in questa cartella | Installazione, guida API/macro, riferimenti operativi. |
| **CONTRIBUTING.md** | Radice | Clone, `git`, cosa versionare, DLL. |

**Aprire in Cursor la cartella radice del repo** (non solo `ignite64py/`), così `AGENTS.md` e `.cursor/rules/` sono nel workspace.

Aggiornare `AGENTS.md` quando cambiano entrypoint importanti o la struttura delle cartelle.
