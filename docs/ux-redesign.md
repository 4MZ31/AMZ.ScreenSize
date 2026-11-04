# Refonte de l’interface — 2.0

## Méthode et périmètre

Travail guidé par **Unslop**, créé par **Damir Sokolovsky (@heydamir)** : [dépôt original](https://github.com/heydamir/unslop), [licence CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Ce document adapte sa méthode à une application Windows Forms. Les composants, les dessins du moniteur et les pictogrammes sont du code du projet, sans bibliothèque graphique supplémentaire.

La demande porte explicitement sur une réorganisation complète. La stack C# / Windows Forms / .NET Framework 4.8, les huit préréglages et l’ensemble des fonctions existantes sont conservés.

## Audit de la version 1.1

- `MainForm.cs` : lancement avec Windows dans le pied de page, mises à jour dans l’en-tête et automatisme dans le corps ; aucune destination claire pour les préférences.
- `MainForm.cs` : application manuelle et attribution à un profil se mêlent à la configuration des jeux ; la hiérarchie des actions reste ambiguë.
- `MainForm.cs` : caractères de 8 à 9 points, nombreuses hauteurs et marges spécifiques, boutons sans traitement commun du focus.
- `MainForm.cs` : l’aperçu quadrillé suggère un outil de mesure alors qu’il représente seulement la surface affichée.
- `ProfilesForm.cs` : fenêtres imbriquées pour modifier un profil puis sélectionner des jeux ; contexte du moniteur perdu pendant l’édition.
- `ProfilesForm.cs` : validation par boîte de dialogue ; l’état de brouillon n’est pas explicite.
- `GameDiscoveryForm.cs` : tableau Windows par défaut, petits boutons et faible distinction entre recherche, sélection et création.
- `TrayApplication.cs` : la vérification périodique déclenche l’état « changement en cours », désactive brièvement l’interface et peut ignorer une action pendant une opération.
- Toutes les fenêtres : palette claire uniquement, styles et textes secondaires définis séparément.

La version initiale n’abusait ni de gradients ni de décorations. La refonte répond principalement à des problèmes d’organisation, de lisibilité et de cohérence.

## Direction retenue

- **Produit :** contrôler son écran et attribuer un format à chaque jeu.
- **Caractère :** précis, calme, attentif.
- **Univers :** instrument de calibration, graphite et accent vert mesuré.
- **Moments :** aperçu de la zone physique affichée ; liste de profils facile à parcourir.
- **Signature :** quatre angles de cadrage autour du format sélectionné.

## Les neuf passes

### 1. Espacements

Grille de 4 px, navigation de 36 px, pas vertical de 40 px, commandes de 40 px, marges de page de 32 px et intérieurs de 20 px. Les formats ont des lignes identiques ; les profils ont une ligne de titre et une ligne de format. Les dimensions du formulaire restent redimensionnables et le cadre Windows est conservé.

### 2. Couleurs

Une rampe graphite pour les fonds, surfaces et bordures. Trois rôles de texte : principal, secondaire et atténué. Le vert signale les sélections, les interrupteurs actifs et la zone du moniteur. Le bouton principal utilise le texte clair sur fond sombre en thème clair et l’inverse en thème sombre. Les deux thèmes sont centralisés dans `UiTheme.cs` ; leurs contrastes de texte et de sélection sont testés à 4,5:1 au minimum.

### 3. Pictogrammes

Un seul système local de traits, dans un cadre de 18 px : moniteur, profils, réglages, recherche, ajout et cadrage. Aucun emoji, police d’icônes ou téléchargement. La marque et l’icône de moniteur existantes sont conservées.

### 4. Textes

Les actions deviennent **Appliquer**, **Attribuer à un jeu**, **Détecter les jeux**, **Enregistrer** et **Annuler**. Les indications expliquent la différence entre zone d’affichage et résolution Windows. Le compteur de sélection de jeux apparaît dans le bouton d’ajout. Les erreurs de profil restent dans l’éditeur, près des actions.

### 5. Typographie

Segoe UI pour conserver une présence Windows native ; titres de 24 points, corps de 10,5 points et indications de 9,5 points. Segoe UI Semibold réservé aux commandes et titres de sections. Les ratios utilisent Consolas pour aligner les valeurs. Les longues chaînes sont tronquées dans les listes, sans déplacer leurs colonnes.

### 6. Organisation

Trois destinations persistantes : **Écran** pour choisir et appliquer, **Profils de jeux** pour gérer l’automatisme et les jeux, **Réglages** pour l’apparence, le démarrage, les mises à jour et le diagnostic. L’éditeur des profils est intégré dans la page, avec une liste à gauche. La détection reste un dialogue consacré à la sélection de plusieurs jeux.

### 7. Détails et interactions

Boutons avec survol, pression, focus clavier, sélection et état désactivé. Interrupteurs basés sur les contrôles Windows accessibles ; champs et listes restent natifs. Bordures, flèches, menus de notification et titre de fenêtre suivent le thème. Les informations de réussite apparaissent huit secondes dans la barre d’état, sans déplacer le contenu ; un message long reste accessible par infobulle.

Les brouillons survivent à la navigation et aux rafraîchissements. Une sauvegarde échouée ne les efface pas. Une installation automatique attend leur enregistrement ou annulation et la fermeture du dialogue de détection. Quitter explicitement avec un brouillon propose de le conserver en annulant la fermeture. La surveillance périodique reste silencieuse et les commandes utilisateur reçues pendant une opération sont mises en file.

### 8. Visuel du produit

Moniteur dessiné en code, avec surface noire, zone active proportionnelle à une dalle de 27 pouces, pied et quatre angles de cadrage. Aucun quadrillage décoratif. L’aperçu reste explicitement indicatif ; **Auto** dépend du signal reçu. Aucune image générée n’a été nécessaire et aucun prompt d’image n’a été utilisé.

### 9. Mouvement

Retours natifs immédiats au survol, au clic, à la sélection et à la validation. Aucune animation en boucle ou déplacement de mise en page. Le thème change sans transition de taille et la vérification périodique ne fait plus clignoter les commandes.

## Vérifications

- Compilation avec le compilateur .NET Framework de Windows, sans NuGet.
- 95 assertions hors matériel : 91 tests de logique et interface, quatre tests du remplacement réel d’un EXE.
- Migration vers le sombre, persistance du clair, contrastes des deux thèmes, conservation/annulation des brouillons et validation des profils.
- Rendus Windows natifs : écran, profils, détection, réglages, thème clair et fenêtre réduite à sa taille minimale. Trois séries de corrections visuelles au maximum par écran ; les captures publiques utilisent des données fictives.
- Cycle sur le G4 réel : jeu Alpha en 17″, priorité sur Beta, passage à Beta en 21,5″, puis restauration du format initial Wide. Le mode initial a été relu après fermeture.
- L’automatisation interactive Computer Use a été bloquée par une erreur d’identité de fenêtre du pilote, persistante après actualisation. Aucun parcours cliqué n’est présenté comme validé par ce pilote ; les tests d’interface utilisent les contrôles WinForms réels.

Les menus et boîtes système de sélection de fichiers restent gérés par Windows. La prise en charge DPI existante est conservée ; cette livraison ne prétend pas certifier toutes les combinaisons de moniteurs et de facteurs d’échelle.
