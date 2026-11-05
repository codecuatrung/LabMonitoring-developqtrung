/// <reference path="./sqljs-inline.d.ts" />
import initSqlJs from 'sql.js/dist/sql-wasm-inline.js'
import type { Database } from 'sql.js'
import type { VercelRequest, VercelResponse } from '@vercel/node'
import { seed, type Collection, type Entity } from '../src/seed.ts'

let databasePromise: Promise<Database> | undefined

async function getDatabase(): Promise<Database> {
  databasePromise ??= initSqlJs().then((SQL) => {
    const db = new SQL.Database()
    db.run('CREATE TABLE IF NOT EXISTS records (collection TEXT NOT NULL, id TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(collection, id))')
    db.run('BEGIN TRANSACTION')
    for (const [collection, rows] of Object.entries(seed)) {
      for (const row of rows) {
        db.run('INSERT OR IGNORE INTO records(collection, id, payload) VALUES (?, ?, ?)', [collection, row.id, JSON.stringify(row)])
      }
    }
    db.run('COMMIT')
    return db
  })
  return databasePromise
}

function snapshot(db: Database): Record<Collection, Entity[]> {
  return Object.fromEntries(Object.keys(seed).map((collection) => {
    const result = db.exec('SELECT payload FROM records WHERE collection = ? ORDER BY rowid', [collection])
    const rows = (result[0]?.values ?? []).map(([payload]) => JSON.parse(String(payload)) as Entity)
    return [collection, rows]
  })) as Record<Collection, Entity[]>
}

function isCollection(value: unknown): value is Collection {
  return typeof value === 'string' && Object.hasOwn(seed, value)
}

export const config = { api: { bodyParser: true } }

export default async function handler(request: VercelRequest, response: VercelResponse): Promise<void> {
  response.setHeader('Cache-Control', 'no-store')
  try {
    const db = await getDatabase()
    if (request.method === 'GET') {
      response.status(200).json({ data: snapshot(db), storage: 'server-memory' })
      return
    }
    if (request.method !== 'POST') {
      response.setHeader('Allow', 'GET, POST')
      response.status(405).json({ error: 'Method not allowed' })
      return
    }

    const body = request.body as { action?: string; collection?: unknown; record?: Entity; id?: string } | undefined
    if (!body || typeof body !== 'object') {
      response.status(400).json({ error: 'Invalid request body' })
      return
    }

    if (body.action === 'reset') {
      db.run('DELETE FROM records')
      db.run('BEGIN TRANSACTION')
      for (const [collection, rows] of Object.entries(seed)) {
        for (const row of rows) db.run('INSERT INTO records(collection, id, payload) VALUES (?, ?, ?)', [collection, row.id, JSON.stringify(row)])
      }
      db.run('COMMIT')
      response.status(200).json({ data: snapshot(db), storage: 'server-memory' })
      return
    }

    if (!isCollection(body.collection)) {
      response.status(400).json({ error: 'Unknown collection' })
      return
    }

    if (body.action === 'save' && body.record && typeof body.record.id === 'string') {
      db.run('INSERT OR REPLACE INTO records(collection, id, payload) VALUES (?, ?, ?)', [body.collection, body.record.id, JSON.stringify(body.record)])
      response.status(200).json({ data: snapshot(db), storage: 'server-memory' })
      return
    }

    if (body.action === 'remove' && typeof body.id === 'string') {
      db.run('DELETE FROM records WHERE collection = ? AND id = ?', [body.collection, body.id])
      response.status(200).json({ data: snapshot(db), storage: 'server-memory' })
      return
    }

    response.status(400).json({ error: 'Unsupported operation' })
  } catch (error) {
    response.status(500).json({ error: error instanceof Error ? error.message : 'Internal server error' })
  }
}
