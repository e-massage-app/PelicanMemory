# Pelican Memory

Mod SMAPI pour Stardew Valley 1.6+ (testé avec la 1.6.15 et SMAPI 4.5.2).
C'est un **outil de mémoire, pas un guide** : il n'affiche que ce que le joueur a déjà découvert dans **sa** sauvegarde.

## Features (v1)

| ID | Ce que ça fait | Filtre anti-spoil |
|---|---|---|
| `visited-map-labels` | Nomme les **zones** de la carte du monde (Montagnes, Pélican Ville, Voie ferrée…) | Uniquement les zones où le joueur est allé (`Farmer.locationsVisited`) |
| `caught-fish-tooltip` | Ajoute eau / saisons / météo / horaires à l'infobulle d'un poisson | Uniquement les poissons attrapés (`Farmer.fishCaught`). Saisons et type d'eau calculés **seulement à partir des lieux visités** |
| `minimap` | Minimap du lieu courant en haut à gauche, zoom avec Page préc. / Page suiv. | Seulement le lieu où le joueur se trouve, et seulement les villageois déjà rencontrés (`friendshipData`) |
| `community-center-hints` | Signale sur l'objet qu'un lot du Centre communautaire l'attend, ou qu'il a déjà été donné | Seulement les salles dont le panneau est visible en jeu (même règle que l'indice vanilla), rien sur la voie Joja |
| `purchase-confirm` | Demande la quantité avant tout achat en boutique (fenêtre vanilla, défaut 1, total affiché, Annuler) | Garde-fou : ni mémoire ni assistance, aucune donnée de jeu révélée |
| `social-locations` | Position de chaque villageois dans l'onglet Relations | Le bâtiment n'est nommé que s'il a été visité ; sinon la zone (« Montagnes ») si elle est connue ; sinon « Lieu inconnu » |

Les features s'activent et se désactivent dans l'onglet « livre bleu » du menu Échap. Le changement est immédiat et enregistré dans `config.json`.

## Build et lancement

Prérequis (déjà installés sur le PC de Jordan) : SDK .NET 8, SMAPI 4.5.2.

```bash
dotnet build
```

Le build compile le mod, puis le **copie automatiquement** dans `Stardew Valley\Mods\PelicanMemory` (NuGet `Pathoschild.Stardew.ModBuildConfig`). Il produit aussi un zip prêt à distribuer dans `bin\Debug\net6.0\`.

**Lancer le jeu avec SMAPI** (au choix) :
- directement : `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\StardewModdingAPI.exe` ;
- via Steam : Stardew Valley → Propriétés → Options de lancement :
  `"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\StardewModdingAPI.exe" %command%`

La console SMAPI doit afficher `Pelican Memory 1.0.0 by Jordan Neau`. En cas de souci, le journal complet se trouve dans `%APPDATA%\StardewModdingAPI\ErrorLogs\SMAPI-latest.txt`.

## Installeur pour un autre PC

`installer/` contient un programme d'installation en français, pensé pour quelqu'un qui ne bricole pas son PC : un seul `.exe`, trois écrans, un bouton « Suivant ».

Ce qu'il fait tout seul : il trouve le dossier du jeu (registre Steam, fichier de bibliothèques Steam, emplacements habituels, sinon « Parcourir… »), installe **SMAPI** s'il manque en le téléchargeant depuis les releases officielles, installe la dernière version du mod, puis affiche la ligne d'options de lancement Steam avec un bouton « Copier ».

Le même exe sert de **mise à jour** : relancé plus tard, il réinstalle la dernière version du mod et **conserve `config.json`** (les réglages du joueur).

### Publier une nouvelle version
```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```
Le script lit la version dans `manifest.json`, compile le mod, met à jour `dist/PelicanMemory.zip` et `dist/version.txt`, puis reconstruit l'exe dans `publish/`. Ensuite `git add -A`, `git commit`, `git push` : l'installeur des autres PC prendra la nouvelle version automatiquement.

L'installeur lit `dist/` via `raw.githubusercontent.com`, donc **un simple push suffit pour livrer** — pas de release GitHub à créer, pas de jeton, pas d'outil en plus. Le dépôt doit être public. Si le dépôt change de nom ou de compte, les deux constantes sont en haut de `installer/Program.cs`.

À savoir : l'exe n'est pas signé, donc au premier lancement Windows affiche « Windows a protégé votre ordinateur ». Il faut cliquer sur **Informations complémentaires**, puis **Exécuter quand même**.

## Idées discutées et écartées (2026-09-18)

- **Étages des minerais dans la mine** : écartée. Le jeu ne stocke pas ces étages dans ses fichiers de données, c'est sa logique de génération qui les décide ; les afficher serait recopier un guide. La seule version acceptable (le mod note les trouvailles du joueur, sans rétroactif) n'apporte rien à court terme.
- **Objets demandés par une quête** : écartée, les quêtes demandent de refarmer l'objet, pas juste de le donner.
- **Recettes connues utilisant un ingrédient** : écartée, l'infobulle deviendrait interminable en fin de partie.
- **Animaux pas encore caressés** : reportée, à reproposer éventuellement pour la compagne de Jordan.
- **Objets jamais expédiés** : reportée, utile seulement pour la chasse aux succès.

## Multijoueur — procédure pour l'autre joueur

Le mod est **100 % côté client** : il ne lit que la progression du joueur local et n'envoie rien sur le réseau. Chacun l'installe ou non, indépendamment, et voit sa propre progression filtrée. Rien à faire côté hôte.

Pour l'installer chez l'autre joueur :
1. Installer SMAPI depuis <https://smapi.io> (même version majeure que toi, 4.x), en suivant l'installeur fourni.
2. Récupérer le zip `PelicanMemory 1.0.0.zip` (produit par `dotnet build` dans `bin\Debug
et6.0\`) et le décompresser dans `Stardew Valley\Mods\`. Il doit obtenir `Mods\PelicanMemory\PelicanMemory.dll`.
3. Lancer le jeu par `StardewModdingAPI.exe` (ou par les options de lancement Steam).
4. Ses réglages sont les siens : `Mods\PelicanMemory\config.json` est local à son PC.

À savoir : les deux joueurs peuvent avoir des versions différentes du mod, ou un seul des deux peut l'avoir — aucune vérification de compatibilité n'est faite entre les machines, puisque rien ne transite.

## Architecture

```
ModEntry.cs                  crée la config, le registre, enregistre les features, branche l'onglet
Core/
  IFeature.cs                contrat : Id, EnabledByDefault, IsActive, Enable(), Disable()
  FeatureBase.cs             état actif + patchs Harmony retirés automatiquement à la désactivation
  FeatureRegistry.cs         lit/écrit config.json, active/désactive, traductions
  ModConfig.cs               { "Features": { "<id>": true|false }, "Numbers": { "minimap.opacity": 100 } }
  ModSettings.cs             lecture/écriture de config.json, sauvegarde à chaque changement
  PlayerStore.cs             données par joueur et par sauvegarde (Farmer.modData), pour les futures features
  WorldMapLookup.cs          lien entre les données Data/WorldMap et les lieux réellement visités
UI/
  GameMenuTab.cs             injection de l'onglet dans le GameMenu vanilla (4 patchs Harmony)
  ModOptionsPage.cs          la page : liste scrollable, reprise du comportement de OptionsPage
  FeatureCheckbox.cs         OptionsCheckbox vanilla reliée à un callback
  FeatureSlider.cs           OptionsSlider vanilla (0-100) reliée à un callback
Features/
  VisitedMapLabels/          feature 1
  CaughtFishTooltip/         feature 2
  Minimap/                   feature 3 (MinimapRenderer = rendu de la carte, MinimapGeometry = maths testables)
  SocialLocations/           feature 4
  CommunityCenterHints/      feature 5
  MuseumHints/               feature 6
  FarmLayers/                feature 7
  AnimalCare/                feature 8
  PurchaseConfirm/           feature 9
i18n/default.json, fr.json
```

### Ajouter une feature
1. Créer `Features/MaFeature/MaFeatureFeature.cs` qui hérite de `FeatureBase`, avec un `Id` unique.
2. Dans `OnEnable()`, s'abonner aux événements SMAPI (`+=`) et poser les patchs via `this.Prefix(...)` / `this.Postfix(...)`.
   Dans `OnDisable()`, se désabonner (`-=`). Les patchs sont retirés automatiquement, et seulement les nôtres.
3. Ajouter `feature.<id>.name` et `feature.<id>.description` dans `i18n/default.json` et `i18n/fr.json`.
   Pour un réglage propre à la feature (curseur…), surcharger `CreateOptionRows()` et stocker la valeur via `this.Settings`.
4. Ajouter une ligne `registry.Add(new MaFeatureFeature(helper, this.Monitor, harmony));` dans `ModEntry`.

La ligne de l'onglet apparaît toute seule.
**Règle anti-spoil** : filtrer toute donnée par la progression réelle du joueur local (`Game1.player`).

### Choix techniques (vérifiés dans le code décompilé du jeu 1.6.15)
- **Persistance** : `helper.Data.ReadSaveData`/`WriteSaveData` sont réservés à l'hôte en multijoueur.
  `PlayerStore` utilise donc `Farmer.modData`, sauvegardé et synchronisé par le jeu de base. Ça marche pour l'hôte comme pour un invité, sans que l'autre joueur ait le mod.
- **Lieux visités** : le jeu les enregistre déjà depuis la 1.6 (`Game1.OnLocationChanged` → `player.locationsVisited`), y compris rétroactivement pour les anciennes sauvegardes.
  La feature 1 n'a donc pas de suivi à elle, c'est plus fiable.
- **Noms de zones** : une étiquette par zone de `Data/WorldMap`, pas par bâtiment — les bâtiments sont déjà reconnaissables sur le dessin et au survol, alors que les quêtes et événements de pêche citent des zones. Le nom vient du texte de parchemin de la zone, sinon de son infobulle générale (« Voie ferrée », « Désert de Calico »), sinon des traductions du mod (les zones de l'île Gingembre, que le jeu ne nomme pas). Une zone est considérée visitée si le joueur est allé dans l'un de ses lieux ; les zones sans lieu nommé (l'île vue depuis la vallée) sont déjà conditionnées par le jeu lui-même.
- **Onglet** : le `GameMenu` code ses onglets en dur. Patchs :
  - postfix du constructeur, pour que `new GameMenu(notreOnglet)`, utilisé au redimensionnement de la fenêtre, fonctionne ;
  - postfix de `getTabNumberFromName` ;
  - dessin de l'icône juste après le cadre du menu, pour le même empilement que les onglets vanilla.
- **Poissons** :
  - depuis la 1.6, `Data/Fish` ne sert plus que pour les horaires et la météo ; les saisons et les lieux viennent de `Data/Locations` ;
  - les poissons propres à certains niveaux de la mine (codés en dur dans `MineShaft.getFish`) sont ajoutés à la main ;
  - les méduses, qui n'ont pas de fiche `Data/Fish`, n'affichent que l'eau et les saisons.
- **Minimap** : la carte du lieu est rendue une fois dans une texture (16 px par tuile, contre 64 en jeu), lors de l'événement `Display.Rendering`, quand aucun lot de dessin n'est en cours. Chaque image ne fait plus que dessiner un extrait zoomé. Les très grandes cartes ne sont rendues qu'autour du joueur, et re-rendues quand il approche du bord. Les calculs de coordonnées sont isolés dans `MinimapGeometry` pour être testables sans lancer le jeu.
- **Onglet Relations** : le texte est écrit sous les cœurs, dans la zone libre de la colonne ; la ligne descend quand le personnage a plus de 10 cœurs (deux rangées).

## Vérifications faites
- Build : 0 erreur, 0 avertissement (analyseurs SMAPI compris).
- Les 7 patchs Harmony ont été appliqués sur la vraie `Stardew Valley.dll` depuis un programme de test (Harmony vérifie les noms de paramètres) : tous OK. Le retrait d'un patch n'enlève que le nôtre.
- `FishInfoResolver` a été exécuté sur les vraies données du jeu et sur la sauvegarde `Elan` (37 poissons) : résultats conformes au wiki. Le poisson-globe n'affiche que l'été tant que l'île n'est pas visitée.
- La découverte des lieux a été simulée sur la même sauvegarde : seuls les lieux réellement visités sont affichés (bug `Mine`/`Mines` trouvé et corrigé).
- Testé en jeu le 2026-09-17 par Jordan : infobulles de pêche validées ; minimap et noms de carte corrigés ensuite (cadre qui masquait la minimap, passage des bâtiments aux zones).
- Géométrie de la minimap : 25 assertions passées hors jeu (joueur toujours centré aux 4 niveaux de zoom, rien dessiné hors du cadre sur une petite carte, aucun marqueur pour un PNJ hors champ).
- Onglet Relations simulé sur la sauvegarde : les 30 villageois rencontrés affichent tous une info, et aucun bâtiment non visité n'est nommé (Robin → « Vers : Montagnes »).

## À valider en jeu
- [ ] L'onglet (livre bleu) apparaît à droite de « Quitter », avec l'infobulle « Pelican Memory » au survol.
- [ ] Clic sur l'onglet → page avec 4 cases à cocher, description au survol.
- [ ] Décocher « Infos de pêche » → l'infobulle d'un poisson redevient vanilla immédiatement ; recocher → elle revient.
- [ ] Carte (M) : noms des zones visitées, rien sur les zones jamais visitées, pas de chevauchement.
- [ ] Redimensionner la fenêtre avec l'onglet ouvert → pas de plantage.
- [ ] Manette : navigation jusqu'à l'onglet.

- [ ] Minimap ronde : cadre net, boutons + et − actifs, curseur d'opacité dans l'onglet.
- [ ] Minimap : s'affiche en haut à gauche, suit le joueur, têtes des villageois rencontrés visibles, Page préc. / Page suiv. zooment, disparaît pendant les cinématiques et les menus.
- [ ] Minimap : changer de lieu, dormir, changer de saison → la carte se met à jour, pas de chute de framerate.
- [ ] Objet attendu par un lot : le rappel s'affiche dans l'infobulle ; après l'avoir donné, il passe à « déjà donné ».
- [ ] Onglet Relations : le lieu s'affiche sous les cœurs sans chevaucher le texte de relation (vérifier un célibataire, un marié à plus de 10 cœurs, et un nom de lieu long).
