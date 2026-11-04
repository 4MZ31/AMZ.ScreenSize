# Historique des versions

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
- Identification matérielle du G4 pour éviter de modifier un autre moniteur.
- Journal local et export JSON de diagnostic.
- Compilation sans NuGet, 30 tests hors matériel et huit tests matériels explicites.
- Workflows GitHub de test et de publication ; distribution EXE, ZIP et empreintes SHA-256.

### Compatibilité vérifiée

Samsung Odyssey G4 S27BG400EU (`SAM727E`), DisplayPort, AMD Radeon RX 7800 XT. Le HDMI et les autres modèles n’ont pas été validés. L’application modifie la taille d’image du moniteur, sans changer la définition Windows.
