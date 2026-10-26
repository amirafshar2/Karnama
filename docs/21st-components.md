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
| سایدبار دسکتاپ | `components/app/side-nav.tsx` | wensity | [21st Sidebar](https://21st.dev/@wensity/components/sidebar) | ۱۴۰۵/۰۸/۰۴ | بخش‌بندی، کارت کاربر، بدون وابستگی | — |
| ناوبری پایین موبایل | داخل `components/app/panel-shell.tsx` | lyanchouss | [21st Dock](https://21st.dev/@lyanchouss/components/dock) | ۱۴۰۵/۰۸/۰۴ | کپسول شناور، safe-area | — |
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


## بازطراحی کامل (۴ آبان ۱۴۰۵) — با کلید 21st

پژوهش: جستجو در API رسمی `21st.dev/api/v1/components/search` (بیش از ۳۰ جستجو)، دیدن پیش‌نمایش‌ها، و گرفتن کد با `components/install`.
کد هر کامپوننت خوانده شد و **بازنویسی** شد: راست‌به‌چپ، رنگ‌های برند پویا (`--brand-black` / `--brand-yellow`)، داده‌ی واقعی پنل،
و بدون وابستگی سنگین (`motion`/`framer-motion`) — حرکت‌ها با CSS ساخته شدند تا صفحه‌ی اصلی تقریباً بدون جاوااسکریپت بماند
و پنل‌ها از بودجه‌ی ۱۸۰KB بیرون نزنند. کلید 21st فقط در محیط محلی است و در ریپو نیست.

### سایت عمومی

| بخش | فایل | کامپوننت 21st | نویسنده | تغییرات |
|---|---|---|---|---|
| ناوبری بالا (جمع‌شونده به کپسول شناور) | `components/site/motion/site-nav.tsx` | [Morphing Scroll Navbar](https://21st.dev/@laziekiki/components/morphing-scroll-navbar) | laziekiki | RTL، منوی تمام‌صفحه‌ی موبایل، صفحه‌های داخلی از اول کپسول |
| سربرگ ویدیویی | `app/(site)/page.web.tsx` + `site.css` (`.hero`) | [Glass Video Hero](https://21st.dev/@rahil1202/components/glass-video-hero) | rahil1202 | ویدیو و پوستر از پنل، سایه‌ی خوانایی، الگوی کاشی |
| بادبزن پست‌ها + دکمه‌ی Orb | همان (`.fan`, `.orb`) + `motion/hero-tilt.tsx` | [Hero Section with Cards](https://21st.dev/@scrollxui/components/hero-with-cards) | scrollxui | عکس‌ها از پنل یا نمونه‌کارها، قاب پست اینستاگرام، کج‌شدن با موس |
| تیتر کلمه‌به‌کلمه | `.hero__title .w` | [Text Reveal](https://21st.dev/@kuratlielia/components/text-reveal) | kuratlielia | فقط CSS |
| نوار متحرک خدمات | `.band` / `.marquee` | [Marquee](https://21st.dev/@diceui/components/marquee) | diceui | فقط CSS، جهت RTL |
| آمار حلقه‌ای | `.ostat` | [Orbit Ring Stats](https://21st.dev/@ham7a311/components/orbit-ring-stats) | ham7a311 | عدد واقعی؛ حلقه کامل است تا درصد ساختگی القا نشود |
| خدمات بنتو | `.bento` | [Agency Bento Grid](https://21st.dev/@abushaidislam7/components/agency-bento-grid) | abushaidislam7 | آیکن خدمت از پنل، خانه‌ی عکس |
| نمونه‌کارها (عکس دنبال موس) | `motion/hover-reel.tsx` | [Hover Reel](https://21st.dev/@ham7a311/components/hover-reel) | ham7a311 | لمسی: عکس داخل ردیف |
| روند همکاری | `.tl` + `motion/site-motion.tsx` | [Scroll animated timeline](https://21st.dev/@ibrandify/components/scroll-timeline) | ibrandify | خط پیشرفت با اسکرول |
| پکیج‌ها | `components/site/package-card.tsx` | [Glowing Pricing Tiers](https://21st.dev/@ibrandify/components/glowing-pricing-tiers) | ibrandify | قاب چرخان برای «پرفروش»، قیمت از پنل |
| نظر مشتری‌ها | `.quotes` | [Testimonials Marquee](https://21st.dev/@scrollxui/components/testimonials-with-marquee) | scrollxui | دو ردیف رفت‌وبرگشت |
| پرسش‌ها | `.faq` | [Unlumen Motion FAQs Accordion](https://21st.dev/@unlumen/components/unlumen-motion-faqs-accordion) | unlumen | `<details>` بومی + انیمیشن ارتفاع CSS |
| فرم مشاوره | `.aurora` / `.glass` | [Liquid Glass CTA](https://21st.dev/@ham7a311/components/liquid-glass-cta) | ham7a311 | فرم کامل به‌جای خبرنامه |
| فوتر | `components/site/site-chrome.tsx` | [Sticker Footer](https://21st.dev/@kedhareswer/components/sticker-slap-footer) | kedhareswer | نام برند بزرگ + برچسب‌ها (متن‌ها قابل ویرایش) |
| کارت نمونه‌کار | `components/site/case-card.tsx` | [Gallery with image cards](https://21st.dev/@shadcnblockscom/components/gallery4) | shadcnblockscom | عدد رشد روی عکس |

### رنگ و لوگو (۴ آبان ۱۴۰۵، خواسته‌ی امیر: «اعتماد، قدرت، تأثیرگذاری، مدرن»)

| بخش | منبع 21st | مقدار |
|---|---|---|
| زمینه‌ی تیره (اعتماد، قدرت) | تم [Indigo Harbor](https://21st.dev/community/themes/indigo-harbor) | سرمه‌ای `#0B1B34` |
| رنگ اصلی (تأثیرگذاری) | تم [Azure Mono](https://21st.dev/community/themes/azure-mono) | آبی سلطنتی `#3B6CFF` (متن روی آن سفید؛ روی زمینه‌ی تیره روشن‌تر می‌شود تا خوانا بماند) |
| رنگ دوم آرام | تم [Camel Linen](https://21st.dev/community/themes/camel-linen) | طلایی شتری `#D4BC8B` (الگوی کاشی، ستاره‌ی لوگو، جزئیات) |
| پالت‌های آماده در «برند و ظاهر» | Indigo Harbor، Camel Linen، Elegant Luxury، Perpetuity، Violet Bloom، Sage Garden، Solar Dusk، Graphite Mono | یک کلیک؛ بعد هم دستی قابل تغییر |
| لوگو | (طراحی اختصاصی) | ستاره‌ی هشت‌پر کاشی شیراز به طلایی + «پخش» آبی روی سرمه‌ای؛ `components/app/logo-mark.tsx`، `public/brand/logo.svg`، آیکن‌های اندروید با `tools/android_assets.py` |

### پنل و اپ اندروید

| بخش | فایل | کامپوننت 21st | تغییرات |
|---|---|---|---|
| سایدبار | `components/app/side-nav.tsx` | [Sidebar](https://21st.dev/@wensity/components/sidebar) (wensity) | بخش‌بندی، کارت کاربر، جمع‌شدن؛ بدون framer-motion/base-ui/tabler |
| نوار پایین موبایل و اپ | `components/app/panel-shell.tsx` (`BottomNav`) | [Dock](https://21st.dev/@lyanchouss/components/dock) (lyanchouss) | کپسول شناور تیره، قرص فعال با نام |
| صفحه‌ی ورود دوتکه | `app/(app)/login/login-screen.tsx` (`BrandStage`) | [Split Sign In Form](https://21st.dev/@diarmuradi/components/sign-in-3) + Glass Video Hero | ویدیو و عکس‌های سایت پشت نام برند |
| بنر خوش‌آمد داشبورد | `app/(app)/(panel)/dashboard/page.tsx` | Hero with Cards (صحنه) | میان‌برهای منو بر اساس دسترسی |

**دیگر جایگزین موقت نیستند:** سایدبار و نوار پایین (ردیف‌های ✅ بالا). دکمه، ورودی، Sheet و Toast هنوز shadcn هستند (همان نسخه‌ی منتشرشده روی 21st با نام `@shadcn`).

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
