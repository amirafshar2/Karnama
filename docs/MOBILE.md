# موبایل: اپ اندروید + آیفون (PWA)

> خواسته‌ی امیر (۳ آبان ۱۴۰۵): **اپ اندروید خیلی مهم است** و باید همه‌ی سناریوهای کاری را پشتیبانی کند؛
> **هر کاری که در سایت ممکن است، در اپ هم ممکن باشد**؛ کاربران **آیفون** سایت را روی صفحه‌ی گوشی ذخیره می‌کنند (PWA).

## اصل معماری: یک کد، سه شکل

| شکل | چطور | برای چه کسی |
|---|---|---|
| **وب** | Next.js روی سرور ایران | ادمین پشت کامپیوتر، همه |
| **اپ اندروید** | همان کد با `npm run build:mobile` (خروجی استاتیک) داخل **Capacitor 8** → Android Studio | کارگرها، بازاریاب‌ها، فیلمبردار، مشتری اندرویدی |
| **آیفون (PWA)** | همان سایت + `manifest.webmanifest` + Service Worker → «Add to Home Screen» | کاربران آیفون (اپ استور ایران ممکن نیست) |

**قانون:** هیچ صفحه‌ای مخصوص یک شکل ساخته نمی‌شود. هر قابلیت بومی پشت یک **آداپتر** است (`lib/native/*`) که در اندروید از پلاگین Capacitor، در آیفون/وب از API مرورگر و در نبودش از «راه جایگزین» استفاده می‌کند. تست تصویری هر صفحه روی ۱۳۶۶ / Pixel 7 / ۳۶۰px اجرا می‌شود.

## نقشه‌ی سناریوها → توانایی هر شکل

| سناریو | اندروید (Capacitor) | آیفون (PWA) | وب دسکتاپ | جایگزین وقتی ممکن نیست |
|---|---|---|---|---|
| ورود OTP، نقش‌ها، همه‌ی CRUD | ✅ | ✅ | ✅ | — |
| توکن ورود | Keystore (Secure Storage) | کوکی HttpOnly (هم‌مبدأ) | کوکی HttpOnly | — |
| ورود/خروج: **اینترنت دفتر** | IP + **BSSID** وای‌فای (پلاگین) | فقط IP عمومی دفتر | IP | QR کیوسک |
| ورود/خروج: **QR چرخان کیوسک** | اسکن با دوربین (ML Kit barcode) | اسکن با `getUserMedia` + کتابخانه‌ی JS (Safari `BarcodeDetector` ندارد) | وب‌کم | کد عددی کوتاه زیر QR |
| ورود/خروج: **موقعیت (Geofence)** | GPS لحظه‌ی ورود + **تشخیص مکان جعلی** (`@capgo/capacitor-mock-location-detector`: پرچم mock، developer options، اپ‌های جعل، جهش سرعت) | `navigator.geolocation` لحظه‌ی ورود؛ تشخیص جعل محدود → «نیازمند بررسی» اگر دقت/الگو مشکوک | — | سلفی + تأیید مدیر |
| **سلفی** زنده | دوربین جلو (نه گالری) | `getUserMedia` (فقط با تعامل کاربر) | وب‌کم | تأیید مدیر |
| **NFC** کنار در | ✅ (پلاگین NFC، NTAG 424) | ❌ در وب آیفون | ❌ | QR کیوسک |
| دستگاه اثر انگشت | سمت سرور (ADMS) — مستقل از گوشی | همان | همان | — |
| **آپلود فایل/ویدیو قابل ادامه** | Filesystem + آپلود تکه‌ای | `<input type=file>` + آپلود تکه‌ای (در پس‌زمینه ادامه نمی‌یابد) | ✅ | ربات تلگرام/بله |
| **فایل از ربات تلگرام** | سمت سرور — مستقل از گوشی | همان | همان | آداپتر بله |
| **اعلان (پوش)** | آداپتر پوش + اعلان محلی | **Web Push فقط از iOS 16.4 و فقط بعد از Add to Home Screen**، با اجازه‌ی کاربر روی دکمه | Web Push | **همیشه ربات بله/تلگرام + پیامک** (پوش هرگز تنها کانال نیست) |
| یادآوری انتشار سر ساعت (آفلاین) | Local Notifications | ❌ (iOS بدون سرور زمان‌بندی ندارد) | ❌ | پوش سرور + ربات + پیامک |
| تماس بازاریاب + «نتیجه‌ی تماس» | `tel:` + رویداد App resume | `tel:` + `visibilitychange` | — | فرم دستی |
| دوربین مداربسته | WebRTC/HLS در WebView | WebRTC/HLS در Safari | ✅ | اسنپ‌شات ۱۰ ثانیه‌ای |
| گفتگو + پیام صوتی | MediaRecorder / پلاگین | MediaRecorder (Safari 14.1+) | ✅ | متن |
| اسکن QR تجهیز | دوربین | دوربین (JS) | — | جستجوی کد اموال |
| کار بدون اینترنت (صف + Idempotency) | ✅ IndexedDB | ✅ IndexedDB (ممکن است بعد از مدتی عدم استفاده پاک شود) | ✅ | ارسال دوباره با اعلان |

## آیفون (PWA) — آنچه ساخته شد (M1)
- `src/app/manifest.webmanifest/route.ts`: نام و رنگ از **تنظیمات برند**، `display: standalone`، `dir: rtl`، آیکون‌های ۱۹۲/۵۱۲/maskable (و لوگوی آپلودشده).
- `public/sw.js`: پوسته‌ی اپ (فایل‌های هش‌دار) cache-first؛ صفحه‌ها network-first با نسخه‌ی ذخیره و `offline.html`؛ **API هرگز کش نمی‌شود**.
- متادیتای iOS: `apple-mobile-web-app-capable`، `apple-touch-icon` (به لوگوی برند عوض می‌شود)، نوار وضعیت مشکی.
- راهنمای نصب در سافاری آیفون/آیپد («اشتراک ← Add to Home Screen»)، یک بار و قابل بستن؛ تشخیص آیپد که خودش را Mac معرفی می‌کند.

## محدودیت‌های واقعی آیفون (برای انتظار درست مشتری)
- Web Push فقط روی iOS **16.4+** و فقط برای وب‌اپ نصب‌شده؛ درخواست اجازه فقط با کلیک کاربر؛ هر پوش باید اعلان نشان دهد؛ گزارش شده که اشتراک‌ها گاهی بعد از مدتی از کار می‌افتند → **کانال‌های پشتیبان اجباری‌اند**.
- BSSID وای‌فای، NFC، کار پس‌زمینه و اعلان محلی زمان‌بندی‌شده در وب آیفون نیست → سیاست ورود برای کاربر آیفون باید روش‌های جایگزین (QR کیوسک، IP، سلفی) داشته باشد. ادمین در «سیاست ورود» می‌بیند کدام روش روی کدام شکل کار می‌کند.
- در اتحادیه‌ی اروپا اپل در iOS 17.4 حذف PWA را اعلام و بعد پس گرفت؛ برای ایران اثری ندارد ولی در `docs/` ثبت است.

## اندروید — آنچه ساخته شد (M7)

- **پروژه:** `mobile/` با Capacitor 8.5.3 (`mobile/capacitor.config.ts`، `appId` = `ir.karnama.app`؛ برای هر آژانس قابل تغییر با `KARNAMA_APP_ID` و `KARNAMA_APP_NAME`). وب اپ همان کد است: `next build` با `BUILD_TARGET=mobile` خروجی استاتیک در `src/karnama-web/out`.
- **پلاگین‌ها (نسخه‌ی ثابت، بررسی‌شده):** Secure Storage (Keystore — توکن ورود)، Geolocation + **تشخیص مکان جعلی** (`@capgo/capacitor-mock-location-detector`)، **اسکنر QR گوشی** (ML Kit؛ اگر نبود، دوربین داخل صفحه)، App (دکمه‌ی برگشت)، Local Notifications (یادآوری زمان انتشار برای پیج‌های دست مشتری، حتی آفلاین)، Splash، Status Bar.
- **آداپترها:** `src/karnama-web/src/lib/native.ts` — صفحه‌ها فقط این را صدا می‌زنند؛ در وب چیزی از Capacitor دانلود نمی‌شود.
- **دانلود فایل:** با لینک امضاشده‌ی ۱۰ دقیقه‌ای (`POST /api/v1/files/{id}/link`) — در اپ به مدیر دانلود اندروید (با ادامه بعد از قطعی) و در وب مستقیم به دیسک، نه حافظه.
- **آیکون و اسپلش:** `python tools/android_assets.py [حرف] [رنگ زمینه] [رنگ حرف]` از برند ساخته می‌شود (برای انتشار می‌توانید طرح کانوا را جایگزین کنید).
- **امنیت (MASVS):** `allowBackup=false` و قوانین استخراج داده، فقط HTTPS و فقط CAهای سیستم (`network_security_config.xml`؛ HTTP فقط برای امولاتور 10.0.2.2)، اشکال‌زدایی WebView فقط با `KARNAMA_DEBUG=1`، امضای انتشار از `android/keystore.properties` (در گیت نیست)، مجوزها فقط دوربین، مکان لحظه‌ای و اعلان. جزئیات: `docs/security/2026-10-25-m7-review.md`.
- **تست:** خروجی اپ در تست تصویری روی Pixel 7 و ۳۶۰px اجرا می‌شود (ورود، ماندن بعد از بارگذاری دوباره، صفحه‌های اصلی).

### ساخت APK (روی کامپیوتر امیر با Android Studio)
1. `build-android.cmd` را اجرا کنید (پیش‌فرض API = امولاتور روی همین کامپیوتر). برای سرور واقعی: `set KARNAMA_API=https://api.example.ir` و بعد اجرا.
2. Android Studio باز می‌شود: Run برای امتحان؛ برای انتشار **Build → Generate Signed App Bundle / APK**.
3. کلید امضا یک بار ساخته می‌شود و **هرگز گم نشود** (بدون آن به‌روزرسانی اپ ممکن نیست):
   `keytool -genkeypair -v -keystore karnama-release.jks -keyalg RSA -keysize 4096 -validity 10000 -alias karnama`
   بعد `mobile/android/keystore.properties` با `storeFile`، `storePassword`، `keyAlias`، `keyPassword` (این فایل و jks در گیت نیستند).
4. در API باید `Cors__AllowedOrigins` شامل `https://localhost` باشد (مبدأ اپ).
5. اسکن امنیتی APK با **MobSF** قبل از انتشار (`docker run -p 8000:8000 opensecurity/mobile-security-framework-mobsf` و آپلود APK).

## اندروید — قدم‌های بعدی (هر مرحله‌ای که قابلیت بومی لازم دارد)
1. `mobile/` با Capacitor 8.5، `webDir` = `src/karnama-web/out`، `minSdk 24`.
2. پلاگین‌ها (هر کدام قبل از نصب بررسی امنیت و نسخه‌ی سازگار با Capacitor 8): Camera، Filesystem، Network، App، Local Notifications، Secure Storage (Keystore)، Splash، Share، Geolocation + mock-location-detector، Barcode (ML Kit)، NFC، Wi-Fi info (BSSID).
3. `lib/native/*` آداپترها با تشخیص `Capacitor.isNativePlatform()`؛ صفحه‌ها فقط آداپتر را صدا می‌زنند.
4. امنیت MASVS (`docs/SECURITY_SKILLS.md` اسکیل ۸–۱۰).

## منابع بررسی‌شده
- [PWA Push Notifications on iOS in 2026: What Really Works](https://webscraft.org/blog/pwa-pushspovischennya-na-ios-u-2026-scho-realno-pratsyuye?lang=en)
- [PWA iOS Limitations and Safari Support](https://www.magicbell.com/blog/pwa-ios-limitations-safari-support-complete-guide)
- [Capgo — Mock Location Detector plugin](https://capgo.app/docs/plugins/mock-location-detector/)
- [Capacitor — Updating plugins to 8.0](https://capacitorjs.com/docs/updating/plugins/8-0)
