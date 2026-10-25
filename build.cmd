@echo off
rem Builds MomPet.exe with the .NET Framework compiler that ships with Windows.
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /unsafe ^
  /out:"%~dp0MomPet.exe" /win32manifest:"%~dp0src\app.manifest" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  "%~dp0src\*.cs"
