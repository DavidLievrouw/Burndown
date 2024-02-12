@echo off
cls
SET DIR=%~dp0%
SET SRCDIR=.\src
SET DISTDIR=%DIR%\dist
SET PRODUCT=Burndown

IF EXIST %DISTDIR% RD /S /Q %DISTDIR%

dotnet.exe publish %SRCDIR%\%PRODUCT%\%PRODUCT%.csproj --configuration Release --force --output %DISTDIR%\IIS --self-contained false --runtime win-x64 -p:EnvironmentName=Production
dotnet.exe publish %SRCDIR%\%PRODUCT%\%PRODUCT%.csproj --configuration Release --force --output %DISTDIR%\Container --self-contained true --runtime linux-x64 -p:EnvironmentName=Production
