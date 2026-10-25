@echo off
rem Windows: runs the visual test through Git Bash (installed with Git for Windows).
cd /d "%~dp0\..\.."
where bash >nul 2>&1 || (echo Git Bash پیدا نشد. Git for Windows را نصب کن: https://git-scm.com & pause & exit /b 1)
bash tests/e2e/run-visual.sh %*
