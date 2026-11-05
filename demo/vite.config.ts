import react from '@vitejs/plugin-react'
import type { VercelRequest, VercelResponse } from '@vercel/node'
import type { Plugin } from 'vite'
import { defineConfig } from 'vite'
import apiHandler from './api/state.js'

function localApi(): Plugin {
  return {
    name: 'labmonitoring-local-api',
    configureServer(server) {
      server.middlewares.use('/api/state', async (request, response, next) => {
        try {
          if (request.method === 'POST') {
            const chunks: Buffer[] = []
            for await (const chunk of request) chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk))
            const body = Buffer.concat(chunks).toString('utf8')
            ;(request as VercelRequest).body = body ? JSON.parse(body) : {}
          }
          const vercelResponse = Object.assign(response, {
            status(code: number) {
              response.statusCode = code
              return this
            },
            json(data: unknown) {
              response.setHeader('Content-Type', 'application/json; charset=utf-8')
              response.end(JSON.stringify(data))
              return this
            },
          }) as unknown as VercelResponse
          await apiHandler(request as VercelRequest, vercelResponse)
        } catch (error) {
          next(error)
        }
      })
    },
  }
}

export default defineConfig({
  plugins: [react(), localApi()],
})
