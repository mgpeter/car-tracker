import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom'
import './index.css'
import { createQueryClient } from './api/queries.ts'
import { IconSprite } from './components/IconSprite.tsx'
import { router } from './routes.tsx'
import { ToastProvider } from './shell/Toast.tsx'
import { ThemeProvider } from './theme/ThemeProvider.tsx'

const queryClient = createQueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          {/* Once, at the root: <Icon> resolves `<use href="#ct-*">` against it in the same document, so there
              is no fetch for the CSP to refuse and no icon FOUC. */}
          <IconSprite />
          <RouterProvider router={router} />
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>
  </StrictMode>,
)
