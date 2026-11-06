@echo off
title Creando PuntoDeVenta.exe
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo No se encontro el compilador de C# de Windows ^(.NET Framework 4^).
  pause
  exit /b 1
)
echo Compilando...
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:PuntoDeVenta.exe /r:System.dll /r:System.Data.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll Program.cs
if exist PuntoDeVenta.exe (
  echo.
  echo LISTO: PuntoDeVenta.exe fue creado en esta carpeta.
) else (
  echo Ocurrio un error al compilar. Copie el mensaje de arriba y envielo.
)
pause
