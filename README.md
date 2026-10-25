# کارنما — سیستم مدیریت کار آژانس سوشال مدیا

وب و اپ اندروید برای مدیریت سفارش مشتری‌ها، کارهای کارگرها، ساعت کار، غیبت و مرخصی، پکیج‌های فروش و انتشار محتوا — همه لایو.

> **وضعیت:** مرحله‌ی **M1** ساخته و تست شده (ورود OTP، ۲FA مالک، نقش‌های دینامیک، چندزبانه، راه‌اندازی اولیه). نقشه: `docs/PROJECT_MAP.md`.

## شروع (یک بار)

1. **پیش‌نیازها روی ویندوز:**
   - .NET 10 SDK (LTS) — .NET 9 تا ۲۰ آبان ۱۴۰۵ پشتیبانی دارد، پس از 10 استفاده می‌کنیم
   - Node.js 22
   - Docker Desktop (برای PostgreSQL 16 و Redis)
   - Git for Windows (برای Git Bash و تست تصویری)
   - Python 3 (برای ساخت نقشه)
   - Android Studio (نصب است)
2. پوشه را جایی مثل `C:\xampp\htdocs\karnama` بگذار و داخلش `git init` بزن.
3. فایل تنظیمات را با کلیدهای تصادفی بساز:  `python tools\new-env.py`  (کد نصب `/setup` را چاپ می‌کند)
4. **اجرا:** `start-dev.cmd` (دمو با دکمه‌های ورود سریع) یا `start-empty.cmd` (نصب خالی مثل مشتری جدید → `/setup`).
   - در حالت دمو، کد پیامکی در پنجره‌ی API چاپ می‌شود (`[DEV SMS]`).
5. **تست‌ها:**
   - `dotnet test` (اگر Docker نداری: `set KARNAMA_TEST_PG=Host=localhost;Username=karnama;Password=...`)
   - تست تصویری: `tests\e2e\run-visual.cmd` → عکس‌ها در `tests\e2e\screenshots` (اول یک بار: `cd tests\e2e && npm ci && npx playwright install chromium`)
6. برای ادامه، Claude Code را در همین پوشه باز کن و بگو: «مرحله‌ی بعد» (M2).

## نقشه‌ی پروژه
```powershell
python tools\build_map.py
start docs\PROJECT_MAP.html
```

## پوشه‌ها
| پوشه | چیست |
|---|---|
| `docs/` | پرامپت، استانداردها، اسکیل‌های امنیتی، نقشه، ثبت کامپوننت‌های 21st و کانوا |
| `tools/` | ساخت نسخه‌ی HTML نقشه (آفلاین) |
| `src/` | بک‌اند .NET و فرانت Next.js (از M1) |
| `mobile/` | اپ اندروید Capacitor برای Android Studio (از M7) |
| `deploy/` | Docker Compose سرور ایران و Nginx (از M7) |
| `tests/` | تست‌های واحد، یکپارچه، تصویری و بار |
