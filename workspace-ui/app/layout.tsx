import type { Metadata, Viewport } from 'next'
import Script from 'next/script'
import './globals.css'

export const metadata: Metadata = {
  title: 'TaskOverlay Workspace',
  description: 'Task Tree workspace for managing projects, sections, tasks and active overlay items',
}

export const viewport: Viewport = {
  colorScheme: 'light dark',
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: 'white' },
    { media: '(prefers-color-scheme: dark)', color: 'black' },
  ],
}

// Resolves a best-guess initial theme/accent before first paint (no FOUC),
// before the real persisted preference can arrive from the Workspace
// snapshot (WebView2 bridge, post-hydration). It assumes System/Neutral with
// a live prefers-color-scheme fallback, matching the production defaults; the
// centralized `useWorkspaceAppearance` layer (lib/appearance.ts) then applies
// the actual persisted preference once the bridge connects, and keeps
// re-applying it on every snapshot change. No localStorage is used here or
// there — persistence lives entirely in native C# settings + AppState.
//
// `?ds-theme=light|dark` and `?ds-accent=neutral|warm` query params are a
// small dev/QA hook for rendering all four theme x accent combinations
// (screenshots, manual review); they are not a second production preference
// system, are re-checked by `useWorkspaceAppearance` too, and have no effect
// once removed from the URL.
const THEME_INIT_SCRIPT = `(function () {
  try {
    var root = document.documentElement;
    var params = new URLSearchParams(window.location.search);
    var themeOverride = params.get('ds-theme');
    var accentOverride = params.get('ds-accent');

    root.setAttribute('data-accent', accentOverride === 'warm' ? 'warm' : 'neutral');

    if (themeOverride === 'dark' || themeOverride === 'light') {
      root.setAttribute('data-theme', themeOverride);
    } else {
      var mq = window.matchMedia('(prefers-color-scheme: dark)');
      var apply = function (isDark) {
        root.setAttribute('data-theme', isDark ? 'dark' : 'light');
      };
      apply(mq.matches);
      if (mq.addEventListener) {
        mq.addEventListener('change', function (e) { apply(e.matches); });
      }
    }
  } catch (e) {}
})();`

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html lang="en">
      <head>
        <Script id="theme-init" strategy="beforeInteractive">
          {THEME_INIT_SCRIPT}
        </Script>
      </head>
      <body className="antialiased bg-background font-sans">
        {children}
      </body>
    </html>
  )
}
