@echo off
:: Build + Test + Pack for all target frameworks
:: Usage: build.cmd [Release|Debug]

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release

echo === Build (%CONFIG%) ===
dotnet build OpenDataGrid.slnx -c %CONFIG%
if errorlevel 1 exit /b %errorlevel%

echo === Tests ===
dotnet test src\Open.DataGrid.Tests\Open.DataGrid.Tests.csproj -c %CONFIG% --no-build
if errorlevel 1 exit /b %errorlevel%

echo === Pack ===
dotnet pack src\Open.DataGrid\Open.DataGrid.csproj -c %CONFIG% --no-build -o artifacts\
if errorlevel 1 exit /b %errorlevel%

echo === Done ===
