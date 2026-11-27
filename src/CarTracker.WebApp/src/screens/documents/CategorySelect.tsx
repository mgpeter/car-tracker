import { groupLabel, groupsOf, useDocumentTypes, type DocumentType } from '../../api/documents'
import { Select } from '../../components/Sheet'

interface CategorySelectProps {
  value: string
  onChange: (type: DocumentType) => void
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

/**
 * The category picker: one `<optgroup>` per group, from `GET /api/document-types`, so the list is never typed
 * out on this side. A group the client does not know still renders, under its raw name, rather than being
 * dropped with its categories.
 */
export function CategorySelect({ value, onChange, ...field }: CategorySelectProps) {
  const { data: types, isError } = useDocumentTypes()

  if (types === undefined) {
    return (
      <Select value={value} disabled onChange={() => {}} {...field}>
        <option value={value}>{isError ? 'Categories could not be loaded' : 'Loading categories…'}</option>
      </Select>
    )
  }

  return (
    <Select value={value} onChange={(e) => onChange(e.target.value as DocumentType)} {...field}>
      {groupsOf(types).map((group) => (
        <optgroup key={group} label={groupLabel(group)}>
          {types
            .filter((t) => t.group === group)
            .map((t) => (
              <option key={t.type} value={t.type}>
                {t.label}
              </option>
            ))}
        </optgroup>
      ))}
    </Select>
  )
}
