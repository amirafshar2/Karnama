@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

if not exist "Karnama.sln" (
  echo [کارنما] هنوز کدی ساخته نشده. اول مرحله‌ی M1 را با Claude Code بساز.
  echo راهنما: README.md
  pause
  exit /b 1
)

if not exist ".env" (
  echo [کارنما] فایل .env نیست. .env.example را به .env کپی کن و پر کن.
  pause
  exit /b 1
)

set Seed__Mode=Demo
set DemoLogin__Enabled=true

echo [1/3] PostgreSQL و Redis ...
docker compose up -d postgres redis || goto :fail

echo [2/3] API روی http://localhost:5080 ...
start "Karnama API" cmd /k dotnet run --project src\Karnama.Api --urls http://localhost:5080

echo [3/3] وب روی http://localhost:3000 ...
pushd src\karnama-web
if not exist node_modules call npm install
start "Karnama Web" cmd /k npm run dev
popd

timeout /t 8 >nul
start http://localhost:3000/login
exit /b 0

:fail
echo Docker اجرا نشد. Docker Desktop را باز کن و دوباره امتحان کن.
pause
exit /b 1
