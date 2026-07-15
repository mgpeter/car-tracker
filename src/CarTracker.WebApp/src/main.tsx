import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { IconSprite } from './components/IconSprite.tsx'
import { ToastProvider } from './shell/Toast.tsx'
import { ThemeProvider } from './theme/ThemeProvider.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <ToastProvider>
        {/* Once, at the root: <Icon> resolves `<use href="#ct-*">` against it in the same document, so there
            is no fetch for the CSP to refuse and no icon FOUC. */}
        <IconSprite />
        <App />
      </ToastProvider>
    </ThemeProvider>
  </StrictMode>,
)
