# ۱۰ اسکیل امنیتی — وب، API و اندروید

> همه‌ی این ریپوها در ۳ آبان ۱۴۰۵ باز و بررسی شدند (SKILL.md و ساختار). مثال‌های خطرناکی مثل `curl | sh` فقط به‌عنوان «الگوی بد» در متن آموزشی بودند، نه دستور اجرایی.
> **قانون:** قبل از نصب هر اسکیل، فایل‌هایش را دوباره بخوان؛ اسکیل می‌تواند اسکریپت اجرا کند. اسکیل‌ها **فقط ابزار بررسی و تست** هستند و منبع UI نیستند (UI فقط 21st.dev).
>
> ⚠️ ریپوی `AlexDevFlow/Claude10XD` که در اسکیل‌های قبلی آمده بود **عمومی نیست یا وجود ندارد**؛ اسکیل‌های ۲، ۴ و ۸ جایگزینش هستند.

## جدول

| # | اسکیل | ریپو | حوزه | کی اجرا شود |
|---|---|---|---|---|
| 1 | **security-review** | `getsentry/skills` | وب + API | آخر هر مرحله، روی کد همان مرحله |
| 2 | **owasp-security** | `agamm/claude-code-owasp` | وب + API (OWASP Top 10:2025، ASVS 5.0) | هنگام نوشتن Auth، آپلود، مجوزها؛ و بررسی آخر هر مرحله |
| 3 | **static-analysis** (codeql + semgrep + sarif-parsing) | `trailofbits/skills` | C#، TypeScript، Kotlin/Java | M1 در CI، بعد در هر PR |
| 4 | **insecure-defaults** | `trailofbits/skills` | تنظیمات و پیش‌فرض‌ها (کلید Fallback، Debug، CORS باز) | M1، M7 و قبل از هر دیپلوی |
| 5 | **supply-chain-risk-auditor** | `trailofbits/skills` | پکیج‌های npm (+ `dotnet list package --vulnerable`) | هر بار افزودن پکیج یا کامپوننت 21st |
| 6 | **differential-review** | `trailofbits/skills` | تغییرات هر PR/کامیت | روی هر PR |
| 7 | **gha-security-review** | `getsentry/skills` | GitHub Actions (CI) | M1 و هر تغییر در `.github/workflows` |
| 8 | **masvs-checklist** + **secure-storage-audit** | `dweinstein/mobile-security-skills` | اندروید: MASVS v2، ذخیره‌ی توکن و داده | M7 (و هر تغییر در Auth موبایل) |
| 9 | **network-security-check** + **auth-assessment** | `dweinstein/mobile-security-skills` | اندروید: TLS، Pinning، Cleartext، نشست | M7 |
| 10 | **performing-android-app-static-analysis-with-mobsf** (+ `testing-websocket-api-security` برای SignalR) | `mukul975/Anthropic-Cybersecurity-Skills` | اسکن APK Release با MobSF؛ تست امنیت WebSocket | M7 و هر نسخه‌ی Release |

## نصب (داخل Claude Code روی کامپیوتر امیر)

```text
# 1, 7 — Sentry
/plugin marketplace add getsentry/skills
/plugin install sentry-skills@sentry-skills

# 2 — OWASP
/plugin marketplace add agamm/claude-code-owasp
/plugin install owasp-security@agamm

# 3, 4, 5, 6 — Trail of Bits (فقط همین چهار پلاگین را نصب کن)
/plugin marketplace add trailofbits/skills
/plugin            # از منو: static-analysis, insecure-defaults, supply-chain-risk-auditor, differential-review
```

```powershell
# 8, 9 — اسکیل‌های موبایل (کپی در پوشه‌ی پروژه)
git clone --depth 1 https://github.com/dweinstein/mobile-security-skills $env:TEMP\mss
mkdir .claude\skills -Force
foreach ($s in "masvs-checklist","secure-storage-audit","network-security-check","auth-assessment") {
  Copy-Item -Recurse "$env:TEMP\mss\skills\$s" ".claude\skills\$s"
}

# 10 — فقط دو اسکیل لازم از این ریپوی بزرگ
git clone --depth 1 https://github.com/mukul975/Anthropic-Cybersecurity-Skills $env:TEMP\acs
Copy-Item -Recurse "$env:TEMP\acs\skills\performing-android-app-static-analysis-with-mobsf" ".claude\skills\"
Copy-Item -Recurse "$env:TEMP\acs\skills\testing-websocket-api-security" ".claude\skills\"
```

**ابزارهای لازم برای این اسکیل‌ها:**
- Semgrep: `pip install semgrep`
- CodeQL CLI (اختیاری)
- MobSF با Docker: `docker run -it --rm -p 8000:8000 opensecurity/mobile-security-framework-mobsf`

## چه چیزی را پوشش می‌دهند

| خطر | اسکیل |
|---|---|
| IDOR (کارگر کار دیگری را ببیند، مشتری سفارش دیگری) | 1، 2 |
| تزریق، XSS در توضیحات پکیج، آپلود فایل | 1، 2، 3 |
| کلید Fallback در کد، Debug در Production، CORS باز | 4 |
| پکیج آلوده‌ی npm (از جمله وابستگی‌های کامپوننت‌های 21st) | 5 |
| تغییر کوچکی که یک باگ امنیتی قدیمی را برگرداند | 6 |
| حمله به CI و دزدی کلید از Actions | 7 |
| ذخیره‌ی ناامن توکن، Backup، لاگ حساس روی گوشی | 8 |
| ترافیک HTTP، Pinning اشتباه، نشست ناامن در اپ | 9 |
| رمز یا کلید داخل APK، مجوزهای اضافه، WebView Debug؛ امنیت اتصال SignalR | 10 |

## خروجی

هر اجرا یک گزارش کوتاه فارسی در `docs/security/YYYY-MM-DD-<skill>.md` با:
- یافته‌ها (فقط با اطمینان بالا)
- شدت
- اصلاح انجام‌شده
- تست اضافه‌شده

یافته‌ی **High/Critical** = مرحله سبز نیست.
