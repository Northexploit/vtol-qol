@echo off
set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set WPF="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
%CSC% /utf8output /target:winexe /win32icon:icon.ico /out:VTOL_QoL.exe ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.IO.Compression.dll ^
  /r:System.IO.Compression.FileSystem.dll ^
  /r:System.Xaml.dll ^
  /r:%WPF%\PresentationCore.dll ^
  /r:%WPF%\PresentationFramework.dll ^
  /r:%WPF%\WindowsBase.dll ^
  /r:%WPF%\WindowsFormsIntegration.dll ^
  /resource:icon.ico ^
  /resource:ffmpeg.exe ^
  /resource:yt-dlp.exe ^
  /resource:FA-26B.fbx ^
  /resource:F-45A.fbx ^
  /resource:AV-42C.fbx ^
  /resource:AH-94.fbx ^
  /resource:T-55.fbx ^
  /resource:EF-24G.fbx ^
  /resource:1_kill.mp3 ^
  /resource:2_doublekill.mp3 ^
  /resource:3_triplekill.mp3 ^
  /resource:4_megakill.mp3 ^
  /resource:5_ultrakill.mp3 ^
  /resource:6_monsterkill.mp3 ^
  /resource:7_ludicrouskill.mp3 ^
  /resource:8_holyshit.mp3 ^
  src\*.cs

pause
