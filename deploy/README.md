# دیپلوی روی سرور ایران

> یک سرور لینوکس (Ubuntu 24.04، حداقل ۴ هسته و ۸ گیگ رم، ۱۰۰ گیگ دیسک) با Docker. همه‌چیز داخل همین سرور: دیتابیس، کش، API، Worker، سایت و TLS.

## یک بار

1. Docker و پلاگین compose را نصب کنید. چون Docker Hub از ایران گاهی بسته است، میرور داخلی بگذارید (مثلاً `docker.arvancloud.ir`) در `/etc/docker/daemon.json`:
   `{ "registry-mirrors": ["https://docker.arvancloud.ir"] }`
2. کد را در `/opt/karnama` بگذارید.
3. رمزها: `python3 tools/new-env.py --prod` → `deploy/.env.prod` ساخته می‌شود؛ دامنه‌ها و کلید پیامک/بله را پر کنید؛ `chmod 600 deploy/.env.prod`.
4. DNS: `example.ir` و `api.example.ir` به IP سرور (یا پشت CDN ابر آروان با «دسترسی بین‌الملل» روشن تا گوگل سایت را ببیند).
5. اجرا:
   `docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod up -d --build`
6. باز کردن `https://example.ir/setup` و وارد کردن «Setup token» که مرحله‌ی ۳ نشان داد → ساختن حساب مالک.
7. بکاپ: `crontab -e`
   `15 3 * * * /opt/karnama/deploy/backup.sh >> /var/log/karnama-backup.log 2>&1`
   `0 4 1 * * /opt/karnama/deploy/restore-test.sh >> /var/log/karnama-backup.log 2>&1`
   پوشه‌ی `deploy/backups` را هر هفته به جای دیگری (فضای ابری یا هارد بیرونی) کپی کنید.

## هر نسخه‌ی تازه

1. روی کامپیوتر توسعه: `dotnet ef migrations script --idempotent -p src/Karnama.Infrastructure -s src/Karnama.Infrastructure -o deploy/migrations.sql` (فایل بازبینی و کامیت می‌شود).
2. روی سرور: `git pull` و همان دستور `up -d --build`. سرویس `migrate` اول اسکریپت را اجرا می‌کند و بعد API بالا می‌آید (مهاجرت خودکار در Production خاموش است).

## چه چیزی کجاست

| سرویس | توضیح |
|---|---|
| `caddy` | HTTPS خودکار، HTTP/3، فشرده‌سازی؛ `example.ir` → سایت، `api.example.ir` → API |
| `web` | Next.js (سایت سئوشده + پنل) |
| `api` | ASP.NET Core؛ فایل‌ها در volume `storage`؛ فقط‌خواندنی جز `/data/storage` و `/tmp` |
| `worker` | کارهای زمان‌بندی‌شده (غیبت، انتشار، انقضای پکیج، بلاگ زمان‌بندی‌شده) و ارسال اعلان‌ها |
| `postgres`, `redis` | بیرون در دسترس نیستند (پورت باز نمی‌شود) |

## بعد از دیپلوی

- `https://example.ir/sitemap.xml` را در Google Search Console ثبت کنید (`docs/SEO.md`).
- وبهوک بله: `https://tapi.bale.ai/bot<TOKEN>/setWebhook?url=https://api.example.ir/api/v1/bots/bale/<BALE_WEBHOOK_SECRET>`
- تست بار روی نسخه‌ی آزمایشی (نه Production): `k6 run -e API=... -e SITE=... tests/load/k6-1000.js`
