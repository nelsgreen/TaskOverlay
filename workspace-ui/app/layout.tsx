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

// Resolves the initial theme/accent before first paint (no FOUC) and keeps
// System live-following the OS appearance setting. This is the PR-1 minimal
// root wiring only: System/Dark/Light preference and Neutral/Warm accent
// persistence through native C# settings + the WebView2 bridge is a separate,
// later increment (see DECISIONS.md) — no localStorage is used here.
//
// `?ds-theme=light|dark` and `?ds-accent=neutral|warm` query params are a
// small dev/QA hook for rendering all four theme x accent combinations
// (screenshots, manual review); they are not a second production preference
// system and have no effect once removed from the URL.
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
