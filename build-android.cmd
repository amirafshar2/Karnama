@echo off
chcp 65001 >nul
rem ساخت اپ اندروید: وب را برای اپ می‌سازد، داخل پروژه‌ی اندروید کپی می‌کند و Android Studio را باز می‌کند.
rem آدرس API سرور را اینجا بگذارید (برای امولاتور روی همین کامپیوتر: http://10.0.2.2:5080)
if "%KARNAMA_API%"=="" set KARNAMA_API=http://10.0.2.2:5080
cd /d "%~dp0"
pushd src\karnama-web
call npm install --prefer-offline --no-audit --no-fund || goto :error
set BUILD_TARGET=mobile
set NEXT_PUBLIC_CLIENT=android
set NEXT_PUBLIC_API_BASE=%KARNAMA_API%
set NEXT_PUBLIC_HUB_BASE=%KARNAMA_API%
call npx next build || goto :error
popd
pushd mobile
call npm install --prefer-offline --no-audit --no-fund || goto :error
call npx cap sync android || goto :error
call npx cap open android
popd
echo.
echo اندروید استودیو باز شد: Run (فلش سبز) برای گوشی/امولاتور، یا Build ^> Generate Signed App Bundle/APK برای انتشار.
exit /b 0
:error
echo خطا در ساخت. پیام بالا را ببینید.
pause
exit /b 1
