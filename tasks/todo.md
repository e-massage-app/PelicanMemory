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

## 2026-09-24 — 1.4.1 (installée sur le PC de Jordan, pas publiée)

- [x] Bug « Ranger partout ne marche pas sur plein d'objets » : la maison, la cabane, la serre et les intérieurs n'étaient pas reconnus comme « la ferme » (lien parent vide) → 53/239 types d'objets jamais rangeables. Corrigé avec `IsFarm`/`IsGreenhouse`/cave, île exclue
- [x] Signalement des objets dont le coffre est plein
- [x] Banc `deposittest` : 18/18
- [x] 2026-10-04, retour de Jordan « pas mieux » : la barre d'outils protégée gardait tout le butin ramassé (sa save : 3 piles restantes, toutes dans la barre). Choix de Jordan : tout ranger sauf l'objet en main
- [x] Banc recréé dans le dépôt (`tests/DepositTest`, les sources du scratchpad avaient disparu) : 19/19
- [x] Déployé sur son PC le 2026-10-04 (jeu fermé, config intacte, UpdateSource null)
- [ ] Test de Jordan
- [ ] Publication 1.4.1 sur son feu vert, puis lui remettre la 1.4.0 publiée (étape 9)
