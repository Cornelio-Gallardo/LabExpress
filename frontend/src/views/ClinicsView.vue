<template>
  <div>
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">Clinics</div>
          <div class="page-sub">{{ auth.tenant?.name }} · Manage dialysis centers</div>
        </div>
        <button class="btn btn-primary" @click="openCreate">+ Add Clinic</button>
      </div>
    </div>

    <div v-if="loading" class="loading">Loading clinics…</div>

    <div v-else class="card">
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Code</th>
              <th>Address</th>
              <th>Users</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="c in clinics" :key="c.id">
              <td style="font-weight:500">{{ c.name }}</td>
              <td><span style="font-family:monospace; background:var(--off-white); padding:2px 8px; border-radius:4px; font-size:12px">{{ c.code }}</span></td>
              <td class="text-slate text-sm">{{ c.address || '—' }}</td>
              <td class="text-sm">{{ c.userCount }} users</td>
              <td>
                <span class="badge" :class="c.isActive ? 'badge-ready' : 'badge-nodata'">
                  {{ c.isActive ? '● Active' : '○ Inactive' }}
                </span>
              </td>
              <td>
                <div class="flex gap-2">
                  <button class="btn btn-outline btn-sm" @click="openEdit(c)">Edit</button>
                  <button class="btn btn-sm" :class="c.isActive ? 'btn-danger' : 'btn-primary'" @click="toggleActive(c)">
                    {{ c.isActive ? 'Deactivate' : 'Activate' }}
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="clinics.length === 0">
              <td colspan="6" style="text-align:center; padding:32px; color:var(--slate)">No clinics found</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Create/Edit Modal -->
    <div v-if="showModal" class="modal-backdrop" @click.self="showModal = false">
      <div class="modal-box">
        <div class="modal-header">
          <div class="card-title">{{ editingClinic ? 'Edit Clinic' : 'Add New Clinic' }}</div>
          <button class="btn btn-outline btn-sm" @click="showModal = false">✕</button>
        </div>
        <div style="padding:22px">
          <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px">
            <div class="form-group" style="grid-column:1/-1">
              <label class="form-label">Clinic Name *</label>
              <input v-model="form.name" class="form-input" placeholder="Metro Dialysis Center" />
            </div>
            <div class="form-group">
              <label class="form-label">Clinic Code *</label>
              <input v-model="form.code" class="form-input" placeholder="MDC" style="text-transform:uppercase" />
              <div class="text-slate" style="font-size:11px; margin-top:4px">Short unique identifier (e.g. MDC, QCC)</div>
            </div>
            <div class="form-group">
              <label class="form-label">Contact Number</label>
              <input v-model="form.contactNumber" class="form-input" placeholder="+63 2 1234 5678" />
            </div>
            <div class="form-group" style="grid-column:1/-1">
              <label class="form-label">Address</label>
              <input v-model="form.address" class="form-input" placeholder="123 Health St., Manila" />
            </div>
            <div class="form-group" style="grid-column:1/-1">
              <label class="form-label">Logo URL (optional)</label>
              <input v-model="form.logoUrl" class="form-input" placeholder="https://..." />
            </div>
          </div>

          <div v-if="formError" style="color:var(--red); font-size:13px; margin-bottom:12px; background:rgba(239,68,68,0.08); padding:10px 14px; border-radius:6px">
            {{ formError }}
          </div>

          <div class="flex gap-2" style="justify-content:flex-end; margin-top:8px">
            <button class="btn btn-outline" @click="showModal = false">Cancel</button>
            <button class="btn btn-primary" @click="saveClinic" :disabled="saving">
              {{ saving ? 'Saving…' : editingClinic ? 'Save Changes' : 'Create Clinic' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { useAuthStore } from '../store/auth'
import api from '../services/api'

const auth = useAuthStore()
const clinics = ref([])
const loading = ref(false)
const showModal = ref(false)
const editingClinic = ref(null)
const saving = ref(false)
const formError = ref('')

const form = reactive({ name: '', code: '', address: '', contactNumber: '', logoUrl: '' })

async function load() {
  loading.value = true
  try {
    const { data } = await api.get('/clinics')
    clinics.value = data
  } finally { loading.value = false }
}

function openCreate() {
  editingClinic.value = null
  form.name = ''; form.code = ''; form.address = ''; form.contactNumber = ''; form.logoUrl = ''
  formError.value = ''
  showModal.value = true
}

function openEdit(c) {
  editingClinic.value = c
  form.name = c.name; form.code = c.code; form.address = c.address || ''
  form.contactNumber = c.contactNumber || ''; form.logoUrl = c.logoUrl || ''
  formError.value = ''
  showModal.value = true
}

async function saveClinic() {
  formError.value = ''
  if (!form.name || !form.code) { formError.value = 'Name and code are required'; return }
  saving.value = true
  try {
    if (editingClinic.value) {
      await api.patch(`/clinics/${editingClinic.value.id}`, form)
    } else {
      await api.post('/clinics', form)
    }
    showModal.value = false
    await load()
  } catch (err) {
    formError.value = err.response?.data?.message || 'An error occurred'
  } finally { saving.value = false }
}

async function toggleActive(c) {
  if (c.isActive && !confirm(`Deactivate ${c.name}? Users in this clinic will lose access.`)) return
  await api.patch(`/clinics/${c.id}/${c.isActive ? 'deactivate' : 'activate'}`)
  await load()
}

onMounted(load)
</script>

<style scoped>
.modal-backdrop { position:fixed; inset:0; background:rgba(0,0,0,0.5); display:flex; align-items:center; justify-content:center; z-index:1000; }
.modal-box { background:white; border-radius:12px; width:520px; max-height:85vh; overflow-y:auto; box-shadow:0 24px 80px rgba(0,0,0,0.3); }
.modal-header { display:flex; align-items:center; justify-content:space-between; padding:18px 22px; border-bottom:1px solid var(--border); }
</style>
