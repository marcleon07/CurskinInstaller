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
   immediately. Leave "Re-apply at sign-in" off unless the cursors still
   change after a reboot; it is a fallback, not a requirement.
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

Cursor section of the active Windows theme (when Apply is checked), so the
scheme survives a restart without a startup task:

  %LOCALAPPDATA%\Microsoft\Windows\Themes\<current>.theme

Sign-in re-apply (only when both check boxes are checked):

  HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\CurskinInstaller

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

and the cursor section of the active theme to:

  %LOCALAPPDATA%\CurskinInstaller\original-theme.txt

Click "Restore original cursors" to put those settings back. Restoring also
removes the sign-in re-apply entry. Restoring does not remove the installed
schemes; you can still select them later in Mouse Properties.

The backups are deliberately not overwritten by later installs, so they keep
the state from before the first install. If you want a new baseline, delete the
files and apply a scheme again.

Cursors reverting after a restart
---------------------------------
Cause: a Windows theme file (.theme) carries its own [Control Panel\Cursors]
section, and Windows reloads the active theme during logon. That reload writes
the theme's cursor paths into HKEY_CURRENT_USER\Control Panel\Cursors but leaves
the scheme name alone. If the theme still describes the default pointers, Mouse
Properties keeps showing your Curskin scheme while the desktop shows the
default cursors.

What the installer does: when it applies a scheme, it also rewrites the cursor
section of the active theme file to match the scheme, so the next logon reloads
the same cursors. Nothing starts with Windows. Only that one section is
changed; wallpaper, colors, sounds, and icons are untouched.

If the theme file cannot be written, or Windows still overrides the pointers,
turn on "Re-apply at sign-in". That adds one per-user Run entry which runs:

  CurskinInstaller.exe --reapply

It reads the current selection from HKEY_CURRENT_USER\Control Panel\Cursors,
reloads it, and retries several times during the first half minute after
sign-in. It is not a service and does not keep running.

You can run the same repair by hand:

  CurskinInstaller.exe --reapply

Other things to check:
- Turn off fast startup in Control Panel > Power Options.
- If you apply a different Windows theme later, apply the Curskin scheme again
  so the new theme file carries it.
- NWPen, Person, and Pin have no SetSystemCursor slot, so an immediate apply
  relies on the normal Windows cursor reload for those three.
- Set ThemeChangesMousePointers to 0 under
  HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes if you would
  rather have Windows stop taking pointers from themes at all.

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
- If you delete the app without clicking Restore first, also delete the
  CurskinInstaller value under
  HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run.
- The executable is larger than a typical small utility because the AVIF and
  SVG decoders are embedded inside it.

Source and license
------------------
The C# source and build instructions are in the GitHub repository.

Curskin Installer is released under the MIT License. See LICENSE.
The bundled third-party components are covered by their own licenses; see
THIRD-PARTY-NOTICES.txt.
