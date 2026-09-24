# Pelican Memory

Mod SMAPI pour Stardew Valley 1.6+ (testé avec la 1.6.15 et SMAPI 4.5.2).
C'est un **outil de mémoire, pas un guide** : il n'affiche que ce que le joueur a déjà découvert dans **sa** sauvegarde.

## Features

| ID | Ce que ça fait | Filtre anti-spoil |
|---|---|---|
| `visited-map-labels` | Nomme les **zones** de la carte du monde (Montagnes, Pélican Ville, Voie ferrée…) | Uniquement les zones où le joueur est allé (`Farmer.locationsVisited`) |
| `caught-fish-tooltip` | Ajoute eau / saisons / météo / horaires à l'infobulle d'un poisson | Uniquement les poissons attrapés (`Farmer.fishCaught`). Saisons et type d'eau calculés **seulement à partir des lieux visités** |
| `minimap` | Minimap du lieu courant en haut à gauche, zoom avec Page préc. / Page suiv. | Seulement le lieu où le joueur se trouve, et seulement les villageois déjà rencontrés (`friendshipData`) |
| `community-center-hints` | Signale sur l'objet qu'un lot du Centre communautaire l'attend, ou qu'il a déjà été donné | Seulement les salles dont le panneau est visible en jeu (même règle que l'indice vanilla), rien sur la voie Joja |
| `purchase-confirm` | Demande la quantité avant tout achat en boutique (fenêtre vanilla, défaut 1, total affiché, Annuler) | Garde-fou : ni mémoire ni assistance, aucune donnée de jeu révélée |
| `social-locations` | Position de chaque villageois dans l'onglet Relations | Le bâtiment n'est nommé que s'il a été visité ; sinon la zone (« Montagnes ») si elle est connue ; sinon « Lieu inconnu » |

| `museum-hints` | Signale qu'un minerai ou un artefact manque encore au musée, ou qu'il a déjà été donné | Seulement après la première visite du musée ; l'état vient des dons réels (`LibraryMuseum`) |
| `farm-layers` | Teinte la zone couverte par les arroseurs et épouvantails, et l'aperçu de celui qu'on tient | Ne lit que ce que le joueur a posé lui-même |
| `animal-care` | Petite icône au-dessus d'un animal qui attend une caresse, une traite ou une tonte | État réel de l'animal du joueur, rien d'autre |
| `recipe-lookup` | Survol + `R` : les recettes de cuisine connues qui utilisent l'objet, ce qui manque et dans quel coffre | Seulement les recettes **apprises** (`cookingRecipes`) et les stocks du joueur |
| `chest-search` | L'infobulle dit combien on en a rangé ; `O` ouvre la liste des coffres qui en contiennent | Ne regarde que les coffres et frigos du joueur, et jamais le coffre déjà ouvert à l'écran |
| `skill-xp` | Au survol d'un talent, l'expérience accumulée et ce qu'il reste avant le niveau suivant | Progression du joueur uniquement (`experiencePoints`), que le jeu compte sans l'afficher |
| `crafting-filters` | Onglets sur le bord gauche de la page Artisanat : ferme, pêche, machines, déco, aventure, divers | Ne cache rien : la liste filtrée est un sous-ensemble exact de ce que le jeu affichait |
| `transfer-quantity` | Dans un coffre ou un frigo, Ctrl + clic sur une pile : on tape le nombre, Entrée, et exactement ce nombre passe de l'autre côté | Sans objet (confort) |
| `horse-actions` | À cheval : coffres et machines utilisables sans descendre, un clic sur une porte fait descendre et entrer, et le cheval ne bloque plus le passage | Sans objet (confort) |
| `deposit-everywhere` | Dans un coffre de la ferme, un bouton envoie chaque objet du sac (barre d'outils exclue) vers le coffre de la ferme qui en contient déjà le plus, quelle que soit la qualité | Volontairement limité à la ferme et à un bouton dans un coffre : tout renvoyer depuis une grotte serait de la triche |
| `self-update` | À l'écran-titre, propose la nouvelle version avec ses nouveautés ; un clic l'installe et ferme le jeu, une seule relance suffit | Sans objet (outil du mod) ; aucune donnée envoyée, simple lecture de `dist/update.json` |
| `chest-names` | Un bouton dans le menu d'un coffre pour le nommer ; le nom remplace le lieu partout où le mod cite ce coffre | Donnée du joueur uniquement (`modData` du coffre, sauvegardée et synchronisée en multi) |
| `fish-hints` | Sur un poisson jamais attrapé, décrit chaque condition **par comparaison** avec un poisson déjà pris | Une condition qu'aucune prise du joueur ne permet d'exprimer reste en `????` ; un poisson qui ne vit que dans un lieu jamais visité ne dit **rien** |

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

Depuis la 1.3.0, l'installeur ne sert plus qu'à la **première installation** : ensuite le mod se met à jour lui-même depuis l'écran-titre. Relancé plus tard, il réinstalle quand même la dernière version en **conservant `config.json`** — c'est le chemin pour quelqu'un resté sur une version antérieure à la 1.3.0, qui ne sait pas encore se mettre à jour seule, ou pour réparer une installation. Les deux constantes du dépôt sont en haut de `installer/Program.cs`, à changer si le dépôt change de nom ou de compte.

L'exe n'est pas signé : au premier lancement, Windows affiche « Windows a protégé votre ordinateur ». Il faut cliquer sur **Informations complémentaires**, puis **Exécuter quand même**.

## Publier une nouvelle version — la marche à suivre

À suivre dans l'ordre, à chaque version. **Rien ne part sur GitHub sans le feu vert de Jordan** : tout ce qui est poussé arrive chez sa compagne à son prochain lancement.

1. **Coder et faire valider en jeu.** Chaque `dotnet build` déploie la version en cours dans `Mods/` sur ce PC : Jordan teste là, rien ne sort.
2. **Choisir le numéro** : `x.y.Z` pour un correctif, `x.Y.0` pour une nouvelle feature. Le mettre dans `manifest.json`.
3. **Écrire les nouveautés en tête de `changelog.json`** : `{ "Version", "Fr": [...], "En": [...] }`. C'est ce que les joueurs lisent dans la fenêtre de mise à jour, donc des phrases courtes, du point de vue du joueur (« survolez un objet et appuyez sur R »), et **aucun spoil** : même règle que pour le mod. `publish.ps1` refuse de publier si la version du manifeste n'y est pas en tête.
4. **Tester la mise à jour elle-même** (voir ci-dessous). Obligatoire si `SelfUpdater`, `GameRestarter`, `UpdateMenu` ou le format de `changelog.json` ont changé ; sinon facultatif.
5. **Demander le feu vert à Jordan**, puis lancer `publish.ps1` :
   ```bash
   powershell -ExecutionPolicy Bypass -File publish.ps1
   ```
   Il reconstruit le mod en `Rebuild` (un simple build met la DLL à jour mais **garde l'ancien zip**), prend le zip **par son nom de version**, vérifie `changelog.json`, puis remplit `dist/` : `PelicanMemory.zip`, `version.txt` (lu par l'installeur) et `update.json` (lu par le mod). Il reconstruit aussi l'installeur dans `publish/`.
6. **Contrôler le paquet avant de commiter** : la version dans le manifeste du zip, `dist/version.txt`, la première entrée de `dist/update.json`, et le fichier en UTF-8 sans BOM (des `?` à la place des accents dans le terminal ne veulent rien dire, il faut vérifier les octets).
7. **Commit + push** sur `main` : `git add -A`, un message qui dit ce que la version apporte, puis `git push origin main`. Pas de release GitHub à créer : le mod et l'installeur lisent `dist/` via `raw.githubusercontent.com`, donc le dépôt doit rester public.
8. **Vérifier en ligne** que `dist/version.txt`, `dist/update.json` et `dist/PelicanMemory.zip` sont bien servis à jour sur `https://raw.githubusercontent.com/e-massage-app/PelicanMemory/main/dist/`.
9. Tenir à jour `tasks/todo.md`, et la section « Vérifications faites » plus bas.

### Tester la mise à jour avant de la publier
Le mod peut lire ses mises à jour dans un dossier local au lieu de GitHub : on installe la version à publier sur le PC de Jordan, et on lui fait proposer une version de test fabriquée à partir du même code.

1. **Fabriquer le paquet de test sans le déployer** : mettre `x.y.z-test` dans `manifest.json` (le suffixe `-test` la classe après la vraie `x.y.z` et avant la suivante), puis
   ```bash
   dotnet build PelicanMemory.csproj -c Release -t:Rebuild -p:EnableModDeploy=false
   ```
   et **remettre aussitôt** la vraie version dans `manifest.json`.
2. Copier `bin/Release/net6.0/PelicanMemory x.y.z-test.zip` en `test-feed/PelicanMemory.zip`, et écrire `test-feed/update.json` : `changelog.json` avec, en tête, une entrée `x.y.z-test` dont la première ligne dit quoi vérifier (« si la console SMAPI affiche x.y.z-test après le redémarrage, tout fonctionne »). Pour tester les titres et les pages, annoncer deux versions de test. `test-feed/` est hors dépôt.
3. Jeu **fermé** : `dotnet build` (déploie la vraie version), puis ajouter `"UpdateSource": "<chemin complet de test-feed>"` dans `Mods/PelicanMemory/config.json` — en modifiant le fichier, jamais en le remplaçant : ce sont les vrais réglages de Jordan.
4. Jordan lance le jeu par Steam : la fenêtre propose la version de test, « Mettre à jour et redémarrer », le jeu revient seul. Le journal `%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt` doit afficher `Pelican Memory x.y.z-test`, et le dossier du mod ne doit plus contenir de `*.old`.
5. **Remettre le PC au propre, sans l'oublier** : retirer `UpdateSource` de la config, puis `dotnet build` pour réinstaller la vraie version. Sinon le jeu de Jordan reste branché sur le dossier de test et ne verra plus jamais les vraies mises à jour.

Le mécanisme de remplacement se teste aussi hors jeu, avec la DLL verrouillée comme par SMAPI : banc `updatetest` (voir « Vérifications faites »).

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
- **Bouton de nom du coffre** : placé d'après les boutons que le jeu a réellement créés (`okButton`, sinon `trashCan`), jamais d'après un calcul. Un grand coffre élargit sa grille au-delà du cadre du menu **puis** décale `yPositionOnScreen` de 42 px, donc toute position calculée à la main finit sur les cases. Sous le bouton OK est le seul endroit que la grille ne peut pas atteindre, quelle que soit la taille du coffre.
- **Familles d'artisanat** : le jeu n'a pas de catégories de fabrication. Chaque recette est classée d'après ce qu'elle produit, lu dans les données du jeu (catégorie de l'objet, `Data/Machines`, étiquettes de contexte) ; seuls les objets que les données ne distinguent pas sont reconnus à leur nom interne anglais. Le classement des 150 recettes vanilla a été vérifié hors jeu.
- **Mise à jour depuis le jeu** (depuis la 1.3.0) : le mod lit `dist/update.json` en tâche de fond au démarrage, et n'affiche rien s'il n'y a pas de connexion ou pas de nouvelle version. La fenêtre n'apparaît que sur l'écran-titre (`TitleMenu.subMenu`), jamais en pleine partie.
- **Remplacer une DLL chargée** : Windows interdit de l'écraser mais autorise de la **renommer**. Chaque fichier passe en `*.old`, le nouveau prend son nom, et le lancement suivant supprime les `*.old`. En cas d'échec à mi-chemin, tout est remis en place ; un paquet dont le manifeste n'est pas exactement la version annoncée de ce mod est refusé sans rien toucher ; `config.json` n'est jamais touché.
- **Redémarrage automatique** : un programme ne peut pas se relancer lui-même, donc le PowerShell de Windows attend la fermeture du jeu, puis le relance **par Steam** (`steam://rungameid/413150`) si c'est Steam qui l'avait lancé — pour garder temps de jeu et succès —, sinon par `StardewModdingAPI.exe`. Si la relance ne peut pas être préparée, le message demande simplement de relancer à la main.
- **À cheval** : le jeu refuse tout objet posé quand on est monté (sauf les portillons), **tous les clics sur un bâtiment de ferme** (porte de la maison, trappe des animaux, actions du bâtiment : `Building.doAction` commence par ce refus), et tout autre clic fait descendre avant même de chercher une porte. La portée d'un clic se mesure depuis la case du joueur, c'est-à-dire le milieu d'un cheval plus long qu'une case : elle est ici mesurée depuis le cheval **tel qu'il est dessiné** (`Utility.tileWithinRadiusOfPlayer`, rayon 1 seulement, joueur local à cheval) : sa boîte de collision ne couvre que les sabots (1,5 × 0,5 case), alors que le sprite fait 2 × 2 cases au-dessus. Portée = une case autour de ce carré ; vérifié par force brute (1,9 million de positions au pixel près) : la portée du jeu est toujours incluse, et un coffre collé au cheval de n'importe quel côté est toujours atteignable. Premier test en jeu (2026-09-23) : porte de la maison impossible, coffres une fois sur deux — les deux causes corrigées ainsi. Les objets sont donc utilisés depuis la selle **après** les vérifications du jeu (bâtiments et villageois gardent leur priorité), avec exactement la logique du jeu à pied ; les actions de décor (portes…) font descendre instantanément avec `Horse.dismount()`, l'appel même que le jeu utilise en passant une porte à cheval ; et comme `dismount()` rend le cheval solide, il est remis en `farmerPassesThrough` juste après.
- **Objets hauts à cheval** : un coffre ou une machine est dessiné sur deux cases mais n'existe que sur sa case du bas. À pied, sa moitié haute est hors de portée, et le jeu se rabat sur la case devant le joueur — qui se trouve être l'objet. La portée élargie de la selle rendait cette moitié « à portée » et vide : clic refusé, descente de cheval (« de face ça rate, par derrière ça marche », test n°4). Désormais un objet haut est à portée si sa base l'est, et un clic sur sa moitié haute lui est redirigé (sauf si la case appartient à un bâtiment). Vérifié par force brute : 1,18 million de clics simulés (base et moitié haute, coffre collé au cheval sur chaque côté), aucun hors de portée.
- **Clic gauche à cheval** : le jeu envoie le clic gauche d'un cavalier vers l'action (les outils sont inutilisables en selle), mais les coffres, le coffre d'expédition et la trappe des animaux n'acceptent qu'un **clic droit** (`Game1.didPlayerJustRightClick`). Refusé, le clic finissait en descente de cheval — à chaque fois. Pendant un clic fait depuis la selle, tout clic compte donc comme un clic droit, sauf si un villageois, un animal de compagnie ou un autre joueur est sur la case (sinon un clic gauche pourrait lui offrir l'objet en main). L'indicateur est remis à zéro par un *finalizer*, qui s'exécute même en cas d'erreur.
- **Ranger partout** : même règle que le bouton « Ajouter au tas existant » du jeu (compléter les tas, puis une case libre du même coffre), mais vers tous les coffres de la ferme et de ses bâtiments ; chaque objet va au coffre qui en contient le plus, par type d'objet (toutes qualités), et un objet sans coffre attitré reste dans le sac. Exclus : la barre d'outils, les outils, les coffres spéciaux (mini coffre d'expédition, trémie, enrichisseur) et un coffre ouvert au même moment par l'autre joueur ; les coffres Junimo, qui partagent un même inventaire, ne comptent qu'une fois. `Chest.addItem` réduit la pile qu'on lui donne et, quand il reste de la place, dépose **l'objet même** du sac : la case du sac est donc vidée à la main. Le bouton se place au premier emplacement libre et visible (à droite de « Ajouter au tas existant » en priorité), vérifié contre tous les éléments du menu. Logique vérifiée hors jeu sur de vrais coffres et objets (banc `deposittest`, 15 contrôles dont la conservation des quantités dans 4 scénarios).
- **Fenêtre de quantité** : le jeu la pré-remplit avec la valeur par défaut et ajoute derrière ce qu'on tape (« 1 » puis « 37 » = 137). Elle s'ouvre donc vide (`UI/QuantityPrompt`). La fonction qui range dans le sac réduit la pile qu'on lui donne et la rend comme « reste » : la quantité réellement déplacée se calcule depuis la quantité demandée.
- **Onglet Relations** : le texte est écrit sous les cœurs, dans la zone libre de la colonne ; la ligne descend quand le personnage a plus de 10 cœurs (deux rangées).

## Vérifications faites
- Build : 0 erreur, 0 avertissement (analyseurs SMAPI compris).
- Les 7 patchs Harmony ont été appliqués sur la vraie `Stardew Valley.dll` depuis un programme de test (Harmony vérifie les noms de paramètres) : tous OK. Le retrait d'un patch n'enlève que le nôtre.
- `FishInfoResolver` a été exécuté sur les vraies données du jeu et sur la sauvegarde `Elan` (37 poissons) : résultats conformes au wiki. Le poisson-globe n'affiche que l'été tant que l'île n'est pas visitée.
- La découverte des lieux a été simulée sur la même sauvegarde : seuls les lieux réellement visités sont affichés (bug `Mine`/`Mines` trouvé et corrigé).
- Testé en jeu le 2026-09-17 par Jordan : infobulles de pêche validées ; minimap et noms de carte corrigés ensuite (cadre qui masquait la minimap, passage des bâtiments aux zones).
- Géométrie de la minimap : 25 assertions passées hors jeu (joueur toujours centré aux 4 niveaux de zoom, rien dessiné hors du cadre sur une petite carte, aucun marqueur pour un PNJ hors champ).
- Mise à jour rejouée hors jeu, DLL tenue ouverte comme par SMAPI : paquet de mauvaise version refusé sans rien toucher, bonne version installée, `config.json` intact, puis nettoyage des `*.old` au « lancement suivant » dans un nouveau processus (15 vérifications).
- Indices de pêche exécutés hors jeu sur les vraies données et la sauvegarde `Elan` (59 poissons pris) : chaque comparaison renvoie bien à un poisson réellement attrapé, jamais à lui-même ; 11 poissons manquants sont décrits, 7 ne disent rien faute d'avoir pêché là où ils vivent.
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
- [ ] Survol d'un objet : « N en réserve » dans l'infobulle, `O` ouvre la liste des coffres (couleur du coffre respectée).
- [ ] Dans un coffre ouvert : le total ne compte plus ce coffre-là, et disparaît s'il est le seul à en contenir.
- [ ] Onglet Compétences : survol d'un talent → ligne d'XP au-dessus de la description, et « niveau maximum » sur un talent à 10.
- [ ] Page Artisanat : 7 onglets à gauche, aucun chevauchement avec la grille, le filtre garde les recettes inconnues en silhouette.
- [ ] Ctrl + clic sur une pile dans un coffre (et dans le sac, coffre ouvert) : fenêtre vide, taper 37 + Entrée → exactement 37 passent ; coffre plein → message et seule la partie qui rentre passe ; clic normal inchangé.
- [ ] Achat en boutique : la fenêtre de quantité s'ouvre vide, taper 5 achète 5 (et non 15).
- [ ] À cheval : ouvrir un coffre, récupérer une machine prête, remplir une machine avec l'objet en main — sans descendre. Clic sur une porte de Pélican Ville : on descend et on entre, le cheval reste dehors. Une fois descendu, on traverse son cheval.
- [ ] Ranger partout : bouton visible à côté de « Ajouter au tas existant » dans un coffre de la ferme (absent ailleurs), rangement dans les bons coffres, récapitulatif, barre d'outils intacte.
- [ ] Mise à jour depuis l'écran-titre : fenêtre lisible, « Jouer sans mettre à jour » revient au titre, « Mettre à jour » ferme le jeu, et la relance charge la nouvelle version sans reproposer la fenêtre.
- [ ] Bouton « pancarte » sous le bouton OK du coffre : le survol montre le nom, le clic ouvre la fenêtre de saisie, et le nom remplace le lieu dans les fenêtres `O` et `R`. À vérifier sur un grand coffre **et** un coffre normal.
- [ ] Collections → Poissons : survol d'un poisson non attrapé → lignes de comparaison lisibles, `????` là où rien ne peut être dit.
- [ ] Onglet Relations : le lieu s'affiche sous les cœurs sans chevaucher le texte de relation (vérifier un célibataire, un marié à plus de 10 cœurs, et un nom de lieu long).
