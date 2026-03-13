<template>
  <div>
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">HL7 Inbox</div>
          <div class="page-sub">Processed lab order files from LIS / Sysmex</div>
        </div>
        <div class="flex gap-2">
          <button class="btn btn-outline" @click="load" :disabled="loading">
            {{ loading ? 'Refreshing...' : '↺ Refresh' }}
          </button>
          <label class="btn btn-primary" style="cursor:pointer">
            ⬆ Upload HL7
            <input type="file" accept=".hl7" multiple style="display:none" @change="uploadFiles" />
          </label>
        </div>
      </div>
    </div>

    <!-- Stat cards -->
    <div class="stat-grid" style="margin-bottom:20px">
      <div class="stat-card">
        <div class="stat-icon">📥</div>
        <div class="stat-body">
          <div class="stat-value">{{ status.pending ?? 0 }}</div>
          <div class="stat-label">Pending</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" style="color:var(--green)">✅</div>
        <div class="stat-body">
          <div class="stat-value" style="color:var(--green)">{{ status.processed ?? 0 }}</div>
          <div class="stat-label">Processed</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" style="color:var(--red)">❌</div>
        <div class="stat-body">
          <div class="stat-value" style="color:var(--red)">{{ status.errored ?? 0 }}</div>
          <div class="stat-label">Errors</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon">🏢</div>
        <div class="stat-body">
          <div class="stat-value">{{ (status.tenantFolders ?? []).length }}</div>
          <div class="stat-label">Tenant Folders</div>
        </div>
      </div>
    </div>

    <!-- Upload result -->
    <div v-if="uploadResults.length" class="card" style="margin-bottom:16px; padding:16px 20px">
      <div class="card-title" style="margin-bottom:12px">Upload Results</div>
      <div v-for="r in uploadResults" :key="r.file" class="upload-result-row">
        <span class="status-pill" :class="pillClass(r.status)">{{ r.status }}</span>
        <span style="font-weight:500; font-size:13px">{{ r.file }}</span>
        <span class="text-slate text-sm">{{ r.patient || '' }}</span>
        <span class="text-slate text-sm">{{ r.notes }}</span>
        <span v-if="r.saved" style="color:var(--green); font-size:12px; font-weight:600">{{ r.saved }} saved</span>
      </div>
    </div>

    <!-- Log table -->
    <div class="card">
      <div class="card-header">
        <div>
          <div class="card-title">Processing Log</div>
          <div class="card-subtitle">Most recent {{ logEntries.length }} entries</div>
        </div>
        <div class="flex gap-2">
          <input v-model="logSearch" class="form-input" style="width:220px; margin:0" placeholder="Search log..." />
          <select v-model="logStatusFilter" class="form-input" style="width:150px; margin:0">
            <option value="">All Status</option>
            <option value="order_saved">Order Saved</option>
            <option value="processed">Processed</option>
            <option value="duplicate">Duplicate</option>
            <option value="error">Error</option>
          </select>
        </div>
      </div>

      <template v-if="loadingLog">
        <div class="loading">Loading log...</div>
      </template>
      <template v-else-if="filteredLog.length === 0">
        <div class="empty-state">
          <div class="empty-icon">📭</div>
          <div class="empty-title">No log entries yet</div>
          <div class="text-slate text-sm">Drop HL7 files into the inbox folder or upload above</div>
        </div>
      </template>
      <template v-else>
        <div class="table-card">
          <div class="table-wrap">
            <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>Status</th>
                <th>Msg Type</th>
                <th>Patient</th>
                <th>Accession</th>
                <th>Saved</th>
                <th>File</th>
                <th>Notes</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(e, i) in filteredLog" :key="i">
                <td class="text-sm" style="white-space:nowrap; font-family:monospace">{{ e.timestamp }}</td>
                <td>
                  <span class="status-pill" :class="pillClass(e.status)">{{ e.status }}</span>
                </td>
                <td class="text-sm" style="font-family:monospace">{{ e.msgType || '—' }}</td>
                <td class="text-sm" style="font-weight:500">{{ cleanPatient(e.patient) || '—' }}</td>
                <td class="text-sm" style="font-family:monospace">{{ cleanAcc(e.accession) || '—' }}</td>
                <td class="text-sm" style="text-align:center">
                  <span v-if="savedCount(e.saved) > 0" style="color:var(--green); font-weight:600">
                    {{ savedCount(e.saved) }}
                  </span>
                  <span v-else class="text-slate">—</span>
                </td>
                <td class="text-sm text-slate" style="max-width:180px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap">{{ e.file }}</td>
                <td class="text-sm text-slate" style="max-width:240px">{{ e.notes }}</td>
              </tr>
            </tbody>
          </table>
        </div>
          <div class="table-footer">
            <span>Showing {{ filteredLog.length }} record{{ filteredLog.length !== 1 ? 's' : '' }}</span>
          </div>
        </div>
      </template>

      <div style="padding:12px 20px; border-top:1px solid var(--border); display:flex; justify-content:space-between; align-items:center">
        <div class="text-slate text-sm">{{ filteredLog.length }} entries shown</div>
        <div class="text-slate text-sm">Inbox: <span style="font-family:monospace">{{ status.inboxPath }}</span></div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import api from '../services/api'

const loading    = ref(false)
const loadingLog = ref(false)
const status     = ref({})
const logEntries = ref([])
const uploadResults = ref([])
const logSearch  = ref('')
const logStatusFilter = ref('')
let autoRefresh  = null

const filteredLog = computed(() => {
  let entries = logEntries.value
  if (logStatusFilter.value)
    entries = entries.filter(e => e.status?.trim() === logStatusFilter.value)
  if (logSearch.value) {
    const s = logSearch.value.toLowerCase()
    entries = entries.filter(e =>
      e.patient?.toLowerCase().includes(s) ||
      e.file?.toLowerCase().includes(s) ||
      e.accession?.toLowerCase().includes(s) ||
      e.notes?.toLowerCase().includes(s)
    )
  }
  return entries
})

async function load() {
  loading.value = true
  try {
    const [statusRes, logRes] = await Promise.all([
      api.get('/hl7/inbox/status'),
      api.get('/hl7/log?lines=200')
    ])
    status.value     = statusRes.data
    logEntries.value = logRes.data.entries || []

    // Derive counts from log if folder counts are zero
    if (!status.value.processed && logEntries.value.length > 0) {
      status.value = {
        ...status.value,
        processed: logEntries.value.filter(e => e.status?.trim() === 'order_saved' || e.status?.trim() === 'processed').length,
        errored:   logEntries.value.filter(e => e.status?.trim() === 'error').length,
      }
    }
  } catch (e) {
    console.error('HL7 load error', e)
  } finally {
    loading.value = false
  }
}

async function uploadFiles(e) {
  const files = Array.from(e.target.files)
  if (!files.length) return
  uploadResults.value = []

  const form = new FormData()
  files.forEach(f => form.append('files', f))

  try {
    const { data } = await api.post('/hl7/upload', form, {
      headers: { 'Content-Type': 'multipart/form-data' }
    })
    uploadResults.value = data.results || []
    await load()
  } catch (err) {
    uploadResults.value = files.map(f => ({
      file: f.name,
      status: 'error',
      notes: err.response?.data?.message || err.message
    }))
  }

  // Reset input so same file can be uploaded again
  e.target.value = ''
}

function pillClass(status) {
  const s = (status || '').trim()
  if (s === 'processed' || s === 'order_saved') return 'pill-ready'
  if (s === 'error') return 'pill-error'
  if (s === 'duplicate') return 'pill-dupe'
  if (s === 'order_received' || s === 'order_only') return 'pill-order'
  return 'pill-neutral'
}

function cleanPatient(p) {
  return (p || '').replace(/^Patient:\s*/i, '').trim()
}

function cleanAcc(a) {
  return (a || '').replace(/^Acc:\s*/i, '').trim()
}

function savedCount(s) {
  const match = (s || '').match(/\d+/)
  return match ? parseInt(match[0]) : 0
}

onMounted(() => {
  load()
  autoRefresh = setInterval(load, 15000) // auto refresh every 15s
})
onUnmounted(() => clearInterval(autoRefresh))
</script>

<style scoped>
.stat-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; }
.stat-card { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 18px 20px; display: flex; align-items: center; gap: 14px; box-shadow: var(--shadow-sm); }
.stat-icon { font-size: 24px; }
.stat-value { font-family: 'Plus Jakarta Sans', sans-serif; font-size: 26px; font-weight: 800; color: var(--navy); line-height: 1; }
.stat-label { font-size: 12px; color: var(--slate); margin-top: 3px; }

.status-pill { display: inline-block; padding: 3px 10px; border-radius: 20px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.4px; white-space: nowrap; }
.pill-ready   { background: #dcfce7; color: #166534; }
.pill-error   { background: #fee2e2; color: #991b1b; }
.pill-dupe    { background: #f1f5f9; color: var(--slate); }
.pill-order   { background: #dbeafe; color: #1e40af; }
.pill-neutral { background: #f1f5f9; color: var(--slate); }

.upload-result-row { display: flex; align-items: center; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--border-light); flex-wrap: wrap; }
.upload-result-row:last-child { border-bottom: none; }
</style>