# Android-v1-Abnahme

Stand: 23. Juli 2026. Ein Haken bedeutet automatisiert oder im Emulator
nachgewiesen; offene Punkte brauchen einen echten AYN Thor.

## Automatisiert und Emulator

- [x] Das finale signierte v0.1.10-APK (`versionCode` 11) startet sowohl nach
  einer sauberen Installation als auch als In-place-Update über v0.1.9. Der
  Test installiert ausdrücklich das fertige Release-APK ohne Fast Deployment
  und prüft, dass der App-Prozess ohne `UnsatisfiedLinkError` aktiv bleibt.
- [x] Das signierte v0.1.9-APK (`versionCode` 10) wurde als In-place-Update
  installiert. Importierte Runtime, EOTA und Zusatzmods blieben erhalten.
- [x] Alle vom Android-Player selbst erzeugten Launcher-, Status-, Dialog-,
  Fehlerbericht-, Tastatur- und Touch-Editor-Texte sind Englisch. Ein
  Quelltexttest verhindert neue deutsche UI-Literale außerhalb der gezielten
  Übersetzung alter gespeicherter Fehlerberichte; Launcher, Mod-Auswahl,
  Save-Import und Texteingabe wurden zusätzlich im Emulator geprüft.
- [x] Das Öffnen einer Musikliste erzeugt keine Audioquelle mehr pro
  aufgelistetem Titel. OGG-Metadaten werden ohne dauerhafte Wiedergabeinstanz
  gelesen; Reader, Puffer und Android-Audioquelle entstehen erst bei `Play`
  oder `PlayAt` und werden nach Fehlern sowie bei `Dispose` vollständig
  freigegeben. Der Lifecycle-Regressionscheck, ARM64-Build und ein
  EOTA-Emulatorlauf mit Titel- und Ground-BGM bestehen ohne
  `InstancePlayLimitException` oder Audiofehler.
- [x] Save-Zugriffsfehler erkennen verweigerte Schreibrechte und weisen auf
  den Launcher-Import von `SAVE.rssv` beziehungsweise die Wiederherstellung
  korrekter Android-Dateieigentümerschaft hin. Das deckt insbesondere Saves
  ab, die außerhalb des Players manuell mit Root-Rechten kopiert wurden.
- [x] Gepinnter PMDO-0.8.12-Graph lässt sich wiederholt neu auschecken, patchen
  und verifizieren.
- [x] Locked Restore sowie Import-/Backup-/Sicherheits-/Viewporttests bestehen.
- [x] Android-Backup (`.zip`) und einzelne `SAVE.rssv`-Dateien sind getrennte
  Importwege. RSSV-Import validiert PMDO 0.8.12, erlaubt Basisspiel oder
  installierte Quest und sichert den vorherigen Save vor der Aktivierung.
- [x] Mod-Anforderungen bis 0.8.12 werden ohne Änderung der `Mod.xml`
  akzeptiert. Quest und Zusatzmods lassen sich einzeln verwalten; neuere oder
  native Desktop-Mods werden nicht automatisch aktiviert.
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
- [x] Die 19 RogueEssence-Patches werden reproduzierbar bis Head
  `b0d84192d099bd3d38eef7deec94e51c89e42c42` angewendet. Der ARM64-Release-Build
  kompiliert darin L/R/ZL/ZR-Mehrfachauswahl und alle vier Links/Rechts-
  Verkaufskombinationen; unverkäufliche Einträge bleiben deaktiviert.
- [x] Der unveränderte Zusatzmod `Enable Mission Board` lädt im x86_64-Emulator
  sowohl mit EOTA als auch mit dem PMDO-Basisspiel. Im Basisspiel wurden ein
  frischer Spielstand und der gemeinsame Stack aus `All Starters`,
  `Any Starting Team`, `Enable Mission Board`, `Gender Unlock`, `Mega Stones`,
  `Music Notice` und `Visible Monster Houses` geprüft. Starter- und
  Geschlechtsauswahl funktionieren; die Y-Taste öffnet das Ground-Menü mit
  `Job List` ohne `Color`-/`mission_menu_tools`-Fehler oder Runtime-Absturz.
  EOTAs native Anpassung von `Friend Areas` war im EOTA-Lauf enthalten. Nur
  die ursprüngliche Standalone-Version 1.1 war in der Basisspiel-Testinstallation
  nicht vorhanden und ist deshalb nicht Teil dieses Basisspiel-Nachweises.
- [x] Der Fanfaren-Fade begrenzt Frame-Overshoot vor der Bruchrechnung. Sämtliche
  Android-Audio-Volume-Schreibzugriffe werden zusätzlich auf einen endlichen
  Wert zwischen 0 und 1 normalisiert; der ausführbare NaN-/Infinity-/Grenzwerttest
  und der ARM64-Release-Build bestehen.
- [x] Ein `versionCode`-8-APK mit fest geprüftem Update-Zertifikat installiert
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
   unverändert laden; Quest und Zusatzmods einzeln verwalten.
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
11. [ ] Einzelnen PMDO-0.8.12-`SAVE.rssv` in Basisspiel und installierte Quest
    importieren, Laden prüfen und den erhaltenen `SAVE.rssv.bak` kontrollieren.

Pro Lauf sind Gerätemodell, Firmware, APK-SHA-256, Runtime-Manifest-SHA-256,
aktive Mods, Logs, Screenshots und gemessene Framewerte zu dokumentieren.
