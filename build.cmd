@echo off
rem Builds DisplayShot.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK or Visual Studio needed. Output: bin\DisplayShot.exe (single file, assets embedded).
setlocal enabledelayedexpansion
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin

set RSP=bin\build.rsp
> "%RSP%" echo -nologo -target:winexe -platform:anycpu -optimize+ -utf8output
>>"%RSP%" echo -win32manifest:src\app.manifest -win32icon:assets\app.ico
>>"%RSP%" echo -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll
>>"%RSP%" echo -out:bin\DisplayShot.exe
for %%f in (assets\*.png) do >>"%RSP%" echo "-resource:%%f,%%~nxf"
for %%f in (src\*.cs) do >>"%RSP%" echo "%%f"

"%CSC%" @"%RSP%"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built bin\DisplayShot.exe
