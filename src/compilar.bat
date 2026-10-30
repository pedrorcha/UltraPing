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
set "REFS=/reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll"
rem gera o UltraPing.exe e o UltraPingViewer.exe na pasta de cima (ao lado do config.ini e da pasta Profiles)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:..\assets\UltraPing.ico /out:..\UltraPing.exe %REFS% UltraPing.cs
if errorlevel 1 goto erro
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:..\assets\UltraPingViewer.ico /out:..\UltraPingViewer.exe %REFS% /reference:System.Data.dll UltraPingViewer.cs
if errorlevel 1 goto erro
echo.
echo Pronto: UltraPing.exe e UltraPingViewer.exe atualizados na pasta do programa.
pause
exit /b 0

:erro
echo.
echo ERRO DE COMPILACAO. Copie as mensagens acima e envie para o Claude.
pause
exit /b 1
