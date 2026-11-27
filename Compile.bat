@echo off
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF
set PROJ=src\VTOL_QoL
set ASSETS=%PROJ%\Assets

if not exist "%PROJ%\VTOL_QoL.csproj" (
  echo PROJECT_NOT_FOUND: %PROJ%\VTOL_QoL.csproj
  exit /b 1
)

rem icon.ico нужен рядом с exe (MainForm ищет BaseDirectory\icon.ico)
copy /Y "%PROJ%\icon.ico" . >nul

%CSC% /nologo /utf8output /target:winexe /win32icon:%PROJ%\icon.ico /out:VTOL_QoL.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:System.Management.dll /r:System.Net.Http.dll /r:System.Runtime.Serialization.dll ^
  /r:System.Xaml.dll ^
  /r:%WPF%\PresentationCore.dll /r:%WPF%\PresentationFramework.dll ^
  /r:%WPF%\WindowsBase.dll /r:%WPF%\WindowsFormsIntegration.dll ^
  /resource:%PROJ%\icon.ico,icon.ico ^
  /resource:%ASSETS%\Tools\ffmpeg.exe,ffmpeg.exe ^
  /resource:%ASSETS%\Tools\yt-dlp.exe,yt-dlp.exe ^
  /resource:%ASSETS%\Meshes\FA-26B.fbx,FA-26B.fbx ^
  /resource:%ASSETS%\Meshes\F-45A.fbx,F-45A.fbx ^
  /resource:%ASSETS%\Meshes\AV-42C.fbx,AV-42C.fbx ^
  /resource:%ASSETS%\Meshes\AH-94.fbx,AH-94.fbx ^
  /resource:%ASSETS%\Meshes\T-55.fbx,T-55.fbx ^
  /resource:%ASSETS%\Meshes\EF-24G.fbx,EF-24G.fbx ^
  /resource:%ASSETS%\Sounds\1_kill.mp3,1_kill.mp3 ^
  /resource:%ASSETS%\Sounds\2_doublekill.mp3,2_doublekill.mp3 ^
  /resource:%ASSETS%\Sounds\3_triplekill.mp3,3_triplekill.mp3 ^
  /resource:%ASSETS%\Sounds\4_megakill.mp3,4_megakill.mp3 ^
  /resource:%ASSETS%\Sounds\5_ultrakill.mp3,5_ultrakill.mp3 ^
  /resource:%ASSETS%\Sounds\6_monsterkill.mp3,6_monsterkill.mp3 ^
  /resource:%ASSETS%\Sounds\7_ludicrouskill.mp3,7_ludicrouskill.mp3 ^
  /resource:%ASSETS%\Sounds\8_holyshit.mp3,8_holyshit.mp3 ^
  /resource:%ASSETS%\Sounds\JAMMER_ON.mp3,JAMMER_ON.mp3 ^
  /resource:%ASSETS%\Sounds\JAMMER_CONTINUOUS.mp3,JAMMER_CONTINUOUS.mp3 ^
  %PROJ%\Core\*.cs %PROJ%\Audio\*.cs %PROJ%\Workshop\*.cs ^
  %PROJ%\Viewer\*.cs %PROJ%\App\*.cs %PROJ%\Debrief\*.cs

if errorlevel 1 (
  echo BUILD_FAILED
  exit /b 1
)
echo BUILD_OK
if /I not "%~1"=="nopause" pause
endlocal
