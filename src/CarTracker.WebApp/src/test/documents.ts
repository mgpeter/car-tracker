import type { DocumentItem, DocumentTypeInfo } from '../api/documents'

/** One filed document with every field the contract carries; override what a test is about. */
export function doc(overrides: Partial<DocumentItem> = {}): DocumentItem {
  return {
    id: 1, type: 'CarPhoto', title: 'On the drive', documentDate: '2026-11-20', contentType: 'image/jpeg',
    sizeBytes: 2_000_000, sha256: 'a'.repeat(64), serviceRecordId: null, expenseEntryId: null, issueId: null,
    notes: null, linkedTo: null, kind: 'Photo', group: 'Photos', hasThumbnail: false, hasDisplay: false,
    width: null, height: null, createdAt: '2026-11-20T10:00:00Z',
    ...overrides,
  }
}

/** A paper, with the kind and group a paper category has. */
export function paper(overrides: Partial<DocumentItem> = {}): DocumentItem {
  return doc({
    type: 'V5C', title: 'V5C registration certificate', contentType: 'application/pdf', sizeBytes: 96_000,
    kind: 'Paper', group: 'Ownership', ...overrides,
  })
}

/** `GET /api/document-types`, as the server serves it today. */
export const DOCUMENT_TYPES: DocumentTypeInfo[] = [
  { type: 'V5C', group: 'Ownership', kind: 'Paper', label: 'V5C logbook' },
  { type: 'BillOfSale', group: 'Ownership', kind: 'Paper', label: 'Bill of sale' },
  { type: 'MOT', group: 'LegalAndCover', kind: 'Paper', label: 'MOT certificate' },
  { type: 'Insurance', group: 'LegalAndCover', kind: 'Paper', label: 'Insurance certificate' },
  { type: 'RoadTax', group: 'LegalAndCover', kind: 'Paper', label: 'Road tax' },
  { type: 'Breakdown', group: 'LegalAndCover', kind: 'Paper', label: 'Breakdown cover' },
  { type: 'ServiceInvoice', group: 'WorkAndParts', kind: 'Paper', label: 'Service invoice' },
  { type: 'PartsReceipt', group: 'WorkAndParts', kind: 'Paper', label: 'Parts receipt' },
  { type: 'Warranty', group: 'WorkAndParts', kind: 'Paper', label: 'Warranty' },
  { type: 'Receipt', group: 'WorkAndParts', kind: 'Paper', label: 'Receipt' },
  { type: 'Manual', group: 'Reference', kind: 'Paper', label: "Owner's handbook" },
  { type: 'WorkshopManual', group: 'Reference', kind: 'Paper', label: 'Workshop manual' },
  { type: 'Diagram', group: 'Reference', kind: 'Paper', label: 'Diagram' },
  { type: 'CarPhoto', group: 'Photos', kind: 'Photo', label: 'Car photo' },
  { type: 'ConditionPhoto', group: 'Photos', kind: 'Photo', label: 'Condition photo' },
  { type: 'Other', group: 'Other', kind: 'Paper', label: 'Other' },
]

/** A document log split the way the server splits it. */
export function logOf(items: DocumentItem[]) {
  return {
    papers: items.filter((d) => d.kind === 'Paper'),
    photos: items.filter((d) => d.kind === 'Photo'),
    totalCount: items.length,
    totalSizeBytes: items.reduce((sum, d) => sum + d.sizeBytes, 0),
  }
}
