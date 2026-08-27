import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'

import { App } from './App'
import './index.css'

const queryClient = new QueryClient()
const router = createBrowserRouter([{ path: '*', Component: App }])
const root = document.getElementById('root')

if (!root) {
  throw new Error('The application root is missing.')
}

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
