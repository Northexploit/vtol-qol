@echo off
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:icon.ico /out:VTOL_QoL.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:ffmpeg.exe /resource:yt-dlp.exe VTOL_QoL.cs
pause
