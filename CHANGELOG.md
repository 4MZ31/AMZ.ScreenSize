# Historique des versions

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
