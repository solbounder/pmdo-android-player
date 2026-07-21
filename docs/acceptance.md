# Android-v1-Abnahme

Stand: 20. Juli 2026. Ein Haken bedeutet automatisiert oder im Emulator
nachgewiesen; offene Punkte brauchen einen echten AYN Thor.

## Automatisiert und Emulator

- [x] Gepinnter PMDO-0.8.12-Graph lässt sich wiederholt neu auschecken, patchen
  und verifizieren.
- [x] Locked Restore und 24 Import-/Backup-/Sicherheits-/Viewporttests bestehen.
- [x] Runtime-Import prüft 11.485 Dateien (ca. 536 MB) nach Größe und SHA-256
  und aktiviert erst nach vollständigem Erfolg.
- [x] Lua-5.4-Smoke-Test besteht auf Android mit Arithmetik, CLR-Bridge,
  Delegate und Coroutine.
- [x] Unverändertes *Echoes of the Abyss* lässt sich per Android-Dateiauswahl
  importieren, als Quest aktivieren und bis in die erste Storyszene starten.
- [x] Gestrecktes Vollbild sowie 1× bis 4× und `Max 4:3`, Touch-A, Touch-D-Pad,
  Android-Tastatur und Zwischenablage-Dialog funktionieren im Landscape-
  Emulator. Die Bildschirmgröße lässt sich im Optionsmenü live umschalten.
- [x] Eine physische Controller-Taste blendet das Touch-Overlay vollständig aus;
  der nächste Touchscreen-Tipp aktiviert es wieder. Das Tastatursymbol liegt
  kompakt mittig im oberen schwarzen Rand.
- [x] Der Android-Hinweisbildschirm wird nach dem Hintergrundladen automatisch
  fortgesetzt; der Lauf erreicht ohne Eingabe den EOTA-Titel und `Ready`.
- [x] Die Android-Engine stellt EOTAs Runtime-Vertrag
  `GAME:GetEotaRuntimeContract() == 1` bereit; der unveränderte Quest-Start
  erreicht damit Titel und Hauptmenü.
- [x] Eine fehlgeschlagene Hintergrundinitialisierung bleibt in der Fehlerphase
  und kann nicht mehr in einen uninitialisierten `Ready`-Spielzustand mit
  kaskadierenden Update-/Draw-Fehlern wechseln.
- [x] Das Spielfeld liegt in einem per `Gravity.Center` zentrierten Host ohne
  feste Surface-Puffergröße; die Skalierung nutzt die Metrik des Activity-
  Fensters statt der möglicherweise falschen Application-Displaymetrik.
- [x] Engine-Fehler bleiben trotz PMDO-Handlerwechsel beobachtbar und erscheinen
  als kopierbarer Dialog mit Log- und Layoutauszug.
- [x] Nach der Render-Thread-Korrektur entsteht beim Start eines neuen Spiels
  kein neuer ANR; der geprüfte Lauf enthält keine Engine-Fehler.
- [x] Alle Eingabepfade einschließlich Menüs werden während des Ground-Map-
  Handoffs verworfen, solange noch kein aktiver Charakter installiert ist; der
  vorhandene EOTA-Spielstand lädt im x86_64-Emulator ohne
  `GroundScene.ProcessInput`-Nullzugriff in die Szene.
- [x] Beendete Android-Soundinstanzen werden auch dann freigegeben, wenn Android
  ihren Zustand nach einem leeren Puffer noch nicht als gestoppt meldet. Bei
  24 gleichzeitigen One-Shots wird nur der älteste kurze Effekt ersetzt; BGM
  und Loop-Sounds bleiben erhalten. 121 schnelle Menü-/Richtungseingaben
  erzeugen keine `InstancePlayLimitException`, Quellenwarnung oder rekursive
  Engine-Fehlermeldung.
- [x] BGM erhält beim Szenenwechsel Vorrang vor kurzlebigen Soundeffekten; drei
  Streaming-Puffer werden laufend nachgefüllt und ein erkannter Underrun wird
  automatisch fortgesetzt. `Title.ogg` und `Base Town.ogg` starten im geprüften
  EOTA-Lauf ohne Audio- oder Quellenlimitfehler.
- [x] Die 18 RogueEssence-Patches werden reproduzierbar bis Head
  `7b4ed6ab26a5c27bfd659439c8c24720563f60a6` angewendet. Der ARM64-Release-Build
  kompiliert darin L/R/ZL/ZR-Mehrfachauswahl und alle vier Links/Rechts-
  Verkaufskombinationen; unverkäufliche Einträge bleiben deaktiviert.
- [x] Der Fanfaren-Fade begrenzt Frame-Overshoot vor der Bruchrechnung. Sämtliche
  Android-Audio-Volume-Schreibzugriffe werden zusätzlich auf einen endlichen
  Wert zwischen 0 und 1 normalisiert; der ausführbare NaN-/Infinity-/Grenzwerttest
  und der ARM64-Release-Build bestehen.
- [x] Ein `versionCode`-5-APK mit fest geprüftem Update-Zertifikat installiert
  sich im Emulator per In-place-Update; Erstinstallationszeit,
  importierte Runtime, EOTA und Spielstand bleiben erhalten.
- [x] Das finale ARM64-APK wird auf genau eine ABI, fehlende
  `INTERNET`-Berechtigung, deaktiviertes Android-Backup und
  16-KiB-Page-Alignment sowie das etablierte Signaturzertifikat geprüft.
- [x] SAF-Traversierung und Dateiimporte laufen außerhalb des UI-Threads; der
  Launcher sperrt während eines laufenden Imports alle konkurrierenden Aktionen.

## Physischer AYN-Thor-Test

Diese Punkte können ein Desktop-Build oder x86_64-Emulator nicht zertifizieren:

1. [ ] APK auf Thor/Thor Lite installieren, PMDO importieren und Titel, Ground
   sowie Dungeon erreichen.
2. [ ] Titel, Menü, Ground und Dungeon visuell mit Desktop-Referenzen vergleichen.
3. [ ] EOTA sowie je einen Lua-, JSONPatch-, Bild- und Ogg-Override-Mod
   unverändert laden.
4. [ ] Eine Stunde Loops, Crossfades, Fanfaren und Soundeffekte ohne Knackser,
   Drift oder Abbruch ausführen.
5. [ ] Alle Thor-Tasten, Trigger, Hot-Plug, Multi-Touch und Android-IME prüfen;
   eine Controller-Eingabe muss das Overlay ausblenden und der nächste
   Touchscreen-Tipp muss es wieder einblenden.
6. [ ] Im Verkaufsmenü mit L, R, ZL und ZR einzeln markieren. L+R, L+ZR,
   ZL+R und ZL+ZR müssen seitenübergreifend alle verkaufbaren Gegenstände
   auswählen; unverkäufliche Schlüsselgegenstände bleiben deaktiviert.
7. [ ] In Faultline Ridge oder Copper Quarry mit einem normalen, nicht
   fliegenden Wasser-/Geist-Pokémon den Fossil-Geheimraum betreten, die
   Rücktreppe erreichen und erneut betreten. Danach im Geheimraum **Aufgeben**:
   der Lauf muss sichtbar im Hub enden und das Menü darf nicht auf einem
   schwarzen, weiter steuerbaren Bildschirm übrig bleiben.
8. [ ] Mehrfach Fanfaren bei gleichzeitigem Szenen-/BGM-Wechsel auslösen; kein
   `ArgumentOutOfRangeException`, schwarzer Bildschirm oder Audioabbruch.
9. [ ] 50 Home/Resume- und Screen-off/on-Zyklen, Surface-Neuanlage und
   Force-Stop in Ground und Dungeon ohne Save-Korruption durchführen.
10. [ ] Auf Thor Lite (Snapdragon 865) 30 Minuten repräsentatives Ground- und
   Dungeon-Spiel bei Ziel 60 FPS: kein anhaltender Abfall unter 55 FPS, keine
   Audio-Underruns und kein monoton wachsender Speicherverbrauch.

Pro Lauf sind Gerätemodell, Firmware, APK-SHA-256, Runtime-Manifest-SHA-256,
aktive Mods, Logs, Screenshots und gemessene Framewerte zu dokumentieren.
