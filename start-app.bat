@echo off
title Time Tracking - Launcher

echo Starting backend and frontend...

start "Backend - dotnet" cmd /k "cd /d %~dp0backend && dotnet run"
start "Frontend - vite" cmd /k "cd /d %~dp0frontend && npm run dev"

echo Both processes launched in separate windows.
