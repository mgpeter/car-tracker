import { Auth0Provider } from '@auth0/auth0-react'
import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom'
import './index.css'
import { createQueryClient } from './api/queries.ts'
import { AuthGate } from './auth/AuthGate.tsx'
import { IconSprite } from './components/IconSprite.tsx'
import { auth0Config } from './lib/authConfig.ts'
import { router } from './routes.tsx'
import { ToastProvider } from './shell/Toast.tsx'
import { ThemeProvider } from './theme/ThemeProvider.tsx'

const queryClient = createQueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {/*
      Auth0 outermost, so useAuth0 is available to everything below. useRefreshTokens (rotation) is chosen over
      the hidden-iframe silent auth on purpose: it needs no frame-src in the strict CSP, only connect-src to the
      tenant. redirect_uri is the gateway origin the browser actually uses — it must be registered in Auth0.
      audience is what makes the API token a verifiable JWT rather than opaque.
    */}
    <Auth0Provider
      domain={auth0Config.domain}
      clientId={auth0Config.clientId}
      authorizationParams={{ redirect_uri: window.location.origin, audience: auth0Config.audience }}
      useRefreshTokens
      cacheLocation="localstorage"
      onRedirectCallback={() => window.history.replaceState({}, document.title, window.location.pathname)}
    >
      <ThemeProvider>
        <QueryClientProvider client={queryClient}>
          <ToastProvider>
            {/* Once, at the root: <Icon> resolves `<use href="#ct-*">` against it in the same document, so there
                is no fetch for the CSP to refuse and no icon FOUC. */}
            <IconSprite />
            <AuthGate>
              <RouterProvider router={router} />
            </AuthGate>
          </ToastProvider>
        </QueryClientProvider>
      </ThemeProvider>
    </Auth0Provider>
  </StrictMode>,
)
