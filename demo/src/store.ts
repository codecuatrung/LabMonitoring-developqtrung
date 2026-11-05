import { seed, type Collection, type Entity } from './seed'

export type { Collection, Entity } from './seed'
type Store = Record<Collection, Entity[]>
type ApiResult = { data: Store; storage: 'server-memory' }

const emptyStore = (): Store => Object.keys(seed).reduce((store, collection) => {
  store[collection as Collection] = []
  return store
}, {} as Store)
let records = emptyStore()
let writes = Promise.resolve()

async function request(payload?: Record<string, unknown>): Promise<ApiResult> {
  const response = await fetch('/api/state', payload ? {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  } : undefined)
  const result = await response.json() as ApiResult & { error?: string }
  if (!response.ok) throw new Error(result.error ?? 'Không thể kết nối API nội bộ')
  return result
}

function reportError(error: unknown): void {
  const detail = error instanceof Error ? error.message : 'Lỗi API nội bộ'
  window.dispatchEvent(new CustomEvent('labmonitoring:api-error', { detail }))
}

export async function openDatabase(): Promise<void> {
  const result = await request()
  records = result.data
}

export function readRecords<T extends Entity>(collection: Collection): T[] {
  return (records[collection] ?? []) as T[]
}

export function saveRecord(collection: Collection, row: Entity): Promise<void> {
  records = { ...records, [collection]: [...records[collection].filter((item) => item.id !== row.id), row] }
  writes = writes.then(async () => {
    const result = await request({ action: 'save', collection, record: row })
    records = result.data
  }).catch(reportError)
  return writes
}

export function removeRecord(collection: Collection, id: string): Promise<void> {
  records = { ...records, [collection]: records[collection].filter((item) => item.id !== id) }
  writes = writes.then(async () => {
    const result = await request({ action: 'remove', collection, id })
    records = result.data
  }).catch(reportError)
  return writes
}

export function resetDatabase(): Promise<void> {
  records = structuredClone(seed)
  writes = writes.then(async () => {
    const result = await request({ action: 'reset' })
    records = result.data
  }).catch(reportError)
  return writes
}
