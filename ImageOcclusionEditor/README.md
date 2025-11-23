## Image Occlusion Editor

*Image Occlusion Editor* is companion software to *SuperMemo*. It is meant as an **alternative** to the **built-in Image Occlusion** template, with the advantage of supporting multiple screen resolutions.

![](https://github.com/supermemo/ImageOcclusionEditor/raw/master/Resources/warning_24.png) This software works in tandem with **SuperMemo Assistant**, found [on this same GitHub profile](https://github.com/supermemo/SuperMemoAssistant)

### Table of Content
- [Image Occlusion Editor](#image-occlusion-editor)
  - [Table of Content](#table-of-content)
  - [Screenshots](#screenshots)
  - [Downloads](#downloads)
  - [Information](#information)
  - [Demonstration video](#demonstration-video)
  - [Additional Templates](#additional-templates)
  - [Configuration (Optional)](#configuration-optional)
  - [Special thanks, Credits, Licenses](#special-thanks-credits-licenses)

### Screenshots

Occlusion Editor           |  Occlusion in SuperMemo
:-------------------------:|:-------------------------:
![](https://raw.githubusercontent.com/SuperMemo/ImageOcclusionEditor/master/Resources/ImageOcclusionEditor-v1.0.png)  |  ![](https://github.com/supermemo/ImageOcclusionEditor/raw/master/Resources/ElementWindow.png)

### Downloads

[**All releases**](https://github.com/supermemo/ImageOcclusionEditor/releases)

[**Latest version (installer)**](https://github.com/supermemo/ImageOcclusionEditor/releases/download/1.0/ImageOcclusionEditor_v1.0_Setup.msi)

### Information

**Usage**: `ImageOcclusionEditor.exe <BackgroundFile> <OcclusionFile>`

**Editing occlusions**: The SVG code is embedded inside the PNG Occlusion file, enabling edition of existing occlusions. Simply open them again with Image Occlusion Editing like you normally would.


### Demonstration video
[![Image Occlusion Editor Demonstration](https://img.youtube.com/vi/BJ1ZAYSGJ4M/0.jpg)](https://youtu.be/BJ1ZAYSGJ4M)


### Additional Templates

Occlusion + 1 HTML         |  Occlusion + 2 HTML
:-------------------------:|:-------------------------:
[Download link](https://github.com/supermemo/ImageOcclusionEditor/raw/master/Resources/Template_IIOT.txt)  |  [Download link](https://github.com/supermemo/ImageOcclusionEditor/raw/master/Resources/Template_IIOTT.txt)
![](https://github.com/supermemo/ImageOcclusionEditor/raw/master/Resources/Template_IIOT.png)  |  ![](https://github.com/supermemo/ImageOcclusionEditor/raw/master/Resources/Template_IIOTT.png)

**How to** apply:
1. Copy the template in your Clipboard (Ctrl+C)
2. Open SuperMemo
3. Paste (Ctrl+V) the template
4. Save as Template (Alt+F10 > Template > Save as Template)
5. Apply the template (Ctrl+Shift+M) to your Occlusion Items

### Configuration (Optional)

In **ImageOcclusionEditor.exe.config**:
```
<setting name="StrokeColor" serializeAs="String">
  <value>2D2D2D</value>
</setting>
<setting name="StrokeWidth" serializeAs="String">
  <value>2</value>
</setting>
<setting name="FillColor" serializeAs="String">
  <value>FFEBA2</value>
</setting>
```

* **StrokeColor**: Default occlusion border color
* **StrokeWidth**: Default occlusion border width
* **FillColor**: Default occlusion background color

### Special thanks, Credits, Licenses

This repository currently distributes ImageOcclusionEditorWinUI3 under the GPL-3.0-or-later license. Some files contain code derived from the original ImageOcclusionEditor by the SuperMemo Community (MIT). Attribution is preserved in-file and below.

Credits and acknowledgements (non-exhaustive):

* SuperMemo Community — authors of ImageOcclusionEditor (MIT), from which parts of this project are derived.

Additional acknowledgements (legacy and related work):

* The people of the *SVG-Edit* group for their [SVG editor](https://github.com/SVG-Edit/svgedit) (central piece of ImageOcclusionEditor)
* The people of the *vvvv* group for their [SVG library](https://github.com/vvvv/SVG)
* *Neil Harvey* for his [FileSignature library](https://github.com/neilharvey/FileSignatures)
* *Aristotelis P.* and predecessors for [the original idea](https://github.com/glutanimate/image-occlusion-enhanced), and years of using their Occlusion Addon in the past

Licensing and notices:

- Project license: see `LICENSE` and `LICENSE.GPL3.txt` (GPL-3.0-or-later).
- Attribution for derived MIT code: see `LICENSE.MIT.txt` and in-file notices.
- Aggregated third-party notices: see `THIRD-PARTY-NOTICES.TXT`.

If you believe a required notice is missing or incomplete, please open an issue so we can correct it.
