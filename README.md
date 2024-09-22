# UnrealLocresEditor
GUI Application for editing Unreal Engine locres files using Avalonia, CsvHelper & UnrealLocres \
**Make sure to place [UnrealLocres.exe](https://github.com/akintos/UnrealLocres/releases/latest) alongside UnrealLocresEditor.Desktop.exe**

### Installation Instructions (Windows x86/x64):
Download the zip file corresponding to what version you need (x86 for 32-bit systems and x64 for 64-bit systems, if you had a 32-bit system you would probably know that) \
Extract all of it to a folder, "UnrealLocresEditor" \
Download [UnrealLocres](https://github.com/akintos/UnrealLocres/releases/latest). Place the UnrealLocres.exe in the same folder as UnrealLocresEditor.Desktop.exe.

Run UnrealLocresEditor.Desktop.exe. \
Exported locres file will appear in `UnrealLocresEditor/export/<date+time you exported it at>/<name>.locres`

### Installation Instructions (Linux x64 + Wine for UnrealLocres because it has no license):
- Make sure wine is installed.
  - Ubuntu example: sudo apt-get update && sudo apt-get install wine wine32
- Download the .tar file from Github Releases. This only supports Linux x64.
- Right click it in your file manager, click "Extract".
- Download [UnrealLocres](https://github.com/akintos/UnrealLocres/releases/latest), you will notice there is only an .exe file, but we will be using Wine.
- Place the .exe besides the files we extracted earlier.
- Navigate to the extracted files, right click, "Open in terminal". Type: `./UnrealLocresEditor.Dekstop`
- You should now see the application. Click Edit and then "Attempt wine prefix."
- If no errors occur, you should see a notification after a bit. Now, close Unreal Locres Editor and the terminal.
- Open a browser and go to the [wine-mono website](https://dl.winehq.org/wine/wine-mono/), click on the version that supports your Wine version. More than likely, this is the latest version, so scroll to the bottom. Next, download the .msi file inside the directory. (e.g `wine-mono-9.1.0-x86.msi`)
- Place this alongside the files we extracted earlier for now.
- Inside your file manager, in the extracted files' directory, right click and "Open terminal" again. You now want to type/paste: `realpath wineprefix`. Copy the output by selecting it and hitting ctrl+shift+c.
- Now type `WINEPREFIX=` and paste the output you just copied using ctrl+shift+v. Now type: `wine msiexec /i wine-mono-` hit the tab key, and the file name should auto complete. Now hit enter. Wait for it to finish.
- Once it has finished, run `./UnrealLocresEditor.Desktop` in the terminal again. You should now be able to save and export files!
- We no longer need the .msi file that we downloaded, so you can now safely delete it if you want to.
