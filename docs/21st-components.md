# کامپوننت‌های 21st.dev

> هر کامپوننتی که در پروژه هست باید اینجا ثبت شده باشد؛ وگرنه حذف شود. (`docs/SKILLS.md` بخش ۳)
>
> **وضعیت M1 (۳ آبان ۱۴۰۵):** کلید 21st (`TWENTYFIRST_API_KEY` / `API_KEY_21ST`) در دسترس نبود.
> نصب از `21st.dev/r/...` بدون کلید پیغام `Authentication required` می‌دهد (بررسی شد).
> طبق قانون «بدون کلید 21st»، پایه از **shadcn/ui** است (همان کامپوننت‌ها روی 21st با نام `@shadcn` منتشر شده‌اند)
> و بالای هر فایل نوشته شده `⚠ TEMPORARY STAND-IN`. برای هر بخش، ۲ جستجو در 21st انجام و ۳ گزینه انتخاب شد
> تا با رسیدن کلید فقط نصب و جایگزین شود.

## ثبت‌شده‌ها (در کد)

| بخش | کامپوننت | نویسنده | منبع | تاریخ | تغییرات | جایگزین موقت؟ |
|---|---|---|---|---|---|---|
| دکمه | `components/ui/button.tsx` | shadcn | new-york-v4 (روی 21st: `@shadcn`) | ۱۴۰۵/۰۸/۰۳ | — | ✅ |
| ورودی متن | `components/ui/input.tsx` | shadcn | new-york-v4 | ۱۴۰۵/۰۸/۰۳ | — | ✅ |
| برچسب | `components/ui/label.tsx` | shadcn | new-york-v4 | ۱۴۰۵/۰۸/۰۳ | — | ✅ |
| متن چندخطی | `components/ui/textarea.tsx` | shadcn | new-york-v4 | ۱۴۰۵/۰۸/۰۳ | — | ✅ |
| ورود OTP | `components/ui/input-otp.tsx` | shadcn | new-york-v4 + `input-otp` | ۱۴۰۵/۰۸/۰۳ | RTL (border-s/e)، ۴۸px، ارقام فارسی | ✅ |
| منوی کشویی (تغییر نقش دمو) | `components/ui/dropdown-menu.tsx` | shadcn | new-york-v4 | ۱۴۰۵/۰۸/۰۳ | RTL (ps/pe/ms)، فقط در نشست دمو lazy | ✅ |
| Sheet (فرم نقش) | `components/ui/sheet.tsx` | shadcn | new-york-v4 | ۱۴۰۵/۰۸/۰۳ | دکمه‌ی بستن end-4؛ موبایل از پایین | ✅ |
| اسکلت بارگیری | `components/ui/skeleton.tsx` | shadcn | new-york-v4 | ۱۴۰۵/۰۸/۰۳ | — | ✅ |
| Toast | `components/ui/sonner.tsx` + `components/app/app-toaster.tsx` | shadcn / sonner | new-york-v4 | ۱۴۰۵/۰۸/۰۳ | lazy، موبایل بالا، دسکتاپ پایین‌چپ در RTL | ✅ |
| سایدبار دسکتاپ | `components/app/side-nav.tsx` | (داخلی) | الگوی shadcn sidebar، سبک‌شده برای بودجه‌ی ۱۸۰KB | ۱۴۰۵/۰۸/۰۳ | جمع‌شونده، RTL | ✅ |
| ناوبری پایین موبایل | داخل `components/app/panel-shell.tsx` | (داخلی) | — | ۱۴۰۵/۰۸/۰۳ | ۶۴px، safe-area | ✅ |
| الگوی کاشی (امضای بصری) | `components/app/tile-pattern.tsx` | (داخلی) | SVG ستاره‌ی هشت‌پر | ۱۴۰۵/۰۸/۰۳ | — | گرافیک است نه کامپوننت UI |

> shadcn sidebar، table، badge، switch، radio-group و … اول نصب و بعد **حذف** شدند چون بودجه‌ی JS پنل‌ها (۱۸۰KB) را رد می‌کردند؛ جایشان عنصر HTML ساده با Tailwind است.

## گزینه‌های 21st برای جایگزینی (۲ جستجو → ۳ گزینه)

| بخش | جستجوها | گزینه‌ی ۱ (پیشنهادی) | گزینه‌ی ۲ | گزینه‌ی ۳ |
|---|---|---|---|---|
| ورود + OTP | `sign-in`, `input-otp` | [Clean & Minimal sign in](https://21st.dev/@preetsuthar17/components/clean-minimal-sign-in) + [Input OTP](https://21st.dev/@preetsuthar17/components/input-otp) | [Sign In Card](https://21st.dev/@ruixen.ui/components/sign-in-card) + [OTP Verification Card](https://21st.dev/@sean0205/components/otp-verification-card) | [Login Form](https://21st.dev/@socolled/components/login-form) + [Origin UI OTP spaced](https://21st.dev/@originui/components/input/otp-input-spaced) |
| سایدبار | `sidebar`, `dashboard sidebar` | [Sidebar](https://21st.dev/@shadcn/components/sidebar) (shadcn) | [Dashboard Sidebar](https://21st.dev/@arunjdass/components/dashboard-sidebar) | [Dashboard with Collapsible Sidebar](https://21st.dev/@uniquesonu/components/dashboard-with-collapsible-sidebar) |
| ناوبری پایین | `dock`, `tabs` | [Dock Tabs](https://21st.dev/@designali-in/components/dock-tabs) | [Dock Nav](https://21st.dev/@soralabs/components/dock-nav) | [Capsule Tabs](https://21st.dev/@ruixen.ui/components/capsule-tabs) |
| Toast | `toast`, `sonner` | [Sonner](https://21st.dev/@shadcn/components/sonner) (shadcn) | [Sonner](https://21st.dev/@sean0205/components/sonner) | [Basic Toast](https://21st.dev/@anubra266/components/basic-toast/toast-updatable) |
| جدول CRUD | `data-table`, `table` | [Data Grid Table](https://21st.dev/@sean0205/components/data-grid-table/auto-width-table) | [HeroUI Table](https://21st.dev/@hero_ui/components/heroui-table/pagination) | [Column Collaboration Table](https://21st.dev/@ruixen.ui/components/column-collaboration-table) |
| حالت خالی | `empty-state`, `empty` | [Empty](https://21st.dev/@shadcn/components/empty/outline) (shadcn) | [Empty](https://21st.dev/@cnippet-dev/components/cnippet-empty/no-results) | [Empty Notifications State](https://21st.dev/@olewandowski1/components/empty-states-1) |
| ۴۰۴ | `404`, `not found` | [Not found](https://21st.dev/@nevsky118/components/not-found) | [not found 2](https://21st.dev/@efferd/components/not-found-2) | [404 Did You Mean](https://21st.dev/@ziegfiroyt/components/error9) |

**قبل از نصب هر کدام:** اندازه‌ی bundle را با `npm run size` چک کن (بودجه‌ی پنل‌ها ۱۸۰KB است و الان ~۱۸۰KB مصرف شده).

## بخش‌هایی که باید پر شوند (از `docs/PROMPT.md` بخش ۹.۵)

| بخش | پنل/صفحه | وضعیت |
|---|---|---|
| تم (روشن + تیره) | همه | 🟡 تم داخلی «فیروزه‌ای شیراز»، منتظر Theme از 21st |
| ورود OTP + ورود سریع دمو | `/login` | 🟡 جایگزین موقت |
| سایدبار (دسکتاپ) + Bottom Nav (موبایل) | همه‌ی پنل‌ها | 🟡 جایگزین موقت |
| داشبورد ادمین | ادمین | ⬜ (M9) |
| کانبان کارها | ادمین | ⬜ (M3) |
| جدول CRUD + نمای کارت موبایل + صفحه‌بندی | ادمین | 🟡 در `/roles` (جایگزین موقت) |
| فرم‌ساز خدمت | ادمین | ⬜ (M2) |
| فرم دینامیک سفارش + انتخاب تاریخ شمسی | ادمین، مشتری | ⬜ |
| دکمه‌ی ورود/خروج + تایمر کار | کارگر | ⬜ (M4) |
| تقویم حضور تیم / تقویم محتوا | ادمین، مشتری | ⬜ |
| درخواست مرخصی و کارت تأیید | کارگر، ادمین | ⬜ (M5) |
| کارت‌ها و جدول پکیج | ادمین، صفحه‌ی عمومی | ⬜ (M2) |
| ویرایشگر توضیحات پکیج | ادمین | ⬜ (M2) |
| پیش‌نمایش و تأیید محتوا | مشتری، ادمین | ⬜ (M6) |
| مرکز اعلان + Toast | همه | 🟡 Toast موقت |
| آپلود فایل بزرگ با نوار پیشرفت | کارگر | ⬜ (M3) |
| تنظیمات + ویرایشگر ترجمه | ادمین | ⬜ (M2/M8) |
| نمودارهای گزارش | ادمین | ⬜ (M9) |
| صفحه‌ی عمومی (هیرو، خدمات، پکیج‌ها، تماس، فوتر) | وب | ⬜ (M8) |
| ۴۰۴ / آفلاین / خطا / حالت خالی | همه | 🟡 موقت |

## N9 (بلاگ)

| بخش | کامپوننت | منبع | جایگزین موقت؟ |
|---|---|---|---|
| ویرایشگر متن | `components/blog/rich-editor.tsx` | Tiptap 3.31.4 (MIT) + نوار ابزار داخلی | ✅ |

گزینه‌های 21st برای نوار ابزار ویرایشگر (کوئری `tiptap editor`, `rich text editor`, `notion editor`): ۱) Minimal Tiptap (@shadcn-ext) ۲) Novel-style editor ۳) Rich Text Editor (@originui) — با رسیدن کلید 21st جایگزین می‌شود؛ هسته (Tiptap) و خروجی JSON تغییری نمی‌کند.

| بخش | کامپوننت | منبع | جایگزین موقت؟ |
|---|---|---|---|
| ستاره‌ی امتیاز (N8) | `components/app/stars.tsx` | گروه رادیویی بومی + آیکن Star از lucide | ✅ |
| نمودار روزانه‌ی گزارش (N8) | `app/(app)/(panel)/reports/day-chart.tsx` | SVG دست‌ساز | ✅ |

گزینه‌های 21st برای ستاره (کوئری `rating`, `star rating`): ۱) Rating (@originui) ۲) Star Rating (@shadcn-ext) ۳) Feedback rating — با رسیدن کلید 21st جایگزین می‌شود؛ ورودی/خروجی (عدد ۱ تا ۵) تغییری نمی‌کند.
| تور اول (N12) | `components/help/tour.tsx` | Spotlight + کارت دست‌ساز | ✅ |

گزینه‌های 21st برای تور (کوئری `onboarding tour`, `product tour`): ۱) Onboarding Tour ۲) Spotlight Tour ۳) Feature Tour — با رسیدن کلید 21st جایگزین می‌شود؛ قدم‌ها و متن‌ها تغییری نمی‌کنند.
