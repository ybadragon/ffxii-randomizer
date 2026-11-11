@echo off
rem Builds bin\dinput8.dll with the MSVC Build Tools (x64, static CRT so friends need no redistributable).
setlocal
rem ProgramFiles(x86) is not inherited when launched from some shells (e.g. Git Bash).
set "PF86=%SystemDrive%\Program Files (x86)"
for /f "usebackq delims=" %%i in (`"%PF86%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%i"
if not defined VS (echo MSVC build tools not found & exit /b 1)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
cd /d "%~dp0"
if not exist bin mkdir bin
if not exist obj mkdir obj
cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /DUNICODE /D_UNICODE /Foobj\ loader.cpp /LD /link /DEF:dinput8.def /OUT:bin\dinput8.dll /IMPLIB:obj\dinput8.lib || exit /b 1
echo built %~dp0bin\dinput8.dll
