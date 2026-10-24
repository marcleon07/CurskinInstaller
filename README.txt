Curskin Installer
=================

A small native Windows app that installs Mouse X .curskin files as real,
per-user Windows cursor schemes. No administrator rights are required.

Quick start
-----------
1. Run CurskinInstaller.exe.
2. Drag a .curskin file onto the window, or click Browse.
3. Select the variants you want.
   If you select two or more, you can also create a Mixed scheme.
4. Leave "Apply after install" checked if you want the new cursors applied
   immediately.
5. Click "Install selected".

The success dialog shows the installed scheme name(s), the scheme that was
applied, and the install folder. The new schemes also appear in:

  Settings > Bluetooth & devices > Mouse > Additional mouse settings > Pointers

The "Mouse settings" button opens that classic Mouse Properties dialog.

Mouse X note
------------
Mouse X can draw its own cursor over the Windows cursor. If Mouse X is
running, quit it (including its tray icon) to see the installed Windows
scheme on the desktop. The app warns you when it detects the Mouse X process.

What gets installed
-------------------
Per-user cursor files:

  %LOCALAPPDATA%\Microsoft\Windows\Cursors\<SkinName>\

Scheme registrations:

  HKEY_CURRENT_USER\Control Panel\Cursors\Schemes

Current cursor selection (when Apply is checked):

  HKEY_CURRENT_USER\Control Panel\Cursors

Everything is per-user. The app never writes to C:\Windows and does not
request elevation.

Supported source images
-----------------------
- GIF (animated or still)
- PNG
- AVIF and animated AVIF/AVIS
- SVG, including layered SVG cursors

Each cursor is converted to a 32x32 Windows .cur or .ani file.
- Bitmap images are scaled with nearest-neighbor sampling.
- Layered SVG is rendered bottom-to-top so the layer order matches Mouse X.
  Masks and transparency are preserved.
- Animation timing comes from the source frames. Frames without timing use
  10 FPS (100 ms per frame).

All 17 Windows cursor roles are installed for every selected variant. If a
skin is missing a role, the app uses its normal Windows fallback (for example,
Crosshair can fall back to the skin's Arrow).

Mixed schemes
-------------
When two or more variants are selected, "Create a Mixed scheme" adds one extra
scheme that assigns the 17 cursor roles round-robin across the selected
variants. For example, the Arrow might come from variant A while the I-beam
comes from variant B. The Mixed scheme reuses the variant files, so it does not
create duplicate cursor files.

If only one variant is selected, the Mixed option is disabled.

Restoring your old cursors
--------------------------
The first time the app applies a scheme, it saves the current cursor settings
to:

  %LOCALAPPDATA%\CurskinInstaller\original-cursors.txt

Click "Restore original cursors" to put those settings back. Restoring does
not remove the installed schemes; you can still select them later in Mouse
Properties.

The backup is deliberately not overwritten by later installs, so it keeps the
state from before the first install. If you want a new baseline, delete that
file and apply a scheme again.

Built-in decoders
-----------------
To keep the app self-contained, the official libavif avifdec.exe and resvg.exe
binaries are embedded in the exe in compressed form. They are extracted only
when an AVIF or SVG cursor needs them, to:

  %LOCALAPPDATA%\CurskinInstaller\tools\

They are conversion helpers, not system components. You can delete that tools
folder when the app is not running; it will be recreated when needed.

See THIRD-PARTY-NOTICES.txt for versions, licenses, and source links. Keep
that file with the exe if you share the app.

Supported Windows
-----------------
64-bit Windows 10 or Windows 11.

Notes
-----
- If a skin has no name in its metadata, the app uses the .curskin file name
  or its parent folder name. This keeps unnamed skins from overwriting each
  other.
- Installed schemes are normal Windows schemes. You can remove them later in
  Mouse Properties or by deleting the corresponding folder and registry value
  under HKEY_CURRENT_USER\Control Panel\Cursors\Schemes.
- The executable is larger than a typical small utility because the AVIF and
  SVG decoders are embedded inside it.

Source and license
------------------
The C# source and build instructions are in the GitHub repository.

Curskin Installer is released under the MIT License. See LICENSE.
The bundled third-party components are covered by their own licenses; see
THIRD-PARTY-NOTICES.txt.
