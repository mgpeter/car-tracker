import { apiStream, type ApiResult } from './client'

/**
 * The assistant's wire.
 *
 * The transcript is opaque on purpose: it is the server's own message shape, echoed back verbatim. Reasoning
 * blocks arrive with their text omitted and a signature attached, and the provider rejects an edited or
 * dropped one — so the client's job is to hold what it was given and send it back, not to understand it.
 */
export type ChatMessage = unknown

export interface ChatFile {
  mediaType: string
  /** Base64, no data-URL prefix. */
  data: string
}

/** What the server sends while a turn runs. */
export type ChatEvent =
  | { type: 'text'; delta: string }
  | { type: 'tool'; name: string; status: 'running' | 'done' }
  | { type: 'pending_write'; pendingWriteId: string; tool: string; title: string; arguments: Record<string, unknown>; schema?: JsonSchema }
  | { type: 'done'; messages: ChatMessage[] }
  | { type: 'error'; detail: string }

/** As much of JSON Schema as the draft card reads — enough to label and type a field, and no more. */
export interface JsonSchema {
  properties?: Record<string, JsonSchemaProperty>
  required?: string[]
}

export interface JsonSchemaProperty {
  type?: string | string[]
  description?: string
  enum?: string[]
}

export const sendChatMessage = (
  messages: ChatMessage[],
  vehicle: string | null,
  files: ChatFile[] = [],
): Promise<ApiResult<AsyncIterable<ChatEvent>>> =>
  apiStream<ChatEvent>('/api/chat', {
    messages,
    ...(vehicle !== null && { vehicle }),
    ...(files.length > 0 && { files }),
  })

/**
 * Runs a proposed write with the owner's final values.
 *
 * `pendingWriteId` is the whole authorisation and there is no `tool` field to send — the server reads the tool
 * from its own store. A client that could name the tool would be a client that could change it.
 */
export const confirmChatWrite = (
  messages: ChatMessage[],
  pendingWriteId: string,
  values: Record<string, unknown>,
): Promise<ApiResult<AsyncIterable<ChatEvent>>> =>
  apiStream<ChatEvent>('/api/chat/confirm', { messages, pendingWriteId, arguments: values })

/** Refuses one. The turn completes and says so; nothing is saved. */
export const declineChatWrite = (
  messages: ChatMessage[],
  pendingWriteId: string,
  reason?: string,
): Promise<ApiResult<AsyncIterable<ChatEvent>>> =>
  apiStream<ChatEvent>('/api/chat/decline', { messages, pendingWriteId, ...(reason !== undefined && { reason }) })
