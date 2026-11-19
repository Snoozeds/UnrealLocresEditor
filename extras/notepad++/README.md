# Notepad++ support for .loctxt translation files

Two optional files that make `.loctxt` translation files easier to read and edit in [Notepad++](https://notepad-plus-plus.org/). Only files with the `.loctxt` extension are affected; every other file keeps its usual display.

## 1. Highlighting (`Locres_translations_UDL.xml`)

1. In Notepad++: **Language > User Defined Language > Define your language...**
2. Click **Import...** and choose `Locres_translations_UDL.xml`.
3. Restart Notepad++.

Every `.loctxt` file now opens with the "Locres translations" language:

- `@@ Namespace/Key` lines in bold blue,
- `@@# ...` comment lines in bold orange,
- the texts themselves unchanged.

To change the colours, open the same dialog, select "Locres translations", then **Comment & Number** (comments) or **Operators & Delimiters > Delimiter 1** (keys) and click **Styler**.

## 2. Key list (`loctxt.xml`, optional)

Lists every comment and key in the **Function List** panel; a click jumps to that text.

1. Close Notepad++.
2. Copy `loctxt.xml` into `%AppData%\Notepad++\functionList`
   (portable or local installs: the `functionList` folder next to `notepad++.exe`).
3. In `overrideMap.xml` from that same folder, add this line just before `</associationMap>`:

   ```xml
   <association id="loctxt.xml" userDefinedLangName="Locres translations"/>
   ```

4. Start Notepad++, open a `.loctxt` file, then **View > Function List**.

## Tip

**View > Word wrap** wraps long lines on screen only; the file itself is not changed.
