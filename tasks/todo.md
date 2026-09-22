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
