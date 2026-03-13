import axios from 'axios'

const api = axios.create({ baseURL: '/api' })

// Attach JWT token to every request
api.interceptors.request.use(config => {
  const token = localStorage.getItem('dx7_token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// Global 401 handler
api.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401) {
      localStorage.removeItem('dx7_token')
      localStorage.removeItem('dx7_user')
      window.location.href = '/login'
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
  getAll: (params) => api.get('/patients', { params }),
  getById: (id) => api.get(`/patients/${id}`),
  create: (data) => api.post('/patients', data),
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
  getCurrent: (patientId) => api.get(`/results/current/${patientId}`),
  getHistory: (patientId, testCode, params) => api.get(`/results/history/${patientId}/${testCode}`, { params })
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
  })
}

// ── Shift Management ─────────────────────────────────────────────────────────
export const shiftsApi = {
  getAll: (params) => api.get('/shifts', { params }),
  getWeek: (params) => api.get('/shifts/week', { params }),
  getHistory: (params) => api.get('/shifts/history', { params }),
  create: (data) => api.post('/shifts', data),
  bulkCreate: (data) => api.post('/shifts/bulk', data),
  update: (id, data) => api.patch(`/shifts/${id}`, data),
  delete: (id) => api.delete(`/shifts/${id}`),
  assignNurse: (id, data) => api.post(`/shifts/${id}/nurses`, data),
  removeNurse: (id, assignmentId) => api.delete(`/shifts/${id}/nurses/${assignmentId}`)
}