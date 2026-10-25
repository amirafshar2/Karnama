@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
cd /d "%~dp0"

if not exist ".env" (
  echo [کارنما] فایل .env نیست:  python tools\new-env.py
  pause & exit /b 1
)

rem رمز دیتابیس و کد نصب را از .env بخوان
for /f "usebackq tokens=1,* delims==" %%a in (".env") do (
  if "%%a"=="POSTGRES_PASSWORD" set "PGPASS=%%b"
  if "%%a"=="Setup__Token" set "SETUP=%%b"
)

rem نصب خالی، مثل یک مشتری جدید: دیتابیس جدا، بدون داده‌ی نمونه، بدون ورود سریع
set ASPNETCORE_ENVIRONMENT=Development
set Seed__Mode=Empty
set DemoLogin__Enabled=false
set "ConnectionStrings__Default=Host=localhost;Port=5432;Database=karnama_empty;Username=karnama;Password=!PGPASS!"
set Cors__AllowedOrigins__0=http://localhost:3001

docker compose up -d --wait postgres redis || (echo Docker اجرا نشد. & pause & exit /b 1)
docker compose exec -T postgres psql -U karnama -d postgres -tc "SELECT 1 FROM pg_database WHERE datname='karnama_empty'" | findstr 1 >nul || docker compose exec -T postgres psql -U karnama -d postgres -c "CREATE DATABASE karnama_empty"

start "Karnama API (empty)" cmd /k dotnet run --project src\Karnama.Api --launch-profile http --urls http://localhost:5081

pushd src\karnama-web
rem بسته‌های جدید هر نسخه (اگر چیزی عوض نشده باشد چند ثانیه بیشتر طول نمی‌کشد)
call npm install --prefer-offline --no-audit --no-fund
set NEXT_PUBLIC_API_BASE=http://localhost:5081
set NEXT_PUBLIC_HUB_BASE=http://localhost:5081
set NEXT_DIST_DIR=.next-empty
start "Karnama Web (empty)" cmd /k npx next dev -p 3001
popd

echo.
echo کد نصب برای صفحه‌ی /setup:  !SETUP!
timeout /t 12 >nul
start http://localhost:3001/setup
