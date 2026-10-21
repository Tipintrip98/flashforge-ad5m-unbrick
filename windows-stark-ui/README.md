# Stark HUD — Windows 10 UI Pack

Pack che cambia l'interfaccia di Windows 10 in stile **HUD Stark Industries**, con la pulizia di **Apple / Apple TV** e le schede arrotondate di **Xiaomi HyperOS**.
**Non tocca** file di sistema, servizi, driver o processi di sistema. Ogni modifica viene salvata e si annulla con un clic.

![Sfondo animato con orologio, telemetria e Now Playing](docs/img/preview-live.jpg)

> Progetto fan-made non ufficiale, senza legami con Marvel o Disney. Non contiene loghi né materiale protetto: grafica, cursori e suoni sono originali e generati dagli script in `tools/`.

---

## Installazione (3 passi)

1. **Scarica** `StarkHUD-Setup.exe` dalla cartella [`W`](../W) del repository (un solo file, contiene tutto il pack).
2. **Doppio clic** sull'exe. Se Windows mostra "Windows ha protetto il PC": **Ulteriori informazioni → Esegui comunque** (l'exe non ha una firma digitale a pagamento).
3. Scegli cosa attivare e premi **INSTALLA**.
   Se compaiono richieste UAC (scaricamento delle app con winget), accettale.

In alternativa: scarica questa cartella (`Code → Download ZIP`), estrai lo ZIP e fai doppio clic su `Installa.cmd`.

Non servono privilegi di amministratore per il tema. Alla fine Esplora risorse si riavvia da solo per applicare barra e colori.

## Tornare a com'era prima

- Nell'installer: **Ripristina originale**.
- Se l'installer non si apre: doppio clic su `Ripristina.cmd`.

Il ripristino riporta indietro tema, colori, sfondo, schermata di blocco, cursori, suoni, font, impostazioni modificate e file sostituiti.
Le app installate restano, a meno che tu non attivi **Rimuovi anche le app**.

---

## Cosa cambia

| Parte di Windows | Cosa diventa | Come |
|---|---|---|
| Colori e modalità | Scuro, accento ciano reattore, tavolozza con oro Stark, trasparenze | Registro utente + API di sistema (crea anche il tema "Stark HUD" in Impostazioni → Temi) |
| Sfondo | "Arc Reactor" 4K (versione ultrawide inclusa) | Nativo |
| Schermata di blocco e accesso | Reattore centrato | API Windows (WinRT) |
| Puntatori | 17 cursori vettoriali multi-risoluzione, attesa animata | Schema cursori nativo |
| Suoni | Notifiche "vetro", errori morbidi, cestino "dissolvenza", power-up all'accesso | Schema suoni nativo |
| Font | Rajdhani + Share Tech Mono (licenza OFL) | Solo per il tuo utente |
| Sfondo animato | Anelli che ruotano, telemetria CPU/RAM/GPU/rete, Now Playing | [Lively Wallpaper](https://github.com/rocksdanister/lively) |
| Widget desktop | Orologio stile Apple TV, meteo, anelli stile Apple Watch, control center stile HyperOS | [Rainmeter](https://github.com/rainmeter/rainmeter) |
| Ricerca | Barra stile Spotlight con `Alt+Spazio` | [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher) |
| Barra delle applicazioni | Trasparente sul desktop, acrilico con finestre aperte | [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) |
| Icone della barra (opzionale) | Centrate stile dock | [TaskbarX](https://github.com/ChrisAnd1998/TaskbarX) |
| Menu tasto destro (opzionale) | Acrilico scuro con accenti ciano | [Nilesoft Shell](https://github.com/moudey/Shell) |

![Sfondo statico 4K](docs/img/preview-desktop.jpg)

![Cursori su sfondo scuro e chiaro](docs/img/cursors-preview.png)

## Tre livelli di sicurezza

| Livello | Cosa include | Tocca processi di sistema? |
|---|---|---|
| **1 — Nativo** | Tema, colori, sfondo, blocco schermo, cursori, suoni, font, ottimizzazioni | No. Solo impostazioni dell'utente (HKCU) |
| **2 — Overlay** | Lively, Rainmeter, Flow Launcher, TranslucentTB, TaskbarX | No. App separate, open source, senza iniezioni |
| **3 — Avanzato** (spento di default) | Nilesoft Shell, Windhawk | Si agganciano a Esplora risorse (non a componenti critici). Facoltativo |

I progetti che si iniettano in `dwm.exe` (il compositore grafico di Windows) sono stati **esclusi di proposito**. Il perché è spiegato in [docs/RICERCA.md](docs/RICERCA.md).

## Ottimizzazioni incluse

Tutte riguardano solo il tuo utente, tutte sono reversibili. Le trovi nella scheda **Ottimizzazioni** e puoi attivarle una per una.

- **Fluidità:** menu più reattivi (400 → 120 ms), avvio del desktop senza ritardo artificiale, testo ClearType.
- **Prestazioni:** modalità gioco automatica, niente registrazione Game DVR in background, app dello Store in background (spenta di default), piano energetico "Prestazioni elevate" (spento di default; consuma batteria).
- **Pulizia:** niente pubblicità né app suggerite, schermata di blocco senza Spotlight, ricerca Start solo locale (niente Bing), pulizia dei file temporanei più vecchi di 3 giorni.
- **Interfaccia:** barra minimale, niente "Notizie e interessi", Esplora file apre "Questo PC". Opzionali: estensioni dei file visibili, desktop senza icone, barre del titolo color reattore.

L'interfaccia resta leggera:

- Gli anelli dello sfondo animato ruotano sulla GPU: costo CPU quasi zero.
- Le particelle sono limitate a 30 FPS e restano vicino al reattore.
- Lively mette in pausa lo sfondo con giochi e video a schermo intero.
- I widget Rainmeter si aggiornano una volta al secondo; il control center non si aggiorna mai.

**Cosa non fa, per scelta:** non disattiva Windows Defender, servizi o aggiornamenti; non rimuove app di sistema; non modifica `uxtheme.dll` o altri file di sistema.

---

## Requisiti

- Windows 10 a 64 bit, versione 1809 o successiva (consigliata 22H2). Su Windows 11 funziona in parte.
- Windows PowerShell 5.1, già presente in Windows 10.
- Per le app del Livello 2: **winget** ("Programma di installazione app" dal Microsoft Store). Senza winget si installa solo il Livello 1.

## Personalizzare

- **Sfondo animato:** apri Lively, clic destro su "Stark HUD - Arc Reactor" → **Personalizza**. Puoi cambiare colori, posizione, velocità, particelle, FPS, orologio, telemetria e "Riduci movimento".
- **Widget:** trascinali dove vuoi. Clic destro → Rainmeter → Modifica skin. Città meteo e scala sono in `Documenti\Rainmeter\Skins\StarkHUD\@Resources\Variables.inc`.
- **Ricerca:** `Alt+Spazio`. Il tema si chiama "Stark HUD" in Flow Launcher → Impostazioni → Aspetto.

## Riga di comando

```powershell
# installazione senza interfaccia (preset: Default, Minimal = solo nativo, Full = tutto)
powershell -ExecutionPolicy Bypass -File .\StarkUI.ps1 -Install -Preset Default -City Roma -Yes

# ripristino completo, rimuovendo anche le app installate dal pack
powershell -ExecutionPolicy Bypass -File .\StarkUI.ps1 -Restore -RemoveApps -Yes
```

## Problemi frequenti

| Problema | Soluzione |
|---|---|
| L'installer non si apre | Apri `Ripristina.cmd` e rispondi **N**: se compare la finestra di PowerShell, PowerShell funziona. Poi controlla il log in `%LOCALAPPDATA%\StarkHUD\logs`. Non serve "Esegui come amministratore". |
| "winget mancante" | Installa "Programma di installazione app" dal Microsoft Store e riapri l'installer. |
| Il colore della barra non cambia | Disconnettiti e rientra (o riavvia Esplora risorse dal Task Manager). |
| Lo sfondo animato non parte | Apri Lively e scegli "Stark HUD - Arc Reactor" dalla libreria. |
| Il meteo dice "in aggiornamento..." | Controlla la connessione; il meteo si aggiorna ogni 30 minuti da wttr.in. |
| Il punto di ripristino non viene creato | Protezione sistema è disattivata, oppure ne esiste già uno delle ultime 24 ore. Il backup Stark HUD funziona comunque. |

Dove sono i file:

- Backup: `%LOCALAPPDATA%\StarkHUD\state`
- Log: `%LOCALAPPDATA%\StarkHUD\logs`

---

## Per sviluppatori

```
windows-stark-ui/
├── Installa.cmd / Ripristina.cmd   avvio con doppio clic
├── StarkUI.ps1                     installer (GUI WPF + riga di comando)
├── src/StarkHUD.psm1               motore: backup, tema, componenti, ripristino
├── src/MainWindow.xaml             interfaccia dell'installer
├── config/tweaks.json              catalogo ottimizzazioni (solo HKCU)
├── theme/                          sfondi, cursori, suoni, font
├── components/                     Lively, Rainmeter, Flow Launcher, TranslucentTB, Nilesoft
├── tools/                          generatori degli asset e Build-Setup.ps1 (crea ../W/StarkHUD-Setup.exe)
├── tests/Test-StarkCore.ps1        test delle funzioni pure
└── docs/                           ricerca e immagini
```

Rigenerare gli asset (Node.js con Playwright, Python con numpy):

```bash
node tools/build-cursors.js       # cursori .cur/.ani + anteprima
python3 tools/gen_sounds.py       # schema suoni .wav
node tools/render-wallpapers.js   # sfondi 4K, ultrawide, blocco schermo, anteprime
```

Test:

- Funzioni pure: `powershell -ExecutionPolicy Bypass -File tests\Test-StarkCore.ps1`. Funziona anche con `pwsh` su Linux e macOS.
- Test completo su Windows (**modifica davvero il sistema**: solo su VM o Windows Sandbox): `powershell -ExecutionPolicy Bypass -STA -File tests\Test-StarkWindows.ps1 [-Components]`.
- Su GitHub, solo ad avvio manuale (Actions → "Stark HUD - test Windows" → Run workflow): `.github/workflows/stark-hud-windows.yml`. Ricostruisce anche `W/StarkHUD-Setup.exe` se il pack è cambiato.

**Cosa è stato verificato e cosa no:**

- **Test su Windows reale.** A richiesta, GitHub Actions avvia due macchine Windows usa e getta ed esegue `tests/Test-StarkWindows.ps1`:
  - **Windows Server 2022** (build 20348, stessa base di Windows 10): tema nativo e ottimizzazioni;
  - **Windows Server 2025**: tema più tutte le app (Rainmeter, Lively, Flow Launcher, TranslucentTB, TaskbarX, Nilesoft), installate e poi disinstallate.
- **Cosa controlla il test:**
  - cursori, suoni, font e XAML caricati con le API di Windows;
  - ogni modifica verificata dopo l'installazione;
  - una seconda installazione, che non deve duplicare il backup;
  - il ripristino, con il registro utente confrontato valore per valore con quello iniziale;
  - l'apertura dell'interfaccia, con uno screenshot di ogni scheda.
- **Errori trovati così e corretti:** sono elencati in [docs/RICERCA.md](docs/RICERCA.md#5-test-su-windows-reale-errori-trovati-e-corretti).
- **Altre verifiche:**
  - le funzioni pure passano 39 test automatici, anche con Windows PowerShell 5.1;
  - gli script passano PSScriptAnalyzer;
  - i formati di configurazione delle app sono stati controllati sul loro codice sorgente.
- **Limiti:**
  - i runner sono Windows Server, non un Windows 10 Home/Pro con un utente reale, e non sempre hanno il desktop di Esplora risorse;
  - su base Windows 11 il colore di bordi e barre del titolo a volte non torna da solo dopo il ripristino: in quel caso il ripristino lo segnala e dice come sistemarlo ([dettagli](docs/RICERCA.md#5-test-su-windows-reale-errori-trovati-e-corretti));
  - il primo utilizzo resta consigliato con il punto di ripristino attivo (l'installer lo crea da solo).

## Crediti e licenze

- Codice, grafica, cursori e suoni di questo pack: MIT (vedi `LICENSE` alla radice del repository).
- Font: Rajdhani (Indian Type Foundry) e Share Tech Mono (Carrois), SIL Open Font License 1.1. Licenze in `theme/fonts/`.
- Le app di terze parti vengono scaricate dalle fonti ufficiali tramite winget e mantengono le loro licenze. TaskbarX viene scaricato da GitHub e verificato con SHA256.
