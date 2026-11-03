@echo off
rem Builds SessionSync.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK or download needed.
rem   build.cmd          -> SessionSync.exe
rem   build.cmd test     -> data-layer tests (temp folders only)
rem   build.cmd uitest   -> end-to-end UI test on demo data, screenshots to docs\screenshots
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll
set COMMON=/nologo /utf8output /codepage:65001 /optimize+ /warn:4 %REFS%

if /i "%1"=="test" (
  "%CSC%" %COMMON% /target:exe /out:test\StoreTests.exe src\Json.cs src\Journal.cs src\Names.cs src\Store.cs src\L.cs test\StoreTests.cs || exit /b 1
  test\StoreTests.exe
  exit /b %errorlevel%
)

if /i "%1"=="uitest" (
  "%CSC%" %COMMON% /target:exe /main:SessionSync.Tests.UiTest /out:test\UiTest.exe /win32manifest:app.manifest src\*.cs test\UiTest.cs || exit /b 1
  test\UiTest.exe docs\screenshots
  exit /b %errorlevel%
)

"%CSC%" %COMMON% /target:winexe /out:SessionSync.exe /win32manifest:app.manifest /win32icon:app.ico src\*.cs || exit /b 1
echo Built SessionSync.exe
