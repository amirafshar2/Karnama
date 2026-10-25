# 📘 همه‌ی اسکیل‌ها و استانداردهای امیر برای ساخت سایت و اپ مشتری

> این فایل را به هر AI کدنویس بده تا لازم نباشد دوباره توضیح بدهی.
> مخصوص یک پروژه نیست: برای هر سایت یا اپ مشتری (طلافروشی، گالری ماشین، آژانس سوشال مدیا، هر کسب‌وکار دیگر) به کار می‌رود.
> هر جا «طلا» آمده، نمونه‌ی همان پروژه است. برای کسب‌وکار دیگر، منطق دامنه‌ی خودش جایش می‌نشیند.
> **به‌روزرسانی ۳ آبان ۱۴۰۵:** بخش ۷ (۱۰ اسکیل امنیتی بررسی‌شده)، بخش ۱۰ (اپ اندروید)، بخش ۱۱ (۱۰۰۰ کاربر و اینترنت ضعیف)، بخش ۱۲ (نقش، زبان و CRUD دینامیک).

---

## ۰. قوانین پایه (همیشه)

1. **زبان:** همه‌ی جواب‌ها و توضیح‌ها به فارسی. رابط کاربری فارسی و RTL. کد، نام متغیرها و کامنت‌ها انگلیسی.
2. **هیچ امضای AI در گیت:** هیچ `Co-Authored-By`، هیچ نام Claude یا AI در کامیت، PR یا فایل‌ها.
3. **هیچ کلید یا رمزی داخل کد:** همه در User Secrets یا متغیر محیطی. یک `.env.example` یا `appsettings.json` با مقدار خالی کافی است. اگر کلید لازم خالی بود، برنامه با پیام روشن اجرا نشود (fail-fast).
4. **تیم سه‌نفره:** قبل از هر تصمیم مهم سه نقش نظر می‌دهند:
   - **مدیر فروش/عملیات:** هر صفحه یک هدف دارد (فروش، یا کوتاه‌کردن کار روزانه).
   - **مدیر طراحی:** حس «واو» در ۵ ثانیه‌ی اول، ولی سریع روی موبایل و اینترنت ضعیف ایران.
   - **مدیر فنی:** امن، تمیز، تست‌پذیر و قابل فروش به مشتری‌های بعدی.
5. **قیمت و پیشنهاد فروش هرگز داخل ریپو نرود.**

---

## ۱. روند کار (به همین ترتیب)

1. **تحقیق:** سایت‌ها و پیج‌های واقعی رقبا در همان صنف را ببین. ایده‌ی ساختار بگیر، کپی نکن.
2. **تحلیل مشتری:** پیج اینستاگرام مشتری را بررسی کن. هر هایلایت یک صفحه یا ماژول اجباری می‌شود (جدول «هایلایت → صفحه»). بعد نیازهای کشف‌شده (Pain Points) را بنویس.
3. **نقشه‌ی کار تصویری (`docs/PROJECT_MAP.md` + `.html`):** وضعیت مراحل، فهرست فایل‌ها، دیاگرام‌ها (معماری، ERD، ماشین‌های وضعیت، نقشه‌ی صفحات)، لاگ تصمیم‌ها، قدم بعدی و سؤال‌های باز.
   - **قانون:** هر جلسه **فقط نقشه** خوانده شود، نه کل پروژه.
   - بعد از هر کار، نقشه به‌روز شود و `python tools/build_map.py` نسخه‌ی HTML را بسازد.
   - فایل `CLAUDE.md` در ریشه همین قانون را به AI می‌گوید.
4. **ساخت مرحله‌به‌مرحله:** کد → تست → build سبز → تست تصویری → به‌روزرسانی نقشه. تا مرحله سبز نشده، مرحله‌ی بعد شروع نشود.
5. **تحویل:** خلاصه‌ی کوتاه فارسی، فایل‌ها در پوشه‌ی پروژه، و دستور اجرای یک‌کلیکی ویندوز (`start-dev.cmd`).

---

## ۲. بک‌اند — `backend-dotnet-skills-bundle` (اجباری)

همراهش Skills گیت‌هاب: `Aaronontheweb/dotnet-skills` (`efcore-patterns`، `database-performance`، `csharp-concurrency-patterns`، `csharp-api-design`، `testcontainers`، `microsoft-extensions-configuration`، `microsoft-extensions-dependency-injection`، `opentelementry-dotnet-instrumentation`). قبل از نصب هر Skill، فایل‌هایش را بخوان.

| # | اسکیل | قوانین |
|---|---|---|
| 1 | **معماری تمیز** | چهار لایه‌ی Domain / Application / Infrastructure / Api؛ وابستگی فقط به داخل. DTO با `record`؛ Entity هرگز بیرون نرود. خطای کسب‌وکار با `Result<T>`. `CancellationToken` همه‌جا. قانون دامنه داخل Entity. ماشین وضعیت صریح؛ انتقال غیرمجاز = خطا. |
| 2 | **امنیت** | JWT کوتاه‌عمر (۱۵ دقیقه) + Refresh Token در کوکی `HttpOnly; Secure; SameSite=Strict` با چرخش و تشخیص استفاده‌ی مجدد (ابطال کل خانواده). **اپ موبایل:** Refresh Token در Secure Storage (Android Keystore)، نه کوکی. CORS فقط Whitelist. RateLimiter روی OTP، ورود، پرداخت، آپلود و فرم‌ها. Authorization مبتنی بر Policy؛ جلوگیری از IDOR با `Guid` و فیلتر مالک. هدرهای امنیتی، ۲FA ادمین، رمزنگاری داده‌ی حساس. |
| 3 | **EF Core** | `AsNoTracking` + Projection، بدون N+1، صفحه‌بندی سمت سرور (Keyset برای لیست‌های بزرگ). Fluent API و ایندکس. همزمانی خوش‌بینانه روی هر چیز فروختنی/محدود (RowVersion/xmin → 409). تضمین دیتابیسی با Unique Filtered Index. پول `long` به ریال، وزن `long` به میلی‌گرم، مدت `long` به ثانیه، درصد `int` به basis point — **هرگز double**. زمان `timestamptz` UTC. Soft Delete، فیلد Audit، جدول‌های Append-only. در Production فقط Migration بازبینی‌شده. |
| 4 | **REST** | `/api/v1/...` جمع؛ کدهای وضعیت درست (`201/204/409/422`)؛ OpenAPI فقط در Development. |
| 5 | **اعتبارسنجی** | FluentValidation برای هر ورودی، **با پیام فارسی** (`LanguageManager.Culture = fa`). `IExceptionHandler` سراسری با `ProblemDetails` و `traceId`؛ هرگز Stack Trace. |
| 6 | **سرعت و Real-time** | HybridCache با باطل‌سازی Tag. Brotli/Gzip. SignalR تایپ‌شده با MessagePack و پیام Delta؛ Redis backplane؛ توکن Query String در لاگ ثبت نشود. |
| 7 | **تست** | xUnit (AAA) + `WebApplicationFactory` با دیتابیس واقعی (Testcontainers، نه InMemory). **تست‌های اجباری:** دو درخواست همزمان روی یک منبع محدود → فقط یکی موفق؛ رویداد/Callback تکراری → یک بار ثبت؛ مبلغ دستکاری‌شده → رد؛ IDOR → 404. |
| 8 | **DevOps** | Dockerfile چندمرحله‌ای non-root، Compose با Health Check، CI با build + test + `dotnet list package --vulnerable` + `npm audit` (+ MobSF برای APK). **پکیج آسیب‌پذیر = build قرمز.** بکاپ روزانه با تست بازیابی ماهانه. |
| 9 | **پرداخت** | Verify فقط سمت سرور؛ مبلغ همیشه از دیتابیس. Idempotency-Key. Ledger فقط-Insert. Outbox برای پیامک. تطبیق روزانه. تأیید دستی بالای سقف با دو امضا. درگاه ایران: **Parbad**؛ در توسعه درگاه آزمایشی با دکمه‌های «موفق / انصراف / مبلغ دستکاری‌شده». |
| 10 | **مشاهده‌پذیری** | Serilog، `/health/live` و `/health/ready`، OpenTelemetry، Audit Log تغییرناپذیر برای تغییر قیمت، وضعیت، پرداخت، نقش و اصلاح ساعت کار. |

---

## ۳. فرانت‌اند — قانون «فقط 21st.dev»

- **همه‌ی UI** (سایت، پنل مشتری، پنل کارمند، پنل ادمین، **و داخل اپ اندروید**) فقط از کامپوننت‌ها، Templateها و Themeهای **21st.dev**. ابزار: 21st MCP (`get_inspiration`، `generate`، `search_logo`) یا «Copy prompt» یا `npx shadcn add "https://21st.dev/r/<author>/<name>?api_key=$API_KEY_21ST"`.
- ❌ ممنوع: UI از صفر، کپی از v0 / Lovable / ThemeForest / CodePen.
- ✅ مجاز: وابستگی‌هایی که خود کامپوننت 21st می‌آورد (shadcn/ui، Radix، Tailwind، framer-motion، GSAP، Lenis، Embla).
- **تغییرات مجاز:** محتوای فارسی، RTL، رنگ برند، فونت، اتصال به API، ریسپانسیو. ساختار و موشن اصلی حفظ شود.
- **روند هر بخش:** ۲ جستجو → ۳ گزینه → انتخاب → ساخت → فارسی‌سازی → **ثبت در `docs/21st-components.md`**. کامپوننتی که آنجا ثبت نشده باید حذف شود.
- **بدون کلید 21st:** پایه از shadcn/ui. هر بخش موشن یک جایگزین موقت در `components/slots/` می‌گیرد که در بالای فایل «⚠ TEMPORARY STAND-IN» نوشته شده و در `21st-components.md` ثبت شده تا با رسیدن کلید عوض شود.
- **موشن سایت‌های ویترینی: فقط عکس با اسکرول، بدون ویدیو** (Zoom Parallax، Scroll Reveal Image، Immersive Scroll Gallery، Horizontal Scroll، Parallax Image). **پنل‌ها و اپ‌های کاری:** موشن کم و معنادار.
- قواعد موشن: فقط `transform`/`opacity`، عکس AVIF/WebP، عکس اول `priority`، بقیه lazy، `prefers-reduced-motion` → بدون حرکت.
- **طراحی:** تاریک با لایت‌مود، رنگ تأکیدی برند فقط برای CTA. فونت Vazirmatn self-hosted. اعداد فارسی، تقویم شمسی، قیمت به تومان.
- **کانوا:** فقط برای گرافیک بیرون از UI (آیکون اپ، اسپلش، عکس‌های فروشگاه، بنر، قالب گزارش). ثبت در `docs/CANVA_ASSETS.md`.

---

## ۴. قالب قابل فروش (White-label) — اجباری برای هر سایت

> هدف: یک سایت را بدون هیچ داده‌ای برای ۱۰ مشتری بفرستی و هر کدام سایت خودش را از پنل بسازد.

1. **همه‌چیز از پنل ادمین تغییر کند. هیچ متنی در کد ثابت نیست.** بخش‌ها (هر کدام یک سند JSON در جدول `SiteSettings`):
   - `brand`: نام، شعار، لوگو، آیکون تب
   - `theme`: رنگ‌ها `#RRGGBB`، گردی گوشه‌ها، تیره یا روشن
   - `contact`: تلفن، موبایل، واتساپ، اینستاگرام، آدرس، ساعات کاری، نقشه
   - `legal`: پروانه، کد اقتصادی، نماد اعتماد، درباره‌ی ما
   - `home`: هیرو + بلوک‌ها (روشن/خاموش، ترتیب، عنوان)
   - `trust`، `footer`، `seo`
   - `features`: روشن/خاموش کردن هر بخش
   - **`texts`: هر برچسب UI با کلید ثابت** (و در پروژه‌های چندزبانه، برای هر زبان — بخش ۱۲).
   - `publish`: منتشر شده یا «به‌زودی»
2. **اعتبارسنجی محتوا:** رنگ فقط Hex، URL فقط `http(s)` یا مسیر داخلی (نه `javascript:`)، سقف حجم، HTML فقط با Sanitizer سمت سرور. فقط ادمین ویرایش می‌کند. هر تغییر در Audit Log.
3. **آپلود:** بررسی Magic Bytes (SVG ممنوع)، سقف حجم، نام تصادفی، ذخیره بیرون از wwwroot، سرو با `/media/{name}` و Regex ضد Path Traversal.
4. **دو حالت نصب:** `Seed:Mode=Demo` و `Seed:Mode=Empty`.
5. **راه‌اندازی اول:** `/setup` با **کد نصب** (`Setup:Token`، مقایسه با زمان ثابت). فقط وقتی هیچ ادمین واقعی نیست؛ بار دوم 409.
6. **دستیار راه‌اندازی قدم‌به‌قدم:** `GET /admin/onboarding` وضعیت هر قدم را **از روی داده‌ی واقعی** حساب می‌کند.
   - دسکتاپ: پنل کنار فرم (هرگز روی دکمه‌ی ذخیره نیفتد).
   - موبایل: فقط در داشبورد؛ بقیه‌جا دکمه‌ی شناور «راهنما ٪».
   - نوار پیشرفت، کارت «قدم بعدی»، تیک قدم‌های انجام‌شده، «بعداً — مخفی کن».
7. **سایت منتشرنشده:** بازدیدکننده فقط «به‌زودی» می‌بیند. ادمین، کارمند، `/setup` و `/login` کار می‌کنند.

---

## ۵. ورود سریع دمو با یک کلیک (برای پرزنت)

- در `/login`، زیر فرم OTP، **یک دکمه برای هر نقش** با آیکون، نام و یک خط توضیح.
- ریسپانسیو:
  - موبایل: تمام‌عرض، زیر هم، حداقل ۴۸px
  - تبلت: ۲×۲
  - دسکتاپ: یک ردیف
- دکمه‌ی شناور «تغییر نقش» فقط در نشست دمو + نوار «حالت نمایشی — داده‌ها نمونه هستند».
- **امنیت:**
  - Flag `DemoLogin:Enabled` پیش‌فرض خاموش؛ خاموش = endpoint ساخته نمی‌شود (404).
  - **Production + Flag روشن = برنامه اجرا نمی‌شود.**
  - فقط حساب‌های `IsDemo` از Whitelist نقش؛ ادمین دمو به کلیدها و کاربران واقعی دسترسی ندارد.
  - هر ورود دمو در Audit با برچسب DEMO.
  - در نصب خالی دکمه‌ای دیده نمی‌شود.

---

## ۶. تست CRUD تصویری (بعد از هر مرحله، اجباری)

> «تو لوکال خودت تمام کارها را یک بار با دیدن تست کن.»

1. **یک اسکریپت** (`tests/e2e/run-visual.sh`):
   - پورت‌های قدیمی را آزاد می‌کند.
   - نصب **دمو** و **خالی** را بالا می‌آورد.
   - Playwright را روی **دسکتاپ (1366)**، **موبایل (Pixel 7)** و **۳۶۰px** اجرا می‌کند.
2. **پوشش:** هر عملیات ساخت/خواندن/ویرایش/حذف هر موجودیت از طریق UI واقعی + سناریوهای اصلی کسب‌وکار + نصب خالی تا دستیار.
3. **هر قدم یک اسکرین‌شات** در `tests/e2e/screenshots/{device}-{nn}-{name}.png` + گزارش HTML.
4. **دیدن واقعی:** AI باید اسکرین‌شات‌ها را **باز کند و نگاه کند**. نمونه‌هایی که فقط با دیدن پیدا شد:
   - CSS لود نشده
   - پنل راهنما روی دکمه‌ی ذخیره
   - دکمه‌های دمو در نصب خالی
   - پیام خطای انگلیسی
   - سفارش جدید پایین لیست
   - جدول، صفحه‌ی موبایل را زوم‌اوت کرده بود
5. **چک ریسپانسیو:** `noHorizontalScroll` عرض صفحه را با **`screen.width`** مقایسه می‌کند، نه فقط `innerWidth`.
6. **Selector پایدار:** `data-testid` برای هر دکمه و ردیف مهم.
7. **اپ اندروید:** همان سناریوهای اصلی روی امولاتور Android Studio با اسکرین‌شات.
8. **شبکه‌ی ضعیف:** throttle **3G کند** و ثبت حجم داده‌ی هر صفحه.

---

## ۷. امنیت و سرعت

### ۷.۱ ده اسکیل امنیتی (بررسی‌شده، ۳ آبان ۱۴۰۵)

> قبل از نصب هر اسکیل، فایل‌هایش را بخوان. ⚠️ `AlexDevFlow/Claude10XD` عمومی نیست/وجود ندارد و حذف شد.

| # | اسکیل | ریپو | حوزه |
|---|---|---|---|
| 1 | security-review | `getsentry/skills` | وب + API، فقط یافته‌ی با اطمینان بالا |
| 2 | owasp-security | `agamm/claude-code-owasp` | OWASP Top 10:2025، ASVS 5.0 |
| 3 | static-analysis (CodeQL، Semgrep، SARIF) | `trailofbits/skills` | C#، TS، Kotlin/Java |
| 4 | insecure-defaults | `trailofbits/skills` | پیش‌فرض‌ها و تنظیمات ناامن |
| 5 | supply-chain-risk-auditor | `trailofbits/skills` | پکیج‌های npm |
| 6 | differential-review | `trailofbits/skills` | بررسی امنیتی هر PR |
| 7 | gha-security-review | `getsentry/skills` | GitHub Actions |
| 8 | masvs-checklist + secure-storage-audit | `dweinstein/mobile-security-skills` | اندروید: MASVS v2، ذخیره‌ی امن |
| 9 | network-security-check + auth-assessment | `dweinstein/mobile-security-skills` | اندروید: TLS، Pinning، نشست |
| 10 | android static analysis with MobSF + testing-websocket-api-security | `mukul975/Anthropic-Cybersecurity-Skills` | اسکن APK، امنیت WebSocket/SignalR |

دستور نصب و زمان اجرای هر کدام: `docs/SECURITY_SKILLS.md` هر پروژه.
کنار این‌ها: `obra/superpowers` (TDD، دیباگ سیستماتیک) و `anthropics/skills` → `webapp-testing`.

### ۷.۲ الزامات
- **امنیت:**
  - OWASP Top 10:2025 / ASVS 5.0 L2؛ برای اپ: **MASVS v2**
  - اعتبارسنجی دوطرفه
  - CSP/HSTS/X-Frame-Options
  - آپلود امن
  - هیچ داده‌ی حساس در URL
  - یافته‌ی High/Critical = مرحله سبز نیست
- **بودجه‌ی سرعت:**
  - Lighthouse موبایل ≥ ۹۰
  - LCP < 2.5s، INP < 200ms، CLS < 0.1
  - JS صفحه‌ی اول < 200KB (پنل‌ها < 180KB)
- **قیمت زنده:** قیمت هرگز از HTML کش‌شده خوانده نشود.

---

## ۸. بازار ایران

- ورود با موبایل + OTP (کاوه‌نگار یا sms.ir).
- درگاه شاپرکی با Parbad و سقف تراکنش؛ پرداخت چندمرحله‌ای، بیعانه، اقساط و فیش کارت‌به‌کارت با تأیید ادمین.
- **هاست ایران** + CDN داخلی + Object Storage داخلی. **هیچ وابستگی خارجی** (Google Fonts، CDN خارجی، Firebase) که ممکن است فیلتر یا قطع شود؛ همه self-hosted.
- اعلان: داخل اپ + **ربات بله/تلگرام** + پیامک؛ پوش اندروید فقط از طریق آداپتر و هرگز تنها کانال.
- انتشار اپ: **کافه‌بازار، مایکت** و APK مستقیم.
- نماد اعتماد، پروانه و کد اقتصادی در فوتر (سایت‌های فروش).
- نرخ مالیات و قوانین را **هاردکد نکن**؛ از پنل با تاریخ اعتبار.

### نمونه‌ی دامنه: طلافروشی
- **فرمول قیمت:**
  ```
  قیمت = وزن × نرخ ۱۸ × عیار/750 + اجرت + سود٪×(خام+اجرت) + مالیات٪×(اجرت+سود) + نگین
  ```
  گرد کردن فقط یک بار در آخر.
- خرید کهنه بدون اجرت. **محافظ نرخ** (کهنه یا جهش > ۵٪ → توقف فروش). **قفل قیمت** ۱۰ دقیقه. صفحه‌ی محصول = جدول قطعه‌های یکتا.

---

## ۹. ساختار استاندارد پروژه

```
<project>/
├── CLAUDE.md                ← «اول فقط docs/PROJECT_MAP.md را بخوان»
├── start-dev.cmd            ← اجرای یک‌کلیکی دمو (ویندوز)
├── start-empty.cmd          ← اجرای نصب خالی
├── docs/  PROJECT_MAP.md/.html · PROMPT.md · SKILLS.md · SECURITY_SKILLS.md · 21st-components.md · CANVA_ASSETS.md
├── tools/ build_map.py · map_template.html · vendor/ (mermaid + marked آفلاین)
├── src/   *.Domain · *.Application · *.Infrastructure · *.Api · *.Worker · <name>-web (Next.js)
├── mobile/ (Capacitor → android/ برای Android Studio)
├── deploy/
└── tests/ *.UnitTests · *.IntegrationTests · e2e/ (Playwright تصویری) · load/ (k6)
```

---

## ۱۰. اپ اندروید (Capacitor + Android Studio)

1. **یک کد UI:** همان Next.js با خروجی استاتیک (`output: 'export'`، `images.unoptimized`) داخل **Capacitor 8**. صفحات پنل Client-side fetching.
2. UI داخل APK → باز شدن فوری بدون اینترنت؛ فقط داده از API.
3. `npx cap sync android` → `npx cap open android` → اجرا و امضا در **Android Studio**.
4. **امنیت:**
   - توکن در Android Keystore (Secure Storage)
   - `allowBackup=false`، بدون Cleartext
   - Pinning با پین پشتیبان
   - WebView Debug فقط Debug
   - R8 در Release
   - هیچ کلید داخل APK
   - کلید امضا بیرون از ریپو
5. **حداقل نسخه‌ی مجاز** از API و صفحه‌ی «به‌روزرسانی کنید».
6. اسکن MobSF هر Release (اسکیل ۱۰).

---

## ۱۱. ۱۰۰۰ کاربر همزمان و اینترنت ضعیف

- **هدف تست k6:** ۱۰۰۰ اتصال SignalR + ۲۰۰ درخواست/ثانیه، p95 < 300ms، خطا < ۰.۵٪. گزارش در `docs/LOAD_TEST.md`.
- **سرور:**
  - Nginx + Kestrel
  - Redis backplane (+ sticky session یا WebSockets-only با SkipNegotiation)
  - Connection Pooling
  - HybridCache
  - RateLimiter
  - کار سنگین در Worker جدا
  - Health Check
- **داده‌ی کم:**
  - Brotli، Projection، ETag/304
  - SignalR MessagePack با پیام Delta، و همگام‌سازی با Cursor بعد از قطع
  - کش IndexedDB در کلاینت
  - عکس WebP/AVIF چندسایزی، پیش‌نمایش ویدیو کم‌حجم
  - **آپلود تکه‌ای قابل ادامه**
  - حالت «صرفه‌جویی داده»
- **UI هرگز قفل نشود:**
  - Timeout و Retry با backoff
  - Skeleton و Optimistic UI
  - صف آفلاین با Idempotency-Key

---

## ۱۲. نقش، زبان و CRUD دینامیک

- **نقش‌ها:** مجوزها کاتالوگ ثابت در کد با دامنه (`Own` / `Assigned` / `All`)؛ نقش‌ها داده‌اند و ادمین می‌سازد. کاربر چند نقش. نقش‌های سیستمی پاک‌نشدنی. فیلتر دامنه در هر Query (ضد IDOR).
- **زبان‌ها:**
  - جدول `Languages` (جهت RTL/LTR، تقویم، اعداد)
  - متن‌های UI با کلید + ترجمه در دیتابیس
  - محتوای قابل ترجمه با JSON و Fallback به زبان پیش‌فرض
  - تغییر زبان و `dir` بدون رفرش
- **فرم‌ها و انواع دینامیک:** Form Builder با JSON Schema نسخه‌دار؛ اعتبارسنجی سمت سرور با همان Schema.
- **CRUD ادمین:** هر موجودیت: لیست (جستجو، فیلتر، Keyset)، دیدن، ساختن، ویرایش، حذف نرم، بازگردانی، خروجی Excel، تاریخچه. **جدول روی دسکتاپ، کارت روی موبایل**، دکمه‌ی ذخیره‌ی چسبان، تأیید حذف داخل صفحه و «برگرداندن».
