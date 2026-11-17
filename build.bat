@echo off
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:TevkifatHesaplayici.exe /codepage:65001 /utf8output /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll Program.cs
echo Derleme tamamlandi: TevkifatHesaplayici.exe
