import { defineStore } from 'pinia'
import { authApi } from '../services/api'

export const useAuthStore = defineStore('auth', {
  state: () => ({
    user: JSON.parse(localStorage.getItem('dx7_user') || 'null'),
    tenant: JSON.parse(localStorage.getItem('dx7_tenant') || 'null'),
    client: JSON.parse(localStorage.getItem('dx7_client') || 'null'),
    token: localStorage.getItem('dx7_token') || null,
    roles: JSON.parse(localStorage.getItem('dx7_roles') || '[]'),
    loading: false,
    error: null
  }),

  getters: {
    isLoggedIn: (s) => !!s.token,
    role: (s) => s.user?.role || '',

    // Role checks
    isSysAd:      (s) => s.user?.role === 'sysad',
    isPlAdmin:     (s) => s.user?.role === 'pl_admin',
    isClinicAdmin: (s) => s.user?.role === 'clinic_admin',
    isChargeNurse: (s) => s.user?.role === 'charge_nurse',
    isShiftNurse:  (s) => s.user?.role === 'shift_nurse',
    isMd:          (s) => s.user?.role === 'md',

    // Admin = can access user/clinic management UI
    isAdmin: (s) => ['clinic_admin', 'pl_admin'].includes(s.user?.role),
    // Clinic staff = clinical roles
    isClinical: (s) => ['charge_nurse', 'shift_nurse', 'md'].includes(s.user?.role),

    // Per PRD Role Permissions Matrix
    canSelectShift:    (s) => ['charge_nurse', 'shift_nurse', 'md', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canSelectPatients: (s) => ['charge_nurse', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canAssignChairs:   (s) => s.user?.role === 'charge_nurse',
    canViewResults:    (s) => ['charge_nurse', 'shift_nurse', 'md'].includes(s.user?.role),
    canViewNotes:      (s) => ['charge_nurse', 'shift_nurse', 'md'].includes(s.user?.role),
    canWriteNotes:     (s) => s.user?.role === 'md',
    canExport:         (s) => ['charge_nurse', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canPrint:          (s) => ['charge_nurse', 'md', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canManageSession:  (s) => ['charge_nurse', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canManageShifts:   (s) => ['charge_nurse', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canManageUsers:    (s) => ['clinic_admin', 'pl_admin'].includes(s.user?.role),
    canManageClinics:  (s) => s.user?.role === 'pl_admin',

    // Display label from DB roles — falls back to role key if not loaded yet
    roleLabel: (s) => {
      if (s.roles.length > 0) {
        const found = s.roles.find(r => r.roleKey === s.user?.role)
        if (found) return found.label
      }
      return s.user?.role || ''
    },
  },

  actions: {
    async login(email, password) {
      this.loading = true
      this.error = null
      try {
        const { data } = await authApi.login(email, password)
        // Normalise — ensure id is always lowercase regardless of server casing
        const normaliseClient = (c) => c ? { ...c, id: c.id || c.Id } : null
        const normaliseUser   = (u) => u ? { ...u, id: u.id || u.Id } : null
        this.token  = data.token
        this.user   = normaliseUser(data.user)
        this.tenant = data.tenant
        this.client = normaliseClient(data.client)
        localStorage.setItem('dx7_token',  data.token)
        localStorage.setItem('dx7_user',   JSON.stringify(this.user))
        localStorage.setItem('dx7_tenant', JSON.stringify(this.tenant))
        localStorage.setItem('dx7_client', JSON.stringify(this.client))
        return true
      } catch (err) {
        this.error = err.response?.data?.message || 'Login failed'
        return false
      } finally {
        this.loading = false
      }
    },

    setSession(data) {
      this.token  = data.token
      this.user   = data.user
      this.tenant = data.tenant
      this.client = data.client
      localStorage.setItem('dx7_token',  data.token)
      localStorage.setItem('dx7_user',   JSON.stringify(data.user))
      localStorage.setItem('dx7_tenant', JSON.stringify(data.tenant))
      localStorage.setItem('dx7_client', JSON.stringify(data.client))
      // Fetch roles from DB after login
      this.fetchRoles()
    },

    async fetchRoles() {
      try {
        const api = (await import('../services/api')).default
        const { data } = await api.get('/roles')
        this.roles = data
        localStorage.setItem('dx7_roles', JSON.stringify(data))
      } catch (e) {
        console.warn('Could not load roles from DB:', e)
      }
    },

    logout() {
      this.token = null
      this.user = null
      this.tenant = null
      this.client = null
      localStorage.removeItem('dx7_token')
      localStorage.removeItem('dx7_user')
      localStorage.removeItem('dx7_tenant')
      localStorage.removeItem('dx7_client')
      localStorage.removeItem('dx7_roles')
      this.roles = []
    }
  }
})