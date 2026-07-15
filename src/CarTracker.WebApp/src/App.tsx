import { GalleryPage } from './gallery/Gallery'

/**
 * The component gallery, until task 5 brings routing and real screens.
 *
 * The API-key panel that lived here has served its purpose — the gateway/API/auth loop was proved end to end
 * on 2026-07-14, and task 5 rebuilds that path properly on TanStack Query with generated types. Keeping a
 * hand-rolled fetch panel alive alongside it would be two ways to talk to the API, one of them untyped.
 */
export default function App() {
  return <GalleryPage />
}
