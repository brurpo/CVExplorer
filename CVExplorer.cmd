@echo off
rem Duplo clique aqui para compilar e abrir o CV Explorer.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1"
if errorlevel 1 pause
