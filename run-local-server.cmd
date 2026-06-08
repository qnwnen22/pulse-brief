@echo off
cd /d "%~dp0"
dotnet run --urls http://localhost:4000 >> server.out.log 2>> server.err.log
