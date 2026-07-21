# Architektur der Android-v1

## Paketgrenze

Das APK ist ein reiner Player und enthält nur verwalteten Port-Code, MonoGame,
Lua 5.4 und den Hash-Katalog der erwarteten PMDO-0.8.12-Dateien. Runtime,
Quest-Mods und Saves werden niemals in das APK aufgenommen. Der Launcher läuft
im Hauptprozess; die PMDO-Engine läuft in `:game`. Android darf einen beendeten
Game-Prozess als leeren Cache behalten. Vor jedem Start beendet der Launcher
diesen eigenen Cache-Prozess gezielt, damit die vielen statischen PMDO-
Singletons garantiert frisch entstehen.

## Quellgraph

- `PMDO.Android` kompiliert die gepinnten PMDC-Quellen unter dem ursprünglichen
  Assemblynamen `PMDC` und enthält Activity, Launcher und Android-Brücken.
- `RogueEssence.Android` kompiliert RogueEssence unter dem ursprünglichen
  Assemblynamen und ersetzt nur Desktop-Plattformränder wie Audio, Eingabe und
  Versionsabfrage.
- `RogueElements.Android`, `NLua.Android` und `KeraLua.Android` erhalten ihre
  ursprünglichen Assemblynamen. Damit funktionieren bestehende Lua-CLR-
  Typnamen ohne Änderung.
- `PMDO.Portable` enthält die plattformunabhängigen, getesteten Import- und
  Backup-Transaktionen.

Der komplette Upstream-Graph ist in `upstream.lock.json` festgeschrieben.
`tools/Get-Upstream.ps1` erzeugt daraus einen Detached Checkout und wendet die
nummerierten Patches mit reproduzierbaren Commit-Zeiten an. Das Originalprojekt
ist keine Build-Abhängigkeit.

## Mod-Kompatibilität

Ein Import kopiert alle Dateien bytegleich in die normale PMDO-Struktur
`MODS/<Name>` und ergänzt ausschließlich `CONFIG/ModConfig.xml`, um den Mod zu
aktivieren. `<Header>`- und ältere `<Mod>`-Metadaten werden akzeptiert. FNA-
Serialisierungsformen für `Color` und `Vector2` werden beim Lesen durch schmale
MonoGame-Konverter verstanden; Moddateien werden nicht migriert oder neu
gespeichert. Android-Lua sucht native Module in `Data/Script/bin/android`.

Reine Lua-/Daten-/Asset-Mods benötigen daher normalerweise keine Anpassung.
Binäre Desktop-Module sind ABI- und betriebssystemspezifisch und können dieses
Versprechen prinzipbedingt nicht erfüllen. Sie werden erkannt und gemeldet.

## Speicher und Sicherheit

Der Storage Access Framework liefert ausschließlich Streams; die Engine erhält
keinen beliebigen Dateisystemzugriff außerhalb ihres privaten App-Verzeichnisses.
Runtime-Dateien werden gegen Größe und SHA-256 geprüft und erst nach komplettem
Erfolg atomar aktiviert. Mod-ZIPs lehnen Pfad-Traversal und kollidierende Namen
ab. Save-Importe werden ebenfalls gehasht und bei Fehlern zurückgerollt.

Die App fordert keine Netzwerkberechtigung an. Das reduziert die Angriffsfläche,
macht aber Netzwerkmods bewusst inkompatibel. Importierter Lua-Code bleibt
vertrauenswürdiger In-Process-Code und ist keine Sandbox.

## Anzeige und Eingabe

PMDO rendert intern 320×240. Auf Android ist `Max 4:3` der Standard: Der größte
passende ganzzahlige Renderpuffer wird unverzerrt auf die maximal verfügbare
4:3-Fläche skaliert und zentriert. Im Optionsmenü stehen zusätzlich gestrecktes
Vollbild sowie 1× bis 4× zur Wahl; die Änderung wird ohne Neustart angewendet.
Die Touchschicht liegt darüber und speist dieselben
virtuellen GamePad-Tasten wie ein physischer Controller. Ein zweiter Bildschirm
und das untere Thor-Display gehören nicht zum v1-Scope.
