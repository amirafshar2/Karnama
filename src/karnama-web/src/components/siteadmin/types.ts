export type Contact = { phone: string | null; address: string | null; city: string | null; hours: string | null; email: string | null; instagram: string | null; telegram: string | null; whatsApp: string | null; neshanUrl: string | null; baladUrl: string | null; latitude: number | null; longitude: number | null };
export type HomeContent = {
  seoTitle: string; seoDescription: string; heroTitle: string; heroSubtitle: string; ctaLabel: string;
  servicesTitle: string; casesTitle: string; packagesTitle: string; processTitle: string; process: { title: string; body: string }[];
  testimonialsTitle: string; blogTitle: string; faqTitle: string; faq: { question: string; answer: string }[];
  consultTitle: string; consultText: string; contact: Contact; showPackages: boolean; showCases: boolean; showBlog: boolean;
};
export type CaseMetric = { label: string; before: number | null; after: number | null; unit: string | null };
export type CaseShot = { image: string; url: string; caption: string | null; takenOn: string | null; isAfter: boolean };
export type CaseItem = {
  id: string; businessName: string; slug: string; industry: string | null; city: string | null; logo: string | null; logoUrl: string | null; handle: string | null;
  from: string | null; to: string | null; services: string[]; summary: string | null; followersBefore: number | null; followersAfter: number | null;
  revenueBefore: number | null; revenueAfter: number | null; revenueAsPercent: boolean; metrics: CaseMetric[]; shots: CaseShot[]; quote: string | null; quoteBy: string | null;
  videoUrl: string | null; source: string | null; consentObtained: boolean; consentFile: string | null; isSample: boolean; published: boolean; featured: boolean; sortOrder: number; version: number;
};
