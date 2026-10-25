@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

if not exist "Karnama.sln" (
  echo [کارنما] هنوز کدی ساخته نشده. اول مرحله‌ی M1 را با Claude Code بساز.
  pause
  exit /b 1
)

rem نصب خالی، مثل یک مشتری جدید: دیتابیس جدا، بدون داده‌ی دمو، بدون ورود سریع
set Seed__Mode=Empty
set DemoLogin__Enabled=false
set ConnectionStrings__Default=Host=localhost;Port=5432;Database=karnama_empty;Username=karnama;Password=%POSTGRES_PASSWORD%

docker compose up -d postgres redis || (echo Docker اجرا نشد. & pause & exit /b 1)
start "Karnama API (empty)" cmd /k dotnet run --project src\Karnama.Api --urls http://localhost:5081

pushd src\karnama-web
if not exist node_modules call npm install
set NEXT_PUBLIC_API_BASE=http://localhost:5081
set NEXT_PUBLIC_HUB_BASE=http://localhost:5081
start "Karnama Web (empty)" cmd /k npm run dev -- -p 3001
popd

timeout /t 8 >nul
start http://localhost:3001/setup
