# Historique des versions

## 2.0.1 — 2026-11-04

- Suppression des fonds noirs et des fragments de contrôles derrière les boutons et interrupteurs : peinture opaque complète, indépendante de la transparence native.
- Navigation latérale prolongée jusqu’au bas de la fenêtre, y compris à côté de la barre d’état.
- Correction des superpositions dans les profils, au démarrage et dans les mises à jour.
- Catalogue de jeux et profils intégré : recherche, sélection, création à l’unité ou par groupe et édition dans la même page, sans fenêtre métier supplémentaire.
- Préservation des choix pendant la recherche et protection des brouillons lors des rafraîchissements et mises à jour.
- 114 assertions hors matériel, dont 19 nouvelles vérifications de peinture, géométrie, catalogue et création intégrée.

## 2.0.0 — 2026-11-04

- Refonte complète de l’interface : navigation Écran / Profils de jeux / Réglages, hiérarchie et parcours réorganisés.
- Thème sombre par défaut, clair disponible dans les réglages et préférence persistante, y compris après migration.
- Aperçu proportionnel du moniteur, identité graphite, angles de cadrage et typographie Windows cohérente.
- Profils édités dans la page, brouillons conservés, validation intégrée, sauvegarde et annulation explicites.
- Détection avec recherche et compteur d’ajout ; commandes, menus et états interactifs communs.
- Vérifications périodiques silencieuses ; file des commandes pour éviter de perdre un clic pendant une opération.
- Installation des mises à jour différée pendant l’édition d’un brouillon ou la sélection de jeux.
- Fonctions existantes et préférences conservées ; 95 assertions hors matériel et cycle réel à deux jeux avec restauration vérifiée.
- Méthodologie Unslop de Damir Sokolovsky (@heydamir), CC BY 4.0 ; audit et attribution dans `docs/ux-redesign.md`.

## 1.1.0 — 2026-11-04

### Nouveautés

- Profils pour tous les jeux : nom, processus, format et activation propres à chaque jeu.
- Gestionnaire permettant d’ajouter, modifier, désactiver et supprimer les profils.
- Détection automatique des installations Steam, Epic Games, GOG, Xbox accessibles et de certaines installations déclarées dans Windows.
- Sélection des jeux détectés pour créer plusieurs profils, avec choix du format initial et correction manuelle de l’exécutable.
- Priorité au premier jeu détecté, passage au jeu suivant à sa fermeture et restauration du format d’origine après le dernier jeu.
- Reprise automatique du profil Counter-Strike et des préférences de la version 1.0.
- Menus de notification adaptés aux profils ; mises à jour différées tant qu’un jeu géré ou l’éditeur de profils est ouvert.

### Vérifications

- 78 assertions hors matériel, dont 30 nouvelles vérifications de profils, migration et détection d’installations.
- Test réel avec deux processus temporaires : formats 17″ puis 21,5″ et restauration de Wide.
- Détection locale vérifiée sur dix jeux installés ; les captures de documentation utilisent des jeux fictifs.

## 1.0.0 — 2026-11-04

Première version exécutable de **AMZ.ScreenSize**, qui succède au script Odyssey CS.

### Fonctionnalités

- Application Windows portable avec une interface en français.
- Huit formats du Samsung Odyssey G4 : Auto, Wide et tailles de 17″ à 23″.
- Aperçu de la zone d’affichage et application manuelle avec relecture du moniteur.
- Profil configurable pour Counter-Strike 2 et CS:GO.
- Restauration du mode précédent à la fermeture du jeu, à la désactivation et à la fermeture de l’application.
- Préférences persistantes et récupération d’une restauration interrompue.
- Icône de notification, menu de choix du profil et lancement avec Windows.
- Mises à jour automatiques depuis GitHub Releases, désactivables, avec vérification SHA-256 et installation différée après la partie.
- Sauvegarde de l’ancien EXE, redémarrage après mise à jour et délai de reprise après échec d’installation.
- Identification matérielle du G4 pour éviter de modifier un autre moniteur.
- Journal local et export JSON de diagnostic.
- Compilation sans NuGet, 44 assertions hors matériel et huit tests matériels explicites.
- Workflows GitHub de test et de publication ; distribution EXE, ZIP et empreintes SHA-256.

### Compatibilité vérifiée

Samsung Odyssey G4 S27BG400EU (`SAM727E`), DisplayPort, AMD Radeon RX 7800 XT. Le HDMI et les autres modèles n’ont pas été validés. L’application modifie la taille d’image du moniteur, sans changer la définition Windows.
