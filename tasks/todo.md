# Pelican Memory — en cours

## 2026-09-22 — « Où est cet objet ? » + indices de pêche

- [x] Sortir le scan des coffres de `RecipeFinder` vers `Core/StorageIndex` (partagé, rafraîchi à la fermeture d'un coffre et au lever du jour)
- [x] Sortir la détection de l'objet survolé vers `Core/HoveredItem` (partagé avec les recettes)
- [x] Feature `chest-search` : total en réserve dans l'infobulle + fenêtre `O` listant les coffres
- [x] Feature `fish-hints` : décrire un poisson jamais pris par comparaison avec les prises du joueur
- [x] Banc de patches Harmony repassé (14 patchs OK, dont `CollectionsPage.performHoverAction`)
- [x] Banc d'indices de pêche sur la vraie sauvegarde : aucune comparaison inventée
- [x] Retour du 22/09 : ne pas compter le coffre déjà ouvert à l'écran (exclusion par référence sur le contenu, marche aussi pour le frigo)
- [x] Retour du 22/09 : nommer ses coffres (`chest-names`, bouton dans la colonne du menu + `NamingMenu` vanilla, nom dans `modData`)
- [x] Retour du 22/09 : pancarte de nom replacée sous le bouton OK (ancrée aux boutons réels) et nom passé en infobulle
- [x] Jauges d'XP au survol des talents (`skill-xp`)
- [x] Filtres de l'artisanat (`crafting-filters`), 7 familles, classement vérifié hors jeu sur les 150 recettes
- [x] 1.3.0 : mise à jour depuis le jeu (`self-update`), rejouée hors jeu avec la DLL verrouillée
- [x] Test de Jordan n°1 : 1.3.0 → 1.3.1-test réussi en vrai (console SMAPI, `.old` nettoyés)
- [x] Retours : liste complète en pages (flèches vanilla, molette, ←/→), bouton trop long, redémarrage automatique via Steam (`GameRestarter`, testé hors jeu)
- [ ] Test de Jordan n°2 : 1.3.0 installée, `test-feed/` annonce 1.3.2-test + 1.3.1-test (titres par version + plusieurs pages)
- [ ] Après son test : retirer `UpdateSource`, redéployer la 1.3.0, puis publier 1.3.0 sur son feu vert
- [ ] Test en jeu par Jordan
- [ ] Sur son accord : bump 1.2.0, `publish.ps1`, commit, push

## Règle d'affichage des indices de pêche

Pour chaque condition (endroit, saison, météo, heure, niveau, ferrage) :

1. condition inexistante (toute l'année, tous temps, toute heure, niveau 0) → phrase simple ;
2. sinon, un poisson **déjà attrapé** partage exactement la même valeur → « comme « X » » ;
3. sinon → `????`.

Et si le poisson ne vit dans **aucun lieu visité**, aucune ligne n'est affichée du tout : le joueur
n'aurait rien pu apprendre, et ça lui évite de le chercher pour rien.

Le poisson comparé est celui qui partage le plus de conditions avec la cible, pour qu'un même
poisson familier porte plusieurs lignes quand c'est possible.

## 2026-09-23 — 1.4.0 (installée sur le PC de Jordan, pas publiée)

- [x] `transfer-quantity` : Ctrl + clic dans un coffre/frigo → fenêtre de quantité → transfert exact, dans les deux sens
- [x] Fenêtre de quantité ouverte vide (bug du jeu : « 1 » + « 37 » = 137), corrige aussi la confirmation d'achat
- [x] `horse-actions` : objets utilisables à cheval, portes → descente + entrée, cheval traversable une fois descendu
- [x] Patchs vérifiés sur la vraie DLL, `changelog.json` rempli pour la 1.4.0
- [x] Test n°1 de Jordan : quantités d'achat OK ; cheval : porte de la maison impossible (les bâtiments de ferme refusent tout à cheval) et coffres intermittents (portée mesurée depuis la selle) → corrigés
- [x] Test n°2 : coffres au-dessus du cheval tourné vers le haut → portée mesurée sur les sabots seulement ; corrigée sur le cheval dessiné
- [x] Règle finale : seules les portes des bâtiments de ferme font descendre, tout le reste en selle
- [x] Test n°3 : « quel que soit le clic, ça me descend » → les coffres n'acceptent que le clic droit, le clic gauche d'un cavalier était refusé puis finissait en descente ; tout clic depuis la selle compte maintenant comme un clic droit (sauf sur un villageois)
- [x] Test n°4 : « de face ça rate, par derrière ça marche » → la moitié haute d'un coffre devenait à portée et vide ; redirigée vers sa base, et portée comptée depuis la base
- [x] Test n°5 : cheval « beaucoup mieux »
- [x] `deposit-everywhere` (Ranger partout) : décisions de Jordan = par type d'objet, barre d'outils protégée, bouton dans les coffres seulement (anti-triche) ; limité à la ferme ; banc hors jeu 15/15
- [x] Test de Jordan : Ranger partout (bouton refait à partir du bouton du jeu), Ctrl + clic validé
- [ ] Publication sur son feu vert (marche à suivre du README)

## 2026-09-24 — 1.4.1 (publiée le 2026-10-04)

- [x] Bug « Ranger partout ne marche pas sur plein d'objets » : la maison, la cabane, la serre et les intérieurs n'étaient pas reconnus comme « la ferme » (lien parent vide) → 53/239 types d'objets jamais rangeables. Corrigé avec `IsFarm`/`IsGreenhouse`/cave, île exclue
- [x] Signalement des objets dont le coffre est plein
- [x] Banc `deposittest` : 18/18
- [x] 2026-10-04, retour de Jordan « pas mieux » : la barre d'outils protégée gardait tout le butin ramassé (sa save : 3 piles restantes, toutes dans la barre). Choix de Jordan : tout ranger sauf l'objet en main
- [x] Banc recréé dans le dépôt (`tests/DepositTest`, les sources du scratchpad avaient disparu) : 19/19
- [x] Déployé sur son PC le 2026-10-04 (jeu fermé, config intacte, UpdateSource null)
- [x] Test de Jordan : il a choisi de ne pas tester, feu vert direct (« tu peux push »)
- [x] Publiée le 2026-10-04 (commit 9099a01), vérifiée en ligne ; 1.4.0 publiée remise sur son PC (étape 9), config intacte, UpdateSource null

## 2026-10-05 — 1.5.0 « Chercher un objet » (publiée le 2026-10-05)

Demande : Elise ne sait jamais où sont ses objets une fois rangés. Dans le menu Échap, taper un nom → où on en a, combien, sinon où l'acheter ou comment le fabriquer.
Décisions de Jordan : la recherche ne connaît que les objets **déjà croisés** ; achat/fabrication **seulement ce que le joueur a vécu**.

### Ce que verra le joueur
- Un nouvel onglet (loupe) dans le menu Échap, à côté de celui de Pelican Memory : un champ de saisie en haut, les résultats en dessous, mis à jour à chaque lettre.
- Recherche sans tenir compte des majuscules ni des accents (« peche » trouve « Pêche »), sur une partie du nom.
- Pour chaque objet trouvé : icône + nom, puis
  - **Sur vous** : quantité dans le sac ;
  - **Rangé** : quantité par coffre (nom donné au coffre, sinon le lieu), frigo compris ;
  - **En vente** : boutiques **déjà visitées** qui le vendent **en ce moment**, avec le prix ;
  - **Fabrication / Cuisine** : recettes **apprises** qui le produisent, avec les ingrédients (ce qu'on a / ce qu'il faut) ;
  - sinon : « Rien de connu pour l'obtenir pour l'instant ».
- Un objet jamais croisé n'apparaît jamais, même si on tape son nom exact (« Aucun objet connu »).

### « Objets déjà croisés » (anti-spoil)
- Rétroactif, depuis la sauvegarde : sac + coffres/frigos, objets expédiés (`basicShipped`), poissons (`fishCaught`), minéraux (`mineralsFound`), artefacts (`archaeologyFound`), plats cuisinés (`recipesCooked`), couture (`tailoredItems`), produits et ingrédients des recettes apprises (déjà affichés par le jeu), objets en vente aujourd'hui dans les boutiques visitées.
- À partir de la 1.5.0 : le mod retient aussi chaque objet qui entre dans le sac (données du joueur, par sauvegarde), pour que la liste grandisse avec la partie.

### Points à vérifier dans le code du jeu AVANT de coder
- [x] Lire le stock d'une boutique hors de la boutique (`ShopBuilder.GetShopStock`) : aucun effet de bord (stock limité, aléatoire du jour, marchand ambulant) ; correspondance boutique → lieu visité ; horaires/jours d'ouverture ignorés ou affichés ?
- [x] Multijoueur : Elise est invitée — les coffres des bâtiments et de la maison de l'hôte sont-ils visibles chez elle ? (sinon le dire dans le résultat plutôt que d'afficher 0)
- [x] Saisie clavier dans le menu : les touches du jeu (E, chiffres de la barre, raccourcis du mod R/O) ne doivent pas agir pendant la frappe ; Échap ferme toujours
- [x] Deux onglets du mod dans le menu : généraliser `GameMenuTab` sans casser l'onglet actuel ni le redimensionnement de fenêtre
- [x] Manette : un clic sur le champ ouvre le clavier virtuel du jeu ; avec une manette le champ ne capture pas le clavier (B arrive comme E)

### Étapes
- [x] `KnownItems` (ensemble des objets croisés + enregistrement des nouveaux) et `ItemSearch` (correspondance nom ↔ objets, sans accents) — testables hors jeu
- [x] Sources de résultat : sac/coffres (`StorageIndex`), boutiques visitées, recettes apprises (`RecipeFinder` étendu à l'artisanat)
- [x] Page de menu + onglet loupe, champ de saisie vanilla, liste défilante façon jeu
- [x] Textes fr/en, option dans la page Pelican Memory, README, changelog 1.5.0
- [x] Bancs hors jeu : recherche (accents, partiel, objet inconnu jamais proposé), sources de résultat sur la vraie sauvegarde en lecture seule ; patchs Harmony sur la vraie DLL
- [x] Vérifié dans le code du jeu : stock des boutiques sans effet de bord sauf 5 meubles aléatoires de Robin (retirés) ; en invité, cave et ferme de l'île de l'hôte non reçues (signalé dans la page) ; « e » fermait le menu (prefix sur `GameMenu.receiveKeyPress`) ; 2 onglets du mod tiennent, pas 3
- [x] Bancs : `tests/SearchTest` 12/12, `tests/DepositTest` 19/19
- [x] Déployée sur son PC (jeu fermé, config intacte, UpdateSource null)
- [x] Test de Jordan en jeu le 2026-10-05 : « encore parfait », validé du premier coup (reste le test d'Elise en invitée, après publication)
- [x] Feu vert de Jordan (« Go push ») → publiée ; 1.4.1 remise sur son PC (étape 9)

## 2026-10-05 — 1.6.0 « la ferme au survol, le bilan du soir, les épingles » (publiée le 2026-10-05, sans test en jeu préalable)

Décisions de Jordan : bulles **au survol seulement** (rien d'autre à l'écran) ; bilan **dans l'écran des ventes, sans clic**, et en messages au réveil les soirs sans vente ; outil prêt chez Clint **dans le bilan** (le jeu ne le dit qu'une fois par session) ; épingles sur la carte : oui.

- [x] `UI/WorldTooltip` : bulle du jeu au curseur dans le monde (RenderedHud, case du curseur + case du dessous pour les machines hautes)
- [x] `Core/FarmTiming` (calculs purs, testables) : jours avant récolte (lus tels quels dans `phaseDays`, engrais/métier déjà inclus), mort au changement de saison (dehors seulement, ni serre ni île), heure de fin d'une machine (`MinutesUntilReady`, nuit = jusqu'à 26h + 400, machines « seulement la nuit »)
- [x] `crop-timer` : récolte dans N jours / prête / repousse dans N jours / pas arrosée aujourd'hui / ne mûrira pas avant la fin de la saison ; arbres fruitiers : adulte dans N jours
- [x] `machine-timer` : prête à HHhMM / demain matin / dans N jours ; fût : prochaine qualité ; casier : plein demain si appâté ; en pause ; le produit n'est nommé qu'une fois prêt
- [x] `night-recap` : récoltes prêtes, cultures à arroser, cultures condamnées par la saison, machines prêtes, outil chez Clint ; panneau à gauche de l'écran des ventes (page principale, après l'intro), sinon messages au réveil (une nuit passée, pas au chargement)
- [x] `map-pins` : clic droit sur la carte → note (fenêtre de nom du jeu, Échap ferme) → croix rouge ; survol = note ; clic sur une épingle = modifier, note vide = supprimer ; par joueur (`modData`), région de la carte respectée (île à part)
- [x] Textes fr/en (228), README, changelog 1.6.0 ; banc `tests/FarmTest` 29/29 (2 attentes fausses de ma part corrigées, pas le code)
- [x] Déployée sur son PC (jeu fermé, config intacte, UpdateSource null)
- [x] Publiée sur demande de Jordan avant son test (« push direct, je testerai en direct ») ; 1.5.0 remise sur son PC (étape 9)
- [ ] Retour de Jordan après son test en jeu

## 2026-10-05 — 1.7.0 « déménager un objet » (publiée le 2026-10-05, sans test en jeu préalable)

Demande : ranger la ferme avec Robin, mais les coffres pleins et les décos bloquent ; le jeu ne pousse un coffre plein que d'une case par double coup de pioche. Choix de Jordan : coffres + machines + décos, Maj + clic puis clic.

- [x] `move-objects` : l'objet reste sur sa case jusqu'à la pose (rien à perdre), puis même objet / nouvelle case comme le jeu pour un coffre poussé ; lumière déplacée à la main ; mutex respecté
- [x] Bulles du survol en pause pendant un déménagement
- [x] Banc `tests/DepositTest` étendu : 27/27 (dont 8 sur ce qui peut bouger ; il a trouvé que les coffres posés par le jeu passaient : corrigé)
- [x] Publiée à la demande de Jordan avant son test (« je regarderai demain ») ; 1.6.0 remise sur son PC (étape 9)
- [ ] Retour de Jordan (1.6.0 et 1.7.0) ; idée en attente : teinte du cheval par le mod (le jeu n'a qu'une apparence, seulement des chapeaux)

## 2026-10-06 — 1.8.0 « bâtiments, fabrication depuis les coffres, téléphone » (publiée le 2026-10-06)

Décisions de Jordan : déplacer un objet jusque dans un bâtiment ; fabriquer/cuisiner avec les coffres **dans la limite de la ferme** ; acheter **par téléphone** chez Clint (outils) et Robin (maison, bâtiments), aux horaires où le téléphone répond, matériaux pris dans les coffres de la ferme (le téléphone est dans la maison : on reste sur la ferme).

- [x] `Core/FarmPlaces.IsOnFarm` partagé (rangement, déménagement, fabrication)
- [x] Déménager entre les lieux de la ferme : l'objet reste à sa place jusqu'à la pose ; le jeu met à jour son lieu tout seul (`OnObjectAdded`)
- [x] `Core/FarmChests` : coffres de rangement de la ferme partagés (rangement, fabrication, téléphone)
- [x] `craft-from-chests` : onglet Fabrication, cuisine et établi sur la ferme ; **seulement les coffres dont on tient le verrou** (sinon doublons/pertes en multi) : hôte/solo = toute la ferme, invité = la pièce où il est + coffres Junimo (limite du réseau du jeu) ; cache des contenus par tick (performance) vidé après chaque fabrication ; `StorageIndex` invalidé en fin de session
- [x] `phone-orders` : Clint et Robin seulement quand le téléphone répond « ouvert » (capturé, pas recalculé) ; garde-fous : outil déjà chez Clint (même prêt) = refus, agrandissement de maison en cours = refus ; la boutique de Robin (bois, meubles) reste en lecture seule ; matériaux : sac d'abord puis coffres de la ferme ; revérification au placement d'un bâtiment ; correctif PurchaseConfirm (ne pas écraser le dialogue de Clint)
- [x] Textes (239), README, changelog 1.8.0 ; banc `tests/FarmTest` : les 17 crochets fabrication + téléphone se posent sur la vraie DLL ; correctif PurchaseConfirm (ne rouvre plus la boutique sur la réplique de Clint)
- [x] 2026-10-06 retours de Jordan (sur la 1.7.0 : le clic droit pour entrer annulait le déménagement) → clic sur un bâtiment de la ferme = rangé dedans près de la porte, Échap seul annule ; Elise (invitée) a toute la ferme pour fabriquer (sans verrou hors de sa pièce)
- [x] Déployée sur son PC pour test (jeu fermé, config intacte)
- [x] Test de Jordan 2026-10-06 : bâtiments « PAR-FAIT », artisanat avec les coffres OK, téléphone OK
- [x] Publiée le 2026-10-06 sur son « Go push » ; 1.7.0 remise sur son PC (étape 9)

## 2026-10-06 — 1.9.0 « le duo sans pause » (publiée le 2026-10-06)

Constat de Jordan : en duo, rien ne met le jeu en pause, les journées filent. Choix : pause ensemble, Marnie au téléphone, quêtes du courrier partagées **avec la récompense pour chacun** (refus explicite du « retirer sans rien donner » : perdant pour l'autre). Refusés : liste de tâches commune, récolte groupée (« de la triche »). Rappel donné : l'hôte peut déjà taper `/pause` dans le chat.

- [x] `pause-together` : le temps s'arrête quand **tous** les joueurs en ligne sont dans un menu (règle du solo). Le jeu a déjà ce code pour l'écran partagé local : transpiler sur `Game1.Update` qui remplace ses 2 appels `IsLocalMultiplayer(true)` (exactement 2, sinon rien) ; seulement si tous les joueurs ont le mod ≥ 1.9.0 et pas d'hôte dédié
- [x] `phone-orders` étendu à Marnie (9h-16h, pas lundi/mardi/festival) : animaux (placement à distance comme Robin, retour à la maison au lieu du ranch codé en dur) + boutique en **achat seul** (vente bloquée), foin **dans le sac**
- [x] `shared-quests` : quêtes du courrier 100-125 (Data/Quests : caleçon du maire, citrouille…) ; quand l'un la termine, l'autre la reçoit terminée avec la même récompense (argent à réclamer + amitié 255/250), lettre retirée si pas encore lue, objet de quête perdu retiré du sac ; registre commun dans `Farm.modData` (marche aussi si l'autre est hors ligne) ; les quêtes d'histoire/progression restent individuelles
- [x] Textes (247), README, changelog 1.9.0 ; banc `tests/FarmTest` : 7 crochets Marnie/quêtes posés, la pause remplace exactement les 2 vérifications du jeu (exécutée sur le vrai code de `Game1.Update`)
- [x] Publiée sur son « go push » (pas rétroactif pour les quêtes déjà réclamées : le jeu ne garde pas de trace) ; 1.8.0 remise sur son PC (étape 9)
- [ ] Test en duo (pause, quêtes) et Marnie

## 2026-10-06 — 1.9.1 correctif (codé, jeu de Jordan ouvert)

- [x] Bug signalé par Jordan : fruit étoilé acheté chez Krobus = mangé (énergie +) ET gardé dans le sac. Cause : la confirmation d'achat (`purchase-confirm`) rangeait dans le sac le « reste sur le curseur » alors que le jeu (`Object.actionWhenPurchased` du fruit étoilé : `exitActiveMenu` + `eatObject`, renvoie faux) avait déjà fermé la boutique et l'aurait jeté. Correctif : si l'achat a quitté la fenêtre de quantité, le reste est jeté comme en vanilla. Bug présent depuis la création de la confirmation d'achat.
- [x] Bug signalé par Jordan : coffres tous rangés dans la maison → plus de fabrication dehors. Cause : le jeu rend le verrou d'un coffre dès qu'un autre joueur entre dans sa pièce (`NetMutex.Update`), donc avec Elise dans la maison les coffres de Jordan (hôte) ne comptaient plus. Correctif : verrou seulement pour la pièce où l'on est (+ Junimo), les autres coffres de la ferme sans verrou tant que personne ne les a ouverts (comme l'invité depuis la 1.8.0)
- [ ] Déploiement jeu fermé, publication sur feu vert
