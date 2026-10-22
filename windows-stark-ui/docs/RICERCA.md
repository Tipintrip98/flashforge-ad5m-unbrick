# Ricerca: personalizzare la UI di Windows 10 senza comprometterla

Questa nota spiega quali progetti open source di **reverse engineering della shell di Windows** sono stati valutati (stato a ottobre 2026), quali sono stati usati e perché.
Il criterio guida è la richiesta di partenza: *cambiare tutta l'interfaccia senza toccare i processi fondamentali del sistema operativo*.

## 1. Progetti di reverse engineering valutati

| Progetto | Windows 10 | Cosa aggancia | Stato | Licenza | Scelta |
|---|---|---|---|---|---|
| [Windhawk](https://github.com/ramensoftware/windhawk) | Sì | Il processo indicato da ogni mod (explorer.exe, dwm.exe...) | Attivo (v1.7.3, ott 2026) | GPL-3.0 | **Opzionale, Livello 3**: installato su richiesta, le mod le scegli tu |
| [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) | Sì, 1809+ (pensato per Win 11) | Inserisce `dxgi.dll` in `C:\Windows`, caricata da explorer.exe | Attivo | GPL-2.0 | Escluso: scrive in `C:\Windows` |
| [OpenGlass](https://github.com/ALTaleX531/OpenGlass) | Sì, 1809–22H2 stabile | Rendering delle cornici in **dwm.exe** (vetro Aero) | Molto attivo (v3.0.2, ott 2026) | GPL-3.0 | Escluso: modifica il compositore grafico |
| [DWMBlurGlass](https://github.com/Maplespe/DWMBlurGlass) | Sì, 2004+ | Iniezione in **dwm.exe**, richiede i simboli | Attivo (2.3.2) | LGPL-3.0 | Escluso: modifica dwm.exe. Attenzione alle build false con malware |
| [ExplorerBlurMica](https://github.com/Maplespe/ExplorerBlurMica) | Sì (blur/acrilico) | DLL COM registrata con `regsvr32` dentro explorer.exe | Attivo (2.1.0) | LGPL-3.0 | Escluso: richiede la registrazione COM di sistema |
| [TranslucentFlyouts](https://github.com/ALTaleX531/TranslucentFlyouts) | Sì | Detours in tutti i processi con interfaccia grafica | **Archiviato** (gen 2025) | LGPL-3.0 | Escluso |
| [Open-Shell](https://github.com/Open-Shell/Open-Shell-Menu) | Sì | Sostituisce il menu Start, aggancia explorer.exe | Attivo (4.4.202) | MIT | Non incluso: lo Start nativo con tema scuro e accento basta. Attenzione alle organizzazioni GitHub false |
| [7+ Taskbar Tweaker](https://github.com/m417z/7-Taskbar-Tweaker) | Sì (22H2 dalla 5.14) | Iniezione in explorer.exe | Attivo | GPL-3.0 | Non incluso: Windhawk copre gli stessi usi |
| [TaskbarX](https://github.com/ChrisAnd1998/TaskbarX) | Sì | **Nessuna iniezione**: API di accessibilità + `SetWindowPos` | Archiviato (giu 2025), v1.7.8 | MIT | **Opzionale, Livello 2**: download verificato con SHA256 |
| [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) | Sì, 1903+ | Nessuna iniezione: `SetWindowCompositionAttribute` | Attivo (2026.2) | GPL-3.0 | **Incluso, Livello 2** |
| [Mica For Everyone](https://github.com/MicaForEveryone/MicaForEveryone) | No (solo Win 11) | Nessuna iniezione | Attivo | MIT | Non applicabile |
| StartAllBack / StartIsBack++ | Solo Win 11 / Win 10 | Closed source, a pagamento | — | Proprietaria | Escluso |

**Il "repo di reverse engineering recente":** non è emerso un unico progetto "di tendenza" verificabile.
I più attivi e discussi nel 2025-2026 sono Windhawk, ExplorerPatcher e OpenGlass.
**OpenGlass** è il progetto nuovo più rilevante che supporta ancora Windows 10: ricostruisce il vetro Aero rielaborando il rendering di `dwm.exe`.
È stato studiato, ma **non viene installato** dal pack, perché si aggancia proprio al processo che la richiesta chiede di non toccare.
Chi lo vuole comunque può installarlo a mano dal repository ufficiale. Il resto del pack funziona anche con OpenGlass attivo.

### Perché `dwm.exe` è una linea rossa

`dwm.exe` (Desktop Window Manager) compone ogni finestra sullo schermo.

- Se una DLL iniettata si blocca dopo un aggiornamento di Windows, lo schermo resta nero o lampeggia finché il DWM non riparte.
- Per funzionare, questi progetti dipendono da offset interni che cambiano a ogni build.

Il pack ottiene trasparenze e colori con API pubbliche: tema `.theme`, `EnableTransparency`, TranslucentTB.

## 2. Mod Windhawk compatibili con Windows 10

Se attivi Windhawk (Livello 3), queste mod funzionano su Windows 10. Installale dalla scheda "Esplora" dell'app Windhawk.

| ID mod (windhawk.net/mods/…) | Cosa fa | Aggancia |
|---|---|---|
| `taskbar-clock-customization` | Orologio della barra con formato personalizzato (es. `HH:mm · ddd d`) | explorer.exe |
| `dark-menus` | Menu Win32 scuri ovunque (build 18362+) | app |
| `custom-menu-height` | Menu più ariosi | app |
| `notifications-placement` | Sposta le notifiche (es. in alto a destra, stile macOS) | explorer.exe |
| `taskbar-hide-running-indicator-w10` | Barra più pulita, stile dock | explorer.exe |
| `keep-rainmeter-always-bottom` | I widget Rainmeter non coprono mai le finestre | Rainmeter |

Da **evitare** su Windows 10 perché solo per Windows 11: `taskbar-icon-size`, `windows-11-taskbar-styler`, `translucent-windows`, `taskbar-labels`, `taskbar-vertical`.
Da **evitare** per la regola su `dwm.exe`: `win10-colored-borders`.

## 3. Formati verificati sul codice sorgente

Tutte le configurazioni del pack sono state controllate sul sorgente delle app, non sulla memoria.

| Componente | Cosa è stato verificato | Fonte |
|---|---|---|
| Lively Wallpaper 2.2 | `Type` 1 = web; flag `--system-information`, `--system-nowplaying`, `--pause-event`, `--audio`; payload di `livelySystemInformation` (CPU/GPU in %, RAM in MB, rete in byte/s); `setwp --file` accetta solo cartelle già nella libreria | `Lively.Models/Enums/WallpaperType.cs`, `Lively/Core/Wallpapers/WebWebView2.cs`, `Lively.Player.WebView2/Form1.cs` |
| Flow Launcher 2.1 | Chiavi obbligatorie del tema XAML, chiavi del blur (`ThemeBlurEnabled`, `DarkBG`), `Settings.json` → `Theme`, `QueryBoxFont` | `Flow.Launcher.Core/Resource/Theme.cs`, `Themes/Base.xaml` |
| TranslucentTB 2026.2 | Percorso `Packages\28017CharlesMilette.TranslucentTB_v826wp6bftszj\RoamingState\settings.json`; chiave `maximized_window_appearance` (con la z); colore `#RRGGBBAA`; ricarica automatica | `settings.schema.json`, `configmanager.cpp` |
| Rainmeter 4.5 | Angoli Roundline in radianti; font caricati da `@Resources\Fonts`; `UpdateRate` in cicli di `Update`; nessuna scalatura DPI automatica (da qui la variabile `Scale`) | rainmeter-docs, `MeterRoundLine.cpp` |
| Nilesoft Shell 1.9 | Grammatica `theme { … }`, `effect = 3` acrilico, `radius` come indice 0–4, ricarica quando cambia la data di `shell.nss` | `dll/src/Parser/Parser.cpp` |
| File .theme | `ColorizationColor` in ARGB, `SystemMode`/`AppMode` (Win 10 1903+), cursori `Pin`/`Person` | learn.microsoft.com + temi di sistema reali |
| Registro accento | `AccentColor`/`AccentColorMenu` in ABGR, `AccentPalette` di 32 byte | strumenti open source che la usano (es. Atlas-OS) |

## 4. Identificatori winget usati

| App | ID winget | Note |
|---|---|---|
| Rainmeter | `Rainmeter.Rainmeter` | installer di sistema (UAC) |
| Lively Wallpaper | `rocksdanister.LivelyWallpaper` | installer di sistema (UAC) |
| TranslucentTB | `CharlesMilette.TranslucentTB` | pacchetto MSIX, stesso dello Store |
| Flow Launcher | `Flow-Launcher.Flow-Launcher` | solo utente |
| Windhawk | `RamenSoftware.Windhawk` | opzionale |
| Nilesoft Shell | `Nilesoft.Shell` | opzionale, MSI di sistema |
| TaskbarX | — | non è su winget: ZIP ufficiale GitHub 1.7.8.0, SHA256 `ab192d20…5a3e2` |

## 5. Test su Windows reale: errori trovati e corretti

Il pack viene installato, verificato e ripristinato su macchine Windows usa e getta (GitHub Actions, Windows Server 2022 e 2025).
Questi test hanno trovato errori che un controllo del codice non avrebbe mostrato.

| # | Problema trovato | Effetto | Correzione |
|---|---|---|---|
| 1 | Un array di un solo elemento restituito da una funzione diventa una stringa, e lo splatting la spezza lettera per lettera | `--disable-interactivity` arrivava a winget come `- - d i s a …`: **nessuna app si installava** (errore 0x8A150002) | Chiamate a winget tramite `Start-Process` con argomenti costruiti in modo esplicito + test di regressione |
| 2 | `switch` srotola `byte[]` in `object[]` | La scrittura di `AccentPalette` falliva e **interrompeva l'applicazione del tema** | Funzione `ConvertTo-StarkRegData` che preserva i tipi + test |
| 3 | `Duration="0:0:90"` non è un TimeSpan valido | L'installer grafico non si apriva | `0:1:30` |
| 4 | `New-Object` avvolge l'oggetto in un PSObject che WPF rifiuta come FontFamily | L'installer grafico non si apriva | `[FontFamily]::new(...)` |
| 5 | In PowerShell `$title` e `$Title` sono la stessa variabile | L'installer grafico non si apriva | Variabile rinominata + controllo automatico su tutto il codice |
| 6 | Un file `.theme` aperto con doppio clic simulato viene applicato **in modo asincrono** | Windows lo applicava dopo il ripristino, rimettendo cursori, suoni e sfondo Stark | Tema applicato direttamente (registro + API), in modo sincrono |
| 7 | `AddFontResource` conta i caricamenti; la cache dei font tiene i file aperti | I font non si cancellavano | `RemoveFontResource` ripetuto, nuovi tentativi, cancellazione al prossimo accesso come ultima risorsa |
| 8 | Il disinstallatore di Lively attende una conferma invisibile se l'app è aperta | **Il ripristino si bloccava per sempre** | App chiuse prima della disinstallazione + timeout su ogni chiamata esterna |
| 9 | Flow Launcher tiene aperto il proprio file tema | Seconda installazione in errore | Flow chiuso prima di aggiornarlo |
| 10 | Esplora risorse salva lo stato "icone del desktop" per conto suo | Le icone restavano nascoste dopo il ripristino | Stesso comando di "Visualizza > Mostra icone del desktop" |
| 11 | L'API della schermata di blocco spegne Windows Spotlight da sola | Spotlight non tornava | Valori salvati prima della chiamata; immagine ripristinata prima del registro |
| 12 | Windows sorveglia la chiave Accent e riallinea il colore delle finestre (DWM): verso il nuovo colore lo fa subito, al ritorno solo dopo `ImmersiveColorSet` e solo se vede una discordanza | Il ripristino riscriveva i valori DWM, Windows "non vedeva nulla da fare" e DWM rimetteva il ciano | Valori DWM salvati solo per verifica, mai riscritti; avviso `ImmersiveColorSet` e attesa del riallineamento |
| 13 | Il backup del colore DWM veniva letto subito dopo aver scritto l'accento, ma Windows riallinea DWM in pochi istanti | A volte il backup conteneva già il ciano: il ripristino credeva DWM a posto (o aspettava 50 s un colore già giusto) | Backup letto prima di qualsiasi modifica dell'accento |
| 14 | Il servizio di sistema "Cache dei font di Windows" (FontCache) tiene aperti i font dell'utente anche dopo `RemoveFontResource` (individuato con Gestione riavvio di Windows) | Il ripristino aspettava 10 s per ogni font e poi rimandava la cancellazione | Il servizio non si tocca: font disinstallati subito, file cancellati al prossimo accesso, un solo messaggio chiaro. Una reinstallazione prima del riavvio annulla la cancellazione rimandata |
| 15 | Un exe senza console (app grafica) non può impostare `Console.InputEncoding` | Il server MCP di Tipaldi AI si chiudeva subito con "handle non valido": Claude Desktop non l'avrebbe mai visto | Lettura di stdin direttamente in UTF-8, senza toccare la console; test che avvia il server MCP come Claude Desktop |
| 16 | PowerShell passa `$null` come stringa vuota `""` ai parametri stringa delle API di Windows | `FindWindow('Progman', $null)` cercava una finestra col titolo vuoto: il desktop non veniva **mai** trovato e "Desktop pulito" falliva sempre con "desktop non trovato" | `[NullString]::Value` per i nomi di finestra + test che vieta lo schema |
| 17 | Quando mostra o nasconde le icone, Esplora risorse riscrive nel registro le sue impostazioni di Visualizza | "Mostra estensioni dei file", applicata subito prima, tornava come prima | Icone del desktop applicate per prime, poi le altre ottimizzazioni |
| 18 | Il suono di avvio partiva con `powershell.exe` dall'Esecuzione automatica | A ogni accesso compariva per un attimo PowerShell nella barra delle applicazioni | Suonato da `TipaldiAI.exe --play` (app grafica senza finestre) + test: niente in Esecuzione automatica punta a PowerShell |
| 19 | La finestra dell'installer apparteneva a `powershell.exe` | Windows la mostrava con icona e nome di PowerShell | `TipaldiAI-Setup.exe` ospita il motore di Windows PowerShell 5.1 al suo interno: la finestra è sua. Da `Installa.cmd` l'installer imposta un proprio AppUserModelID e la propria icona |

## 6. Il desktop di Esplora risorse, studiato dal vivo

Il desktop Tipaldi AI deve essere lo **sfondo** di Windows, non una finestra appoggiata sopra.
La struttura del desktop non è documentata da Microsoft. Per questo è stata misurata sulla shell vera dei runner (Esplora risorse avviato come shell nella sessione interattiva) con `tests/Test-StarkShell.ps1`: gerarchia delle finestre prima e dopo, catture reali dello schermo.

**Struttura misurata.**

```
Windows 10 (build 20348)                         Windows 11 24H2 (build 26100)
WorkerW                                          Progman
  SHELLDLL_DefView      menu, trascinamento        SHELLDLL_DefView
    SysListView32       le icone                     SysListView32
WorkerW                 livello sfondo  <- HUD     WorkerW            livello sfondo <- HUD
Progman
```

**Il messaggio `0x052C` a Progman.** Fa creare a Esplora risorse il livello sfondo, lo stesso che usa per l'animazione del cambio di sfondo. Ogni variante è stata provata su un Esplora risorse appena riavviato, con gli effetti visivi spenti:

| wParam, lParam | Windows 10 (20348) | Windows 11 24H2 | Effetti collaterali |
|---|---|---|---|
| 0, 0 (la forma che si trova in giro) | nessun livello | nessun livello | nessuno |
| 0xD, 0 | nessun livello | nessun livello | nessuno |
| **0xD, 1** | **livello creato** dietro alle icone | **livello creato** come figlio di Progman, sotto le icone | nessuno: "Mostra icone" e icone visibili invariati |

Tipaldi AI manda `(0xD, 1)` una sola volta per ogni avvio di Esplora risorse. Usa `(0, 0)` come ripiego per le build più vecchie e non manda nulla se il desktop non esiste ancora.

**Cosa ne deriva (verificato con catture reali):**

- Il HUD è una finestra figlia del livello sfondo. Non compare nella barra delle applicazioni né in Alt+Tab, resta con Win+D e non sta mai sopra le finestre.
- Icone, menu col tasto destro e trascinamento restano quelli di Windows, sopra al HUD.
- Il HUD lascia libere le colonne delle icone vere. Le conta con `LVM_GETITEMCOUNT` e `LVM_GETITEMSPACING`, messaggi senza puntatori: nessuna memoria scritta dentro explorer.exe.
- I clic sul desktop arrivano sempre a Esplora risorse. Un hook del mouse di sola lettura li inoltra ai pulsanti del HUD, ma solo se sotto il cursore c'è il desktop e non un'icona (controllo con UI Automation) né un'altra finestra.
- Esplora risorse riavviato: il livello sfondo sparisce e la finestra del HUD con lui. Tipaldi AI se ne accorge (handle della finestra e avviso `TaskbarCreated`), la ricrea e si riaggancia in meno di un secondo.
- App e file aperti dal HUD partono tramite la vista del desktop (`IShellFolderViewDual.ShellExecute`), come un doppio clic su un'icona. Così vanno in primo piano.
- Nessuna DLL dentro explorer.exe: solo messaggi di finestra, `SetParent` e API pubbliche. In `logs\desktop.log` restano agganci, riavvii e clic, per la diagnosi.

Il punto 12 è stato chiarito con un esperimento dedicato su una macchina pulita. L'accento veniva cambiato uno stimolo alla volta (registro, avvisi di sistema, sfondo, schermata di blocco, riavvio di Esplora risorse), misurando dopo ognuno il colore che DWM aveva in memoria.
Lo stesso esperimento ha mostrato che `SetUserColorPreference` non è esportata da `uxtheme.dll` sulle versioni recenti. Le soluzioni che girano online e la usano quindi non funzionano.

**Limite noto (base Windows 11).** Sul runner Windows Server 2025, che usa la base di Windows 11, Windows a volte non riallinea da solo il colore di bordi e barre del titolo dopo il ripristino, anche ripetendo l'avviso `ImmersiveColorSet`. Sul runner Windows Server 2022, che usa la base di Windows 10 (il sistema per cui è pensato il pack), il colore torna sempre.
Quando il colore non torna, il ripristino lo dice e indica come sistemarlo: Impostazioni > Personalizzazione > Colori, riscegliendo il proprio colore. Il colore resta visibile solo se l'opzione "barre del titolo colorate" era attiva.
