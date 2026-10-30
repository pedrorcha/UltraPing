@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Nao encontrei o csc.exe do .NET Framework 4.
  pause
  exit /b 1
)
rem gera o UltraPing.exe na pasta de cima (ao lado do config.ini e da pasta Profiles)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:..\UltraPing.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll UltraPing.cs
if errorlevel 1 (
  echo.
  echo ERRO DE COMPILACAO. Copie as mensagens acima e envie para o Claude.
  pause
  exit /b 1
)
echo.
echo Pronto: UltraPing.exe atualizado na pasta do programa.
pause
