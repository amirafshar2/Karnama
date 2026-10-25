@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
cd /d "%~dp0"

if not exist "Karnama.sln" (
  echo [کارنما] Karnama.sln پیدا نشد. این فایل را از ریشه‌ی پروژه اجرا کن.
  pause & exit /b 1
)
if not exist ".env" (
  echo [کارنما] فایل .env نیست. یک بار این را بزن:  python tools\new-env.py
  pause & exit /b 1
)
where dotnet >nul 2>&1 || (echo [کارنما] .NET 10 SDK نصب نیست: https://dotnet.microsoft.com/download/dotnet/10.0 & pause & exit /b 1)
where node >nul 2>&1 || (echo [کارنما] Node.js 22 نصب نیست: https://nodejs.org & pause & exit /b 1)

rem نسخه‌ی نمایشی: داده‌ی نمونه + دکمه‌های ورود سریع
set ASPNETCORE_ENVIRONMENT=Development
set Seed__Mode=Demo
set DemoLogin__Enabled=true

echo [1/3] PostgreSQL و Redis ...
docker compose up -d --wait postgres redis || goto :nodocker

echo [2/3] API روی http://localhost:5080 ...
start "Karnama API" cmd /k dotnet run --project src\Karnama.Api --launch-profile http

echo [3/3] وب روی http://localhost:3000 ...
pushd src\karnama-web
if not exist .env.local copy .env.example .env.local >nul
rem بسته‌های جدید هر نسخه (اگر چیزی عوض نشده باشد چند ثانیه بیشتر طول نمی‌کشد)
call npm install --prefer-offline --no-audit --no-fund
start "Karnama Web" cmd /k npm run dev
popd

timeout /t 12 >nul
start http://localhost:3000/login
exit /b 0

:nodocker
echo.
echo Docker اجرا نشد. Docker Desktop را باز کن و دوباره امتحان کن.
echo (اگر PostgreSQL 16 و Redis را خودت نصب کرده‌ای، رشته‌ی اتصال را در .env درست کن و این خط را حذف کن.)
pause
exit /b 1
