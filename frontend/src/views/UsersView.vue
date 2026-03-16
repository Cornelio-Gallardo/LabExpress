<template>
  <div>
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">User Management</div>
          <div class="page-sub">{{ auth.client?.name || auth.tenant?.name }} · Manage staff accounts and roles</div>
        </div>
        <button class="btn btn-primary" @click="openCreate">+ Add User</button>
      </div>
    </div>

    <!-- Filters -->
    <div class="card" style="padding:14px 20px; margin-bottom:16px">
      <div class="flex gap-3">
        <input v-model="search" class="form-input" style="max-width:280px; margin:0" placeholder="🔍  Search by name or email…" autocomplete="new-password" readonly @focus="($event.target.removeAttribute('readonly'))" />
        <select v-model="roleFilter" class="form-input" style="max-width:180px; margin:0">
          <option value="">All Roles</option>
          <option v-for="r in availableRoles" :key="r.value" :value="r.value">{{ r.label }}</option>
        </select>
        <select v-model="statusFilter" class="form-input" style="max-width:150px; margin:0">
          <option value="">All Status</option>
          <option value="active">Active</option>
          <option value="inactive">Inactive</option>
        </select>
        <div class="text-slate text-sm" style="margin-left:auto; align-self:center">
          {{ filtered.length }} user{{ filtered.length !== 1 ? 's' : '' }}
        </div>
      </div>
    </div>

    <!-- Users table -->
    <div v-if="loading" class="loading">Loading users…</div>
    <div v-else class="card">
      <div v-if="selectedUsers.length" class="bulk-bar">
        <span class="bulk-count">{{ selectedUsers.length }} selected</span>
        <button class="btn btn-outline btn-sm" @click="selectedUsers = []">✕ Deselect</button>
      </div>
      <div class="table-card">
        <div class="table-wrap">
          <table>
          <thead>
            <tr>
              <th style="width:36px; text-align:center"><input type="checkbox" :checked="allUsersSelected" @change="toggleSelectAllUsers" class="row-check" /></th>
              <th>Name</th>
              <th>Role</th>
              <th>Clinic</th>
              <th>Status</th>
              <th>Created</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="u in filtered" :key="u.id">
              <td style="text-align:center"><input type="checkbox" :value="u.id" v-model="selectedUsers" class="row-check" /></td>
              <td>
                <div class="user-avatar-row">
                  <div class="user-avatar-wrap" @click="auth.isAdmin && triggerAvatarUpload(u)" :style="auth.isAdmin ? 'cursor:pointer' : ''">
                    <img v-if="u.avatarUrl" :src="u.avatarUrl" class="user-avatar user-avatar-img" :alt="u.name" />
                    <div v-else class="user-avatar" :style="{ background: avatarBg(u.name) }">{{ initials(u.name) }}</div>
                    <div v-if="auth.isAdmin" class="avatar-overlay">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
                    </div>
                  </div>
                  <div>
                    <div style="font-weight:600; color:#111827">{{ u.name }}</div>
                    <div style="font-size:11px; color:var(--slate)">{{ u.email }}</div>
                    <div v-if="u.id === auth.user?.id" style="font-size:11px; color:var(--teal); font-weight:600">● You</div>
                    <button v-if="auth.isAdmin && u.avatarUrl" class="avatar-remove-btn" @click.stop="removeAvatar(u)">✕ Remove photo</button>
                  </div>
                </div>
                <input type="file" :data-uid="u.id" class="avatar-file-input" accept="image/jpeg,image/png,image/webp" style="display:none" @change="onAvatarFile($event, u)" />
              </td>
              <td>
                <span class="role-badge" :class="`role-${u.role}`">
                  {{ roleLabel(u.role) }}
                </span>
              </td>
              <td class="text-sm text-slate">{{ u.clientName || '— (PL level)' }}</td>
              <td>
                <span class="badge" :class="u.isActive ? 'badge-ready' : 'badge-nodata'">
                  {{ u.isActive ? '● Active' : '○ Inactive' }}
                </span>
              </td>
              <td class="text-sm text-slate">{{ formatDate(u.createdAt) }}</td>
              <td>
                <div class="flex gap-2">
                  <button class="action-btn edit" title="Edit" @click="openEdit(u)">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                  </button>
                  <button v-if="u.id !== auth.user?.id && !u.isActive" class="btn btn-primary btn-sm" @click="toggleActive(u)">Activate</button>
                  <button v-if="u.id !== auth.user?.id && u.isActive" class="action-btn delete" title="Deactivate" @click="toggleActive(u)">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/></svg>
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="filtered.length === 0">
              <td colspan="7" style="text-align:center; padding:32px; color:var(--slate)">No users found</td>
            </tr>
          </tbody>
          </table>
        </div>
        <div class="table-footer">
          <span>Showing {{ filtered.length }} of {{ users.length }} user{{ users.length !== 1 ? 's' : '' }}</span>
        </div>
      </div>
    </div>

    <!-- Role legend -->
    <div class="card mt-6" style="padding:20px 24px">
      <div class="card-title" style="margin-bottom:14px">Role Permissions</div>
      <div style="display:grid; grid-template-columns:repeat(3,1fr); gap:12px">
        <div v-for="r in allRoles" :key="r.value" class="role-card">
          <div class="flex gap-2" style="align-items:center; margin-bottom:6px">
            <span class="role-badge" :class="`role-${r.value}`">{{ r.label }}</span>
          </div>
          <div class="text-sm text-slate">{{ r.description }}</div>
        </div>
      </div>
    </div>

    <!-- Change own password -->
    <div class="card mt-6" style="padding:20px 24px; max-width:400px">
      <div class="card-title" style="margin-bottom:14px">Change My Password</div>
      <div class="form-group">
        <label class="form-label">Current Password</label>
        <input v-model="pwForm.current" type="password" class="form-input" />
      </div>
      <div class="form-group">
        <label class="form-label">New Password</label>
        <input v-model="pwForm.newPw" type="password" class="form-input" />
      </div>
      <div class="form-group">
        <label class="form-label">Confirm New Password</label>
        <input v-model="pwForm.confirm" type="password" class="form-input" />
      </div>
      <div v-if="pwError" style="color:var(--red); font-size:13px; margin-bottom:10px">{{ pwError }}</div>
      <div v-if="pwSuccess" style="color:var(--green); font-size:13px; margin-bottom:10px">✅ Password changed successfully!</div>
      <button class="btn btn-primary" @click="changePassword" :disabled="!pwForm.current || !pwForm.newPw">
        Update Password
      </button>
    </div>

    <!-- Create / Edit Modal -->
    <div v-if="showModal" class="modal-backdrop" @click.self="closeModal">
      <div class="modal-box">
        <div class="modal-header">
          <div class="card-title">{{ editingUser ? 'Edit User' : 'Add New User' }}</div>
          <button class="btn btn-outline btn-sm" @click="closeModal">✕</button>
        </div>
        <div style="padding:22px">

          <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px">
            <div class="form-group" style="grid-column:1/-1">
              <label class="form-label">Full Name *</label>
              <input v-model="form.name" class="form-input" placeholder="Juan Dela Cruz" />
            </div>

            <div class="form-group" style="grid-column:1/-1">
              <label class="form-label">Email Address *</label>
              <input v-model="form.email" type="email" class="form-input" placeholder="juan@clinic.com" />
            </div>

            <div class="form-group">
              <label class="form-label">Role *</label>
              <select v-model="form.role" class="form-input">
                <option value="">— Select Role —</option>
                <option v-for="r in availableRoles" :key="r.value" :value="r.value">{{ r.label }}</option>
              </select>
            </div>

            <div class="form-group" v-if="auth.isPlAdmin && clinics.length > 0">
              <label class="form-label">Assign to Clinic</label>
              <select v-model="form.clientId" class="form-input">
                <option value="">— No clinic (PL level) —</option>
                <option v-for="c in clinics" :key="c.id" :value="c.id">{{ c.name }}</option>
              </select>
            </div>

            <div class="form-group">
              <label class="form-label">{{ editingUser ? 'New Password (leave blank to keep)' : 'Password *' }}</label>
              <input v-model="form.password" type="password" class="form-input" :placeholder="editingUser ? 'Leave blank to keep current' : 'Min. 8 characters'" />
            </div>
          </div>

          <!-- Role description -->
          <div v-if="form.role" class="doctrine-bar" style="margin-bottom:16px">
            {{ allRoles.find(r => r.value === form.role)?.description }}
          </div>

          <div v-if="formError" style="color:var(--red); font-size:13px; margin-bottom:12px; background:rgba(239,68,68,0.08); padding:10px 14px; border-radius:6px">
            {{ formError }}
          </div>

          <div class="flex gap-2" style="justify-content:flex-end; margin-top:8px">
            <button class="btn btn-outline" @click="closeModal">Cancel</button>
            <button class="btn btn-primary" @click="saveUser" :disabled="saving">
              {{ saving ? 'Saving…' : editingUser ? 'Save Changes' : 'Create User' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, reactive } from 'vue'
import { useAuthStore } from '../store/auth'
import api, { usersApi } from '../services/api'
import { useDialog } from '../composables/useDialog'
const dialog = useDialog()

const auth = useAuthStore()
const users = ref([])
const loading = ref(false)
const search = ref('')
const roleFilter = ref('')
const statusFilter = ref('')
const showModal = ref(false)
const editingUser = ref(null)
const saving = ref(false)
const formError = ref('')
const pwError = ref('')
const pwSuccess = ref(false)

const form = reactive({ name: '', email: '', password: '', role: '', clientId: '' })
const clinics = ref([])
const pwForm = reactive({ current: '', newPw: '', confirm: '' })

const allRoles = ref([])

async function loadRoles() {
  try {
    const { data } = await api.get('/roles')
    allRoles.value = data.map(r => ({
      value: r.roleKey,
      label: r.label,
      description: r.description
    }))
  } catch {
    // fallback if API fails
    allRoles.value = [
      { value: 'charge_nurse', label: 'Charge Nurse', description: 'Manages shift roster, assigns chairs, views all results, exports reports' },
      { value: 'shift_nurse',  label: 'Shift Nurse',  description: 'Views patient results and MD notes. Read-only access.' },
      { value: 'md',           label: 'Nephrologist', description: 'Views results and writes/edits session notes (24hr edit window)' },
      { value: 'clinic_admin', label: 'Clinic Admin', description: 'Full access to clinic: manage users, patients, sessions, export' },
      { value: 'pl_admin',     label: 'PL Admin',     description: 'Partner Lab admin — manages all clinics under the tenant' },
    ]
  }
}

// Clinic admin can't create pl_admin
const availableRoles = computed(() =>
  auth.isAdmin && !auth.user?.role?.includes('pl') && !auth.user?.role?.includes('sysad')
    ? allRoles.value.filter(r => !['pl_admin', 'sysad'].includes(r.value))
    : allRoles.value
)

const filtered = computed(() =>
  users.value.filter(u => {
    const s = search.value.toLowerCase()
    const matchSearch = !s || u.name.toLowerCase().includes(s) || u.email.toLowerCase().includes(s)
    const matchRole = !roleFilter.value || u.role === roleFilter.value
    const matchStatus = !statusFilter.value ||
      (statusFilter.value === 'active' && u.isActive) ||
      (statusFilter.value === 'inactive' && !u.isActive)
    return matchSearch && matchRole && matchStatus
  })
)

const selectedUsers = ref([])
const allUsersSelected = computed(() =>
  filtered.value.length > 0 && filtered.value.every(u => selectedUsers.value.includes(u.id))
)
function toggleSelectAllUsers(evt) {
  selectedUsers.value = evt.target.checked ? filtered.value.map(u => u.id) : []
}

function triggerAvatarUpload(user) {
  const input = document.querySelector(`.avatar-file-input[data-uid="${user.id}"]`)
  input?.click()
}

async function onAvatarFile(evt, user) {
  const file = evt.target.files[0]
  if (!file) return
  try {
    const { data } = await usersApi.uploadAvatar(user.id, file)
    user.avatarUrl = data.avatarUrl + '?t=' + Date.now()
  } catch (e) {
    await dialog.alert('Upload failed: ' + (e.response?.data || e.message), 'Upload Failed')
  }
  evt.target.value = ''
}

async function removeAvatar(user) {
  if (!await dialog.confirm('Remove this photo?', 'Remove Photo')) return
  try {
    await usersApi.deleteAvatar(user.id)
    user.avatarUrl = null
  } catch (e) {
    await dialog.alert('Failed: ' + (e.response?.data || e.message), 'Error')
  }
}

function initials(name) {
  if (!name) return '?'
  const parts = name.trim().split(/\s+/)
  return parts.length >= 2
    ? (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
    : name.slice(0, 2).toUpperCase()
}

const avatarPalette = [
  '#1d4ed8','#0891b2','#059669','#7c3aed',
  '#db2777','#d97706','#dc2626','#0284c7'
]
function avatarBg(name) {
  if (!name) return '#6b7280'
  let hash = 0
  for (const ch of name) hash = (hash * 31 + ch.charCodeAt(0)) & 0xffffffff
  return avatarPalette[Math.abs(hash) % avatarPalette.length]
}

function roleLabel(role) {
  return allRoles.value.find(r => r.value === role)?.label || role
}

function formatDate(dt) {
  return new Date(dt).toLocaleDateString('en-PH', { dateStyle: 'medium' })
}

async function loadClinics() {
  if (!auth.isPlAdmin) return
  try {
    const { data } = await api.get('/clinics')
    clinics.value = data.filter(c => c.isActive)
  } catch {}
}

async function load() {
  loading.value = true
  try {
    const { data } = await api.get('/users')
    users.value = data
  } finally { loading.value = false }
}

function openCreate() {
  editingUser.value = null
  form.name = ''
  form.email = ''
  form.password = ''
  form.role = ''
  form.clientId = auth.client?.id || ''
  formError.value = ''
  showModal.value = true
}

function openEdit(u) {
  editingUser.value = u
  form.name = u.name
  form.email = u.email
  form.password = ''
  form.role = u.role
  form.clientId = u.clientId || ''
  formError.value = ''
  showModal.value = true
}

function closeModal() {
  showModal.value = false
  editingUser.value = null
}

async function saveUser() {
  formError.value = ''
  if (!form.name || !form.email || !form.role) {
    formError.value = 'Name, email and role are required'
    return
  }
  if (!editingUser.value && !form.password) {
    formError.value = 'Password is required for new users'
    return
  }
  if (form.password && form.password.length < 8) {
    formError.value = 'Password must be at least 8 characters'
    return
  }

  saving.value = true
  try {
    if (editingUser.value) {
      const payload = {}
      if (form.name !== editingUser.value.name) payload.name = form.name
      if (form.email !== editingUser.value.email) payload.email = form.email
      if (form.role !== editingUser.value.role) payload.role = form.role
      if (form.password) payload.password = form.password
      if (form.clientId !== editingUser.value.clientId) payload.clientId = form.clientId || null
      await api.patch(`/users/${editingUser.value.id}`, payload)
    } else {
      await api.post('/users', {
        name: form.name,
        email: form.email,
        password: form.password,
        role: form.role,
        clientId: form.clientId || auth.client?.id || null
      })
    }
    closeModal()
    await load()
  } catch (err) {
    formError.value = err.response?.data?.message || 'An error occurred'
  } finally { saving.value = false }
}

async function toggleActive(u) {
  const action = u.isActive ? 'deactivate' : 'activate'
  if (u.isActive && !await dialog.confirm(`Deactivate ${u.name}? They will not be able to log in.`, 'Deactivate User')) return
  await api.patch(`/users/${u.id}/${action}`)
  await load()
}

async function changePassword() {
  pwError.value = ''
  pwSuccess.value = false
  if (pwForm.newPw !== pwForm.confirm) {
    pwError.value = 'New passwords do not match'
    return
  }
  if (pwForm.newPw.length < 8) {
    pwError.value = 'Password must be at least 8 characters'
    return
  }
  try {
    await api.patch('/users/me/password', {
      currentPassword: pwForm.current,
      newPassword: pwForm.newPw
    })
    pwForm.current = ''
    pwForm.newPw = ''
    pwForm.confirm = ''
    pwSuccess.value = true
  } catch (err) {
    pwError.value = err.response?.data?.message || 'Failed to change password'
  }
}

onMounted(async () => {
  search.value = ''
  await Promise.all([load(), loadClinics(), loadRoles()])
  // clear again after load in case browser autofills async
  setTimeout(() => { search.value = '' }, 100)
})
</script>

<style scoped>
.bulk-bar { display:flex; align-items:center; gap:10px; padding:8px 16px; background:#fef9c3; border-bottom:1px solid #fde68a; font-size:13px; }
.bulk-count { font-weight:600; color:#92400e; }
.row-check { width:15px; height:15px; cursor:pointer; }
.role-badge {
  display: inline-flex;
  align-items: center;
  padding: 3px 10px;
  border-radius: 20px;
  font-size: 11.5px;
  font-weight: 600;
}
.role-charge_nurse  { background: #e0f2fe; color: #0369a1; }
.role-shift_nurse   { background: #f0fdf4; color: #166534; }
.role-md            { background: #fdf4ff; color: #7e22ce; }
.role-clinic_admin  { background: #fff7ed; color: #c2410c; }
.role-pl_admin      { background: #0A1628; color: #94a3b8; }
.role-sysad         { background: #1e293b; color: #f8fafc; }

.role-card {
  background: var(--off-white);
  border: 1.5px solid var(--border);
  border-radius: 8px;
  padding: 14px 16px;
}

.modal-backdrop {
  position: fixed; inset: 0;
  background: rgba(0,0,0,0.5);
  display: flex; align-items: center; justify-content: center;
  z-index: 1000;
}
.modal-box {
  background: white;
  border-radius: 12px;
  width: 520px;
  max-height: 85vh;
  overflow-y: auto;
  box-shadow: 0 24px 80px rgba(0,0,0,0.3);
}
.modal-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 18px 22px;
  border-bottom: 1px solid var(--border);
}
.user-avatar-row { display: flex; align-items: center; gap: 10px; }
.user-avatar-wrap { position: relative; flex-shrink: 0; }
.user-avatar {
  width: 38px; height: 38px; border-radius: 50%;
  display: flex; align-items: center; justify-content: center;
  font-size: 13px; font-weight: 700; color: white;
  letter-spacing: 0.5px; box-shadow: 0 2px 6px rgba(0,0,0,0.15);
}
.user-avatar-img { width: 38px; height: 38px; border-radius: 50%; object-fit: cover; }
.avatar-overlay {
  position: absolute; inset: 0; border-radius: 50%;
  background: rgba(0,0,0,0.45);
  display: flex; align-items: center; justify-content: center;
  opacity: 0; transition: opacity 0.15s;
}
.user-avatar-wrap:hover .avatar-overlay { opacity: 1; }
.avatar-remove-btn {
  margin-top: 3px; font-size: 10px; color: var(--red);
  background: none; border: none; cursor: pointer; padding: 0;
  font-family: 'Inter', sans-serif;
}
.avatar-remove-btn:hover { text-decoration: underline; }
</style>