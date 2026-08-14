import { ChatPanel } from '../chat/ChatPanel'
import { Wrap } from '../components/layout'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'

/**
 * The assistant on a phone: a screen, where a desktop gets a docked panel.
 *
 * Same `<ChatPanel>` in both — the variant only decides where it sits. A narrow viewport has nothing to dock
 * beside, and a 420 px panel overlaying a 390 px screen is a screen with extra steps.
 *
 * It is a route without a nav entry, which is why `current` is `'assistant'` rather than a `ScreenId`: adding
 * an eighteenth item to every menu for something reached from the bar itself would be worse than the highlight
 * it buys.
 */
export function AssistantPage() {
  const reg = useVehicleReg()
  const plate = usePlate()

  return (
    <AppShell scope={{ kind: 'vehicle', reg }} current="assistant">
      <Wrap>
        <ChatPanel vehicle={plate} variant="page" />
      </Wrap>
    </AppShell>
  )
}
