@echo off
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find the .NET Framework C# compiler.
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ ^
    /out:CurskinInstaller.exe ^
    /win32manifest:app.manifest ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /resource:avifdec.exe.gz,avifdec.exe.gz ^
    /resource:resvg.exe.gz,resvg.exe.gz ^
    Core.cs CursorBuilder.cs ExternalTools.cs Installer.cs MainForm.cs Properties\AssemblyInfo.cs

if errorlevel 1 (
    echo.
    echo Build failed.
    exit /b 1
)

echo.
echo Built CurskinInstaller.exe
exit /b 0
