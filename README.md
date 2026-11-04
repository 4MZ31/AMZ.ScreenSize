# AMZ.ScreenSize

Choisir la taille d’image de son **Samsung Odyssey G4** depuis Windows et appliquer automatiquement son format préféré au lancement de **Counter-Strike**.

Une application Windows portable, avec une interface en français et une icône dans la zone de notification.

![Interface AMZ.ScreenSize : formats prédéfinis, aperçu et profil Counter-Strike](docs/interface.png)

## Télécharger

Télécharger **[AMZ.ScreenSize.exe dans la dernière release](https://github.com/4MZ31/AMZ.ScreenSize/releases/latest)**, ou l’archive ZIP qui contient aussi la documentation. Ce dépôt est privé : une connexion à un compte GitHub autorisé est nécessaire.

L’exécutable est autonome : aucun script PowerShell ni fichier source n’est nécessaire pour l’utiliser. Il s’appuie sur .NET Framework installé dans Windows.

**Configuration :** Windows 10/11 64 bits avec .NET Framework 4.8 ou supérieur. La configuration vérifiée est un Samsung Odyssey G4 **S27BG400EU / SAM727E**, connecté en **DisplayPort**, avec une AMD Radeon RX 7800 XT. Les autres modèles et les connexions HDMI n’ont pas été validés.

## Utiliser l’application

1. Ouvrir `AMZ.ScreenSize.exe`.
2. Choisir un format dans la liste de gauche ; l’aperçu montre la zone d’image attendue.
3. Cliquer sur **Appliquer maintenant** pour l’utiliser immédiatement, ou sur **Utiliser pour CS** pour l’enregistrer comme profil Counter-Strike.
4. Cocher **Changer le format au lancement du jeu** pour activer l’automatisme.

Les huit préréglages du G4 sont disponibles :

- **Auto** — conserve le format de la source.
- **Wide** — utilise toute la surface de l’écran.
- **17″ (4:3)**.
- **19″ (4:3)**.
- **19″ Wide (16:10)**.
- **21,5″ Wide (16:9)**.
- **22″ Wide (16:10)**.
- **23″ Wide (16:9)**.

Il s’agit des réglages **Taille de l’écran / Screen Size** du moniteur. L’application ne change pas la définition Windows, par exemple 1920 × 1080, ni la résolution choisie dans le jeu. L’aperçu est indicatif : le signal vidéo et les options du moniteur influencent le rendu.

### Counter-Strike et restauration

L’application détecte `cs2.exe` et `csgo.exe` toutes les deux secondes. Elle mémorise le format courant **avant** d’appliquer le profil enregistré.

Le format précédent est restauré à la fermeture du jeu, à la désactivation de l’automatisme et avec **Quitter et restaurer l’écran**. Modifier le profil pendant une partie applique le nouveau format tout en conservant le format d’origine à restaurer.

Pendant une partie gérée automatiquement, l’application manuelle est désactivée. Désactiver l’automatisme permet de reprendre le contrôle manuel.

Les informations de restauration sont sauvegardées sur disque avant tout changement. Une interruption du programme peut ainsi être récupérée au lancement suivant. Si le moniteur est débranché ou en veille, l’application réessaie après dix secondes.

### Zone de notification

Fermer ou réduire la fenêtre conserve l’application en arrière-plan. Utiliser l’icône d’écran dans la zone de notification, accessible via la flèche **^** si elle est masquée :

- **Double-clic :** rouvrir l’interface.
- **Clic droit :** activer/désactiver l’automatisme, choisir le profil Counter-Strike ou quitter proprement.
- **Icône verte :** automatisme actif ; **icône grise :** désactivé.

Cocher **Lancer avec Windows** pour démarrer en arrière-plan avec la session. Conserver l’exécutable à son emplacement après avoir activé cette option ; la décocher puis la recocher actualise le chemin si le fichier a été déplacé.

## Compatibilité et diagnostic

Le moniteur est identifié par son ID matériel `SAM727E`, indépendamment du numéro d’écran Windows. L’application refuse de sélectionner arbitrairement un écran si plusieurs modèles identiques sont connectés. Elle ne commande pas les autres moniteurs.

Les huit codes ont été appliqués et relus sur le S27BG400EU de test. Le mode initial a été restauré après les essais. Un aller-retour automatique avec un processus de test a également été vérifié.

Le réglage peut être indisponible selon le signal, le mode PC/AV et les options **Adaptive-Sync / FreeSync Premium** ou **Ultrawide Game View**. Si le moniteur refuse un changement, vérifier d’abord sa disponibilité dans le menu physique de l’écran. Aucune autre option du moniteur n’est modifiée automatiquement.

Les préférences et le journal local sont enregistrés ici :

```text
%LOCALAPPDATA%\AMZ.ScreenSize\settings.json
%LOCALAPPDATA%\AMZ.ScreenSize\activity.log
```

Le bouton **Journal** ouvre le journal local. L’application ne collecte ni ne transmet de télémétrie, n’injecte aucun code dans le jeu et n’a besoin d’aucun accès réseau.

Deux commandes de diagnostic sont disponibles :

```powershell
# Exporter le format actuel, sans le modifier.
.\AMZ.ScreenSize.exe --diagnostic "$env:TEMP\screen-size.json"

# Demander la fermeture de l’instance existante avec restauration.
.\AMZ.ScreenSize.exe --quit
```

L’exécutable de cette version n’est pas signé numériquement. Les empreintes SHA-256 des fichiers distribués sont fournies dans `SHA256SUMS.txt` avec chaque release.

## Compiler et tester

Le projet utilise **C# et Windows Forms**, sans dépendance NuGet. Le script de compilation utilise le compilateur .NET Framework présent sous Windows 64 bits.

```powershell
git clone https://github.com/4MZ31/AMZ.ScreenSize.git
cd AMZ.ScreenSize
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -Package
```

- Exécutable : `bin\Release\AMZ.ScreenSize.exe`.
- Tests : `bin\Release\AMZ.ScreenSize.Tests.exe`.
- Fichiers distribuables : `dist\`.

Les **30 assertions hors matériel** vérifient notamment la restauration, le changement de profil en cours de partie, les préférences persistantes, les échecs DDC/CI, la déconnexion et les erreurs d’écriture disque. Les workflows GitHub exécutent uniquement ces tests, sans écran physique.

Pour lancer volontairement le test matériel sur un G4 compatible, hors partie :

```powershell
# L’écran changera brièvement de format. Le mode initial est restauré à la fin.
.\bin\Release\AMZ.ScreenSize.Tests.exe --hardware
```

Ce test ajoute **huit assertions** de relecture, une par préréglage. Ne pas le lancer en même temps qu’un autre utilitaire de contrôle du moniteur.

### Organisation des sources

- `src/AMZ.ScreenSize/` : interface, contrôle du moniteur, automatisme et préférences.
- `tests/Tests.cs` : tests de cycle de vie, de persistance et tests matériels explicites.
- `build.ps1` : compilation, tests et création des archives et empreintes.
- `tools/New-AppIcon.ps1` : génération de l’icône du projet.
- `.github/workflows/` : intégration continue et publication des releases.

### Créer une release

1. Mettre à jour `VERSION`, `CHANGELOG.md` et les notes dans `docs/releases/`.
2. Vérifier `build.ps1 -Test -Package`.
3. Pousser un tag correspondant au fichier `VERSION`, par exemple `v1.0.1`.

Le workflow de release recompile et teste le projet sous Windows, puis publie l’exécutable, le ZIP et `SHA256SUMS.txt`. La release reste privée, comme le dépôt.

## Protocole et sources

La taille d’image est lue et écrite via l’API Windows de configuration des moniteurs et le code VCP **`0xDB`**, absent de la chaîne de capacités annoncée par le G4. Le code standard `0x86` ne fonctionne pas sur l’exemplaire testé.

La correspondance utilisée est `0=Auto`, `1=Wide`, `2=17″ 4:3`, `3=19″ 4:3`, `4=19″ 16:10`, `5=21,5″ 16:9`, `6=22″ 16:10`, `7=23″ 16:9`. Les valeurs 8 et 9 présentes sur certains autres Odyssey ne sont pas proposées sur ce modèle.

- [Samsung : assistance et manuel du S27BG400EU](https://www.samsung.com/fr/support/model/LS27BG400EUXEN/).
- [Microsoft : lecture des paramètres VCP](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-getvcpfeatureandvcpfeaturereply).
- [ddccontrol-db : correspondance Screen Size des Samsung Odyssey](https://github.com/ddccontrol/ddccontrol-db/blob/master/db/monitor/SAM7476.xml), utilisée comme point de départ puis vérifiée par écriture/relecture sur le G4.

## Licence

[MIT](LICENSE) — © 2026 4MZ31. Projet indépendant, sans affiliation avec Samsung ou Valve.
