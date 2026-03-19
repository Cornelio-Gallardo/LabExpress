import axios from 'axios'
import { useAuthStore } from '../store/auth'

const api = axios.create({ baseURL: '/api' })

// CDM §8: token is memory-only — read from Pinia store, never from localStorage
api.interceptors.request.use(config => {
  const auth = useAuthStore()
  if (auth.token) config.headers.Authorization = `Bearer ${auth.token}`
  return config
})

// Global 401 handler — session expired or invalid token
api.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401 && !window.location.pathname.includes('/login')) {
      const auth = useAuthStore()
      auth.logout()                   // clear Pinia state + localStorage profile data
      window.location.href = '/login' // hard redirect to force re-authentication
    }
    return Promise.reject(err)
  }
)

export default api

// ── Auth ──────────────────────────────────────────────────────────────────────
export const authApi = {
  login: (email, password) => api.post('/auth/login', { email, password })
}

// ── Patients ─────────────────────────────────────────────────────────────────
export const patientsApi = {
  getAll:     (params) => api.get('/patients', { params }),
  getSummary: (params) => api.get('/patients/summary', { params }),
  getById:    (id) => api.get(`/patients/${id}`),
  create:     (data) => api.post('/patients', data),
  deactivate: (id) => api.delete(`/patients/${id}`)
}

// ── Sessions ─────────────────────────────────────────────────────────────────
export const sessionsApi = {
  getAll: (params) => api.get('/sessions', { params }),
  getById: (id) => api.get(`/sessions/${id}`),
  create: (data) => api.post('/sessions', data),
  bulkCreate: (data) => api.post('/sessions/bulk', data),
  updateChair: (id, chair) => api.patch(`/sessions/${id}`, { chair }),
  delete: (id) => api.delete(`/sessions/${id}`)
}

// ── Results ──────────────────────────────────────────────────────────────────
export const resultsApi = {
  getCurrent:      (patientId) => api.get(`/results/current/${patientId}`),
  getHistory:      (patientId, testCode, params) => api.get(`/results/history/${patientId}/${testCode}`, { params }),
  getCompare:      (patientId, count) => api.get(`/results/compare/${patientId}`, { params: { count } }),
  getOrders:       (patientId) => api.get(`/results/orders/${patientId}`),
  getLongitudinal: (patientId, months = 6) => api.get(`/results/longitudinal/${patientId}`, { params: { months } })
}

// ── MD Notes ─────────────────────────────────────────────────────────────────
export const notesApi = {
  getBySession: (sessionId) => api.get('/notes', { params: { sessionId } }),
  create: (data) => api.post('/notes', data),
  update: (id, noteText) => api.patch(`/notes/${id}`, { noteText })
}

// ── Users (avatar)
export const usersApi = {
  uploadAvatar: (id, file) => {
    const form = new FormData()
    form.append('file', file)
    return api.post('/users/' + id + '/avatar', form, { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  deleteAvatar: (id) => api.delete('/users/' + id + '/avatar')
}

// ── Export ───────────────────────────────────────────────────────────────────
export const exportApi = {
  export: (data) => api.post('/export', data, {
    responseType: data.format === 'csv' ? 'blob' : 'json'
  }),
  sessionPdf: (sessionId, opts) => api.get('/export/session-pdf', {
    params: { sessionId, ...opts },
    responseType: 'blob'
  }),
  shiftPdf: (sessionIds) => api.post('/export/shift-pdf', { sessionIds }, { responseType: 'blob' }),
  urr: (params) => api.get('/export/urr', { params })
}

// ── Shift Management ─────────────────────────────────────────────────────────
export const shiftsApi = {
  getAll:        (params) => api.get('/shifts', { params }),
  create:        (data) => api.post('/shifts', data),
  update:        (id, data) => api.patch(`/shifts/${id}`, data),
  delete:        (id) => api.delete(`/shifts/${id}`),
  bulkCreate:    (data) => api.post('/shifts/bulk', data),
  assignNurse:   (shiftId, data) => api.post(`/shifts/${shiftId}/nurses`, data),
  removeNurse:   (shiftId, nurseId) => api.delete(`/shifts/${shiftId}/nurses/${nurseId}`)
}

// ── Branding / Tenant ─────────────────────────────────────────────────────────
export const tenantApi = {
  get:              () => api.get('/tenant'),
  updateBranding:   (data) => api.patch('/tenant/branding', data),
  getSxaTests:      () => api.get('/tenant/sxa-tests'),
  getSxaAnalytes:   () => api.get('/tenant/sxa-analytes'),
  getTestMaps:      () => api.get('/tenant/test-maps'),
  createTestMap:    (data) => api.post('/tenant/test-maps', data),
  deleteTestMap:    (id) => api.delete(`/tenant/test-maps/${id}`),
  getAnalyteMaps:   () => api.get('/tenant/analyte-maps'),
  createAnalyteMap: (data) => api.post('/tenant/analyte-maps', data),
  deleteAnalyteMap: (id) => api.delete(`/tenant/analyte-maps/${id}`)
}

export const clinicsApi = {
  getAll:           () => api.get('/clinics'),
  create:           (data) => api.post('/clinics', data),
  update:           (id, data) => api.patch(`/clinics/${id}`, data),
  activate:         (id) => api.patch(`/clinics/${id}/activate`),
  deactivate:       (id) => api.patch(`/clinics/${id}/deactivate`),
  updateBranding:   (id, data) => api.patch(`/clinics/${id}/branding`, data)
}