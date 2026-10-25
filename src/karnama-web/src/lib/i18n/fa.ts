/**
 * Persian UI texts (the default language). Every key can be overridden per language from the admin panel
 * (Settings > Texts → Translations table). Split by module so each page only ships the texts it uses:
 * core (here) is in every page; module dictionaries register themselves when their page loads.
 */
export const faCore = {
  "brand.defaultName": "کارنما",
  "brand.tagline": "کارهای آژانس، یک جا و لحظه‌ای",

  "common.save": "ذخیره",
  "common.saving": "در حال ذخیره…",
  "common.cancel": "انصراف",
  "common.delete": "حذف",
  "common.restore": "برگرداندن",
  "common.edit": "ویرایش",
  "common.create": "ساختن",
  "common.search": "جستجو",
  "common.loading": "در حال بارگیری…",
  "common.retry": "دوباره امتحان کن",
  "common.back": "بازگشت",
  "common.more": "بیشتر",
  "common.close": "بستن",
  "common.loadMore": "موارد بیشتر",
  "common.offline": "اینترنت قطع است. تغییرات بعد از وصل شدن ارسال می‌شود.",
  "common.errorGeneric": "مشکلی پیش آمد. دوباره امتحان کنید.",
  "common.errorNetwork": "به سرور وصل نشد. اینترنت را بررسی کنید و دوباره امتحان کنید.",
  "common.traceId": "کد پیگیری: {id}",


  "demo.banner": "حالت نمایشی — داده‌ها نمونه هستند",
  "demo.switchRole": "تغییر نقش",


  "nav.dashboard": "داشبورد",
  "nav.roles": "نقش‌ها و دسترسی",
  "nav.profile": "حساب من",
  "nav.logout": "خروج",
  "nav.users": "همکاران",
  "nav.work": "تابلوی کار",
  "nav.orders": "سفارش‌ها",
  "nav.clients": "مشتری‌ها",
  "nav.packages": "پکیج‌ها",
  "nav.services": "انواع خدمت",
  "nav.platforms": "شبکه‌ها",
  "nav.brand": "برند و ظاهر",
  "nav.texts": "متن‌های سایت",
  "nav.main": "منوی اصلی",



  "notFound.title": "این صفحه پیدا نشد",
  "notFound.body": "آدرس را بررسی کنید یا به داشبورد برگردید.",
  "notFound.home": "رفتن به داشبورد",

  "pwa.iosTitle": "کارنما را مثل یک اپ روی آیفون نصب کنید",
  "pwa.iosStep1": "در سافاری دکمه‌ی اشتراک‌گذاری",
  "pwa.iosStep2": "را بزنید و بعد",
  "pwa.iosAdd": "Add to Home Screen",

  "realtime.permissionsChanged": "دسترسی‌های شما تغییر کرد و منو به‌روز شد.",
} as const;
