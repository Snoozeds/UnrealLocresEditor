![A slightly rounded but rectangular logo with a dark background, including a crossed hammer and wrench with a vibrant, rainbow gradient. The text is positioned to the right of the tools, with "Unreal" in bold red, "Locres" in bold green, and "Editor" in bold blue, each stacked vertically.](/wiki/assets/img/ULE-banner-rounded.png)

A GUI tool to edit Unreal Engine localisation files using [UnrealLocres](https://github.com/akintos/UnrealLocres). \
<br />
[![Github Sponsors Badge](https://img.shields.io/badge/Github%20Sponsors-2f3742?style=for-the-badge&logo=github-sponsors&logoColor=c96198)](https://github.com/sponsors/snoozeds) [![Ko-fi Badge](https://img.shields.io/badge/Ko--fi-F16061?style=for-the-badge&logo=ko-fi&logoColor=white)](https://ko-fi.com/snoozeds) [![Patreon](https://img.shields.io/badge/Patreon-F96854?style=for-the-badge&logo=patreon&logoColor=white)](https://patreon.com/snoozeds)


## Previews:
| | | |
|:-------------------------:|:-------------------------:|:-------------------------:|
|<img width="1604" src="/wiki/assets/img/preview1.png">  |  <img width="1604" src="/wiki/assets/img/preview2.png">|<img width="1604" src="/wiki/assets/img/preview3.png">|
|<img width="1604" src="/wiki/assets/img/preview4.png">  |  <img width="1604" src="/wiki/assets/img/preview5.png">|<img width="1604" src="/wiki/assets/img/preview6.png">|

## Installation Instructions (Windows x86/x64):
1. Download the appropriate zip file (usually UnrealLocresEditor-x.x.x-win-x86.zip).
2. Extract all files to a folder.
3. Double-click on UnrealLocresEditor.Desktop.exe to run the application.

Exported locres files will be saved in the following directory: \
`UnrealLocresEditor/export/<date_time>/<name>.locres`

## Installation Instructions (Linux x64 + Wine for UnrealLocres because it has no license):

#### Prerequisites:
- Wine must be installed.
  - Ubuntu example: `sudo apt-get update && sudo apt-get install wine wine32`
<hr>

1. Download the .tar file from Github Releases. This only supports Linux x64.
2. Right click it in your file manager, click "Extract".
3. Download [UnrealLocres](https://github.com/akintos/UnrealLocres/releases/latest), you will notice there is only an .exe file, but we will be using Wine.
4. Place the .exe besides the files we extracted earlier.
5. Navigate to the extracted files, right click, "Open in terminal". Type: `./UnrealLocresEditor.Dekstop`
6. You should now see the application. Click Edit and then "Attempt wine prefix."
7. If no errors occur, you should see a notification after a bit. Now, close Unreal Locres Editor and the terminal.
8. Open a browser and go to the [wine-mono website](https://dl.winehq.org/wine/wine-mono/), click on the version that supports your Wine version. More than likely, this is the latest version, so scroll to the bottom. Next, download the .msi file inside the directory. (e.g `wine-mono-9.1.0-x86.msi`)
9. Place this alongside the files we extracted earlier for now.
10. Inside your file manager, in the extracted files' directory, right click and "Open terminal" again. You now want to type/paste: `realpath wineprefix`. Copy the output by selecting it and hitting ctrl+shift+c.
11. Now type `WINEPREFIX=` and paste the output you just copied using ctrl+shift+v. Now type: `wine msiexec /i wine-mono-` hit the tab key, and the file name should auto complete. Now hit enter. Wait for it to finish.
12. Once it has finished, run `./UnrealLocresEditor.Desktop` in the terminal again. You should now be able to save and export files!
13. We no longer need the .msi file that we downloaded, so you can now safely delete it if you want to.
