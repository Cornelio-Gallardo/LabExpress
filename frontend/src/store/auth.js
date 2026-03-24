import { defineStore } from 'pinia'
import { authApi } from '../services/api'

// F-09: httpOnly Cookie pattern.
// The JWT is stored exclusively in an httpOnly, Secure, SameSite=Lax cookie set by the
// server on login. JavaScript never reads, writes, or transmits the token — the browser
// sends it automatically via withCredentials on every API request.
// Profile data (non-secret) is mirrored in sessionStorage so the UI survives page reload;
// it is cleared on logout and never contains the credential itself.

export const useAuthStore = defineStore('auth', {
  state: () => ({
    // No token field — credential lives only in the httpOnly cookie
    user:   JSON.parse(sessionStorage.getItem('dx7_user')   || 'null'),
    tenant: JSON.parse(sessionStorage.getItem('dx7_tenant') || 'null'),
    client: JSON.parse(sessionStorage.getItem('dx7_client') || 'null'),
    roles:  JSON.parse(sessionStorage.getItem('dx7_roles')  || '[]'),
    loading: false,
    error: null
  }),

  getters: {
    // Session is active when profile data is present (backed by httpOnly cookie on server)
    isLoggedIn: (s) => !!s.user,
    role: (s) => s.user?.role || '',

    // Role checks
    isSysAd:      (s) => s.user?.role === 'sysad',
    isPlAdmin:     (s) => s.user?.role === 'pl_admin',
    isClinicAdmin: (s) => s.user?.role === 'clinic_admin',
    isChargeNurse: (s) => s.user?.role === 'charge_nurse',
    isShiftNurse:  (s) => s.user?.role === 'shift_nurse',
    isMd:          (s) => s.user?.role === 'md',

    isAdmin:   (s) => ['clinic_admin', 'pl_admin'].includes(s.user?.role),
    isClinical:(s) => ['charge_nurse', 'shift_nurse', 'md'].includes(s.user?.role),

    // Per PRD Role Permissions Matrix
    canSelectShift:    (s) => ['charge_nurse', 'shift_nurse', 'md', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canSelectPatients: (s) => ['charge_nurse', 'shift_nurse', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canAssignChairs:   (s) => ['charge_nurse', 'shift_nurse'].includes(s.user?.role),
    canViewResults:    (s) => ['charge_nurse', 'shift_nurse', 'md'].includes(s.user?.role),
    canViewNotes:      (s) => ['charge_nurse', 'shift_nurse', 'md'].includes(s.user?.role),
    canWriteNotes:     (s) => s.user?.role === 'md',
    canExport:         (s) => ['charge_nurse', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canPrint:          (s) => ['charge_nurse', 'md', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canManageSession:  (s) => ['charge_nurse', 'shift_nurse', 'clinic_admin', 'pl_admin'].includes(s.user?.role),
    canManageUsers:    (s) => ['clinic_admin', 'pl_admin'].includes(s.user?.role),
    canManageClinics:  (s) => s.user?.role === 'pl_admin',

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
        // Server sets httpOnly cookie; response body carries only profile data (no token)
        const { data } = await authApi.login(email, password)
        const normaliseClient = (c) => c ? { ...c, id: c.id || c.Id } : null
        const normaliseUser   = (u) => u ? { ...u, id: u.id || u.Id } : null
        this.user   = normaliseUser(data.user)
        this.tenant = data.tenant
        this.client = normaliseClient(data.client)
        sessionStorage.setItem('dx7_user',   JSON.stringify(this.user))
        sessionStorage.setItem('dx7_tenant', JSON.stringify(this.tenant))
        sessionStorage.setItem('dx7_client', JSON.stringify(this.client))
        this.fetchRoles()
        return true
      } catch (err) {
        this.error = err.response?.data?.message || 'Login failed'
        return false
      } finally {
        this.loading = false
      }
    },

    // Called after SSO external login (Google / Facebook)
    setSession(data) {
      const normaliseClient = (c) => c ? { ...c, id: c.id || c.Id } : null
      const normaliseUser   = (u) => u ? { ...u, id: u.id || u.Id } : null
      this.user   = normaliseUser(data.user)
      this.tenant = data.tenant
      this.client = normaliseClient(data.client)
      sessionStorage.setItem('dx7_user',   JSON.stringify(this.user))
      sessionStorage.setItem('dx7_tenant', JSON.stringify(this.tenant))
      sessionStorage.setItem('dx7_client', JSON.stringify(this.client))
      this.fetchRoles()
    },

    async fetchRoles() {
      try {
        const api = (await import('../services/api')).default
        const { data } = await api.get('/roles')
        this.roles = data
        sessionStorage.setItem('dx7_roles', JSON.stringify(data))
      } catch (e) {
        console.warn('Could not load roles from DB:', e)
      }
    },

    async logout() {
      try {
        // Ask server to expire the httpOnly cookie — browser cannot do this itself
        const api = (await import('../services/api')).default
        await api.post('/auth/logout')
      } catch { /* ignore network errors on logout */ }

      this.user   = null
      this.tenant = null
      this.client = null
      this.roles  = []
      sessionStorage.removeItem('dx7_user')
      sessionStorage.removeItem('dx7_tenant')
      sessionStorage.removeItem('dx7_client')
      sessionStorage.removeItem('dx7_roles')
      // Remove stale localStorage entries from versions prior to the cookie migration
      localStorage.removeItem('dx7_token')
      localStorage.removeItem('dx7_user')
      localStorage.removeItem('dx7_tenant')
      localStorage.removeItem('dx7_client')
      localStorage.removeItem('dx7_roles')
    }
  }
})
