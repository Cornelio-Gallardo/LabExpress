<template>
  <div>
    <!-- Page header -->
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">Patients</div>
          <div class="page-sub">{{ auth.client?.name }} · Patient registry and lab results</div>
        </div>
        <button v-if="auth.isAdmin" class="btn btn-primary" @click="showAdd = true">+ Add Patient</button>
      </div>
    </div>

    <!-- Stat cards -->
    <div class="stat-grid" style="margin-bottom:20px">
      <div class="stat-card">
        <div class="stat-icon">👥</div>
        <div class="stat-body">
          <div class="stat-value">{{ summary.total }}</div>
          <div class="stat-label">Total Patients</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" style="color:var(--green)">✅</div>
        <div class="stat-body">
          <div class="stat-value" style="color:var(--green)">{{ summary.ready }}</div>
          <div class="stat-label">With Recent Results</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" style="color:var(--gold)">⚠</div>
        <div class="stat-body">
          <div class="stat-value" style="color:var(--gold)">{{ summary.stale }}</div>
          <div class="stat-label">Stale Results</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" style="color:var(--slate)">○</div>
        <div class="stat-body">
          <div class="stat-value" style="color:var(--slate)">{{ summary.noData }}</div>
          <div class="stat-label">No Results</div>
        </div>
      </div>
    </div>

    <!-- Search & filters -->
    <div class="card" style="padding:14px 20px; margin-bottom:16px">
      <div class="flex gap-3" style="align-items:center; flex-wrap:wrap">
        <input v-model="search" class="form-input" style="max-width:280px; margin:0" placeholder="🔍  Search by name or LIS ID..." />
        <select v-model="statusFilter" class="form-input" style="max-width:160px; margin:0">
          <option value="">All Status</option>
          <option value="ready">Ready</option>
          <option value="stale">Stale</option>
          <option value="nodata">No Data</option>
        </select>
        <div style="display:flex; align-items:center; gap:8px; margin-left:auto">
          <span class="text-slate text-sm">Rows:</span>
          <select class="page-size-select" v-model="pageSize">
            <option :value="25">25</option>
            <option :value="50">50</option>
            <option :value="100">100</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Patients table -->
    <LoadingSpinner v-if="loading" message="Loading patients…" />
    <div v-else class="card">
      <div v-if="selectedPatients.length" class="bulk-bar">
        <span class="bulk-count">{{ selectedPatients.length }} selected</span>
        <button class="btn btn-outline btn-sm" @click="selectedPatients = []">✕ Deselect</button>
      </div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th style="width:36px; text-align:center"><input type="checkbox" :checked="allPatientsSelected" @change="toggleSelectAllPatients" class="row-check" /></th>
              <th>Patient Name</th>
              <th>LIS ID</th>
              <th>PhilHealth No.</th>
              <th>DOB</th>
              <th>Sex</th>
              <th>Contact</th>
              <th>Results</th>
              <th>Last Result</th>
              <th>Status</th>
              <th v-if="auth.isAdmin"></th>
            </tr>
          </thead>
          <tbody>
            <template v-for="p in patients" :key="p.id">
              <!-- Patient row -->
              <tr class="patient-row">
                <td style="text-align:center"><input type="checkbox" :value="p.id" v-model="selectedPatients" class="row-check" /></td>
                <td>
                  <div style="font-weight:600; color:var(--navy)">{{ p.name }}</div>
                </td>
                <td><span class="mono-tag">{{ p.lisPatientId || '—' }}</span></td>
                <td><span class="mono-tag">{{ p.philhealthNo || '—' }}</span></td>
                <td class="text-sm text-slate">{{ p.birthdate ? formatDate(p.birthdate) : '—' }}</td>
                <td class="text-sm text-slate">{{ p.gender === 'M' ? 'Male' : p.gender === 'F' ? 'Female' : '—' }}</td>
                <td class="text-sm text-slate">{{ p.contactNumber || '—' }}</td>
                <td>
                  <span v-if="p.resultDateCount" class="result-count-badge">
                    {{ p.resultDateCount }}
                  </span>
                  <span v-else class="text-slate text-sm">—</span>
                </td>
                <td class="text-sm">
                  <span v-if="p.daysSinceLastResult !== null" :class="p.daysSinceLastResult > 365 ? 'text-slate' : p.daysSinceLastResult > 30 ? 'text-gold' : p.daysSinceLastResult > 14 ? 'text-gold' : 'text-green'">
                    {{ formatAge(p.daysSinceLastResult) }}
                  </span>
                  <span v-else class="text-slate">Never</span>
                </td>
                <td>
                  <span class="status-pill" :class="`pill-${p.resultStatus}`">
                    {{ p.resultStatus === 'ready' ? 'Released' : p.resultStatus === 'stale' ? 'Stale' : 'Pending' }}
                  </span>
                </td>
                <td @click.stop style="text-align:right">
                  <div class="actions-cell" style="justify-content:flex-end">
                    <button class="action-btn view" title="View Results Popup" @click="openReport(p)">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
                    </button>
                    <!--- 
                    <button v-if="auth.isAdmin" class="action-btn delete" title="Remove" @click="deactivate(p)">
                      <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/></svg>
                    </button>
                    --->
                  </div>
                </td>
              </tr>
            </template>

            <tr v-if="patients.length === 0">
              <td :colspan="auth.isAdmin ? 11 : 10" style="text-align:center; padding:40px; color:var(--slate)">No patients found</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div style="padding:10px 20px; border-top:1px solid var(--border); display:flex; align-items:center; justify-content:space-between">
        <span class="text-slate text-sm">{{ total }} patient{{ total !== 1 ? 's' : '' }} total</span>
        <div v-if="totalPages > 1" class="pagination-wrap">
          <button class="page-btn" :disabled="page === 1" @click="page--">‹</button>
          <span class="page-info">{{ page }} / {{ totalPages }}</span>
          <button class="page-btn" :disabled="page >= totalPages" @click="page++">›</button>
        </div>
      </div>
    </div>

    <!-- Add Patient Modal -->
    <div v-if="showAdd" class="modal-backdrop" @click.self="showAdd = false">
      <div class="modal-box">
        <div class="modal-header">
          <div class="card-title">Add Patient</div>
          <button class="btn btn-outline btn-sm" @click="showAdd = false">✕</button>
        </div>
        <div style="padding:20px">
          <div class="doctrine-bar" style="font-size:11.5px; margin-bottom:16px">
            In production, patients are created automatically via HL7 ingestion. This form is for testing only.
          </div>
          <div class="form-group">
            <label class="form-label">Full Name *</label>
            <input v-model="newP.name" class="form-input" placeholder="DELA CRUZ, Juan" />
          </div>
          <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px">
            <div class="form-group">
              <label class="form-label">LIS Patient ID</label>
              <input v-model="newP.lisPatientId" class="form-input" placeholder="P009" />
            </div>
            <div class="form-group">
              <label class="form-label">PhilHealth No.</label>
              <input v-model="newP.philhealthNo" class="form-input" placeholder="PH-XXXX-XXXX-X" />
            </div>
            <div class="form-group">
              <label class="form-label">Birthdate</label>
              <input v-model="newP.birthdate" type="date" class="form-input" />
            </div>
            <div class="form-group">
              <label class="form-label">Gender</label>
              <select v-model="newP.gender" class="form-input">
                <option value="">—</option>
                <option value="M">Male</option>
                <option value="F">Female</option>
              </select>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">Contact Number</label>
            <input v-model="newP.contactNumber" class="form-input" placeholder="09XX XXX XXXX" />
          </div>
          <div class="flex gap-2" style="justify-content:flex-end; margin-top:8px">
            <button class="btn btn-outline" @click="showAdd = false">Cancel</button>
            <button class="btn btn-primary" @click="create" :disabled="!newP.name">Create</button>
          </div>
        </div>
      </div>
    </div>
  </div>
  <!-- Result Report Modal -->
  <ResultReportModal
    v-if="showReport && reportPatient"
    :patientName="reportPatient?.name || ''"
    :philhealthNo="reportPatient?.philhealthNo || ''"
    :birthdate="reportPatient?.birthdate ? formatDate(reportPatient.birthdate) : ''"
    :gender="reportPatient?.gender || ''"
    :results="reportResults"
    :loading="loadingReport"
    :reportDate="new Date().toLocaleDateString('en-PH', { year: 'numeric', month: 'long', day: 'numeric' })"
    @close="showReport = false; reportPatient = null"
  />
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import ResultReportModal from '../components/ResultReportModal.vue'
import LoadingSpinner from '../components/LoadingSpinner.vue'
import { useAuthStore } from '../store/auth'
import { useDialog } from '../composables/useDialog'
const dialog = useDialog()
import { patientsApi } from '../services/api'
import api from '../services/api'

const auth    = useAuthStore()
const patients = ref([])
const loading  = ref(false)

// ── Filters & pagination (all server-side) ────────────────────────────────
const search       = ref('')
const statusFilter = ref('')
const page         = ref(1)
const pageSize     = ref(25)
const total        = ref(0)
const totalPages   = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

// ── Summary stats (separate fast endpoint for the stat cards) ────────────
const summary = ref({ total: 0, ready: 0, stale: 0, noData: 0 })

// ── Modals ────────────────────────────────────────────────────────────────
const showAdd        = ref(false)
const showReport     = ref(false)
const reportPatient  = ref(null)
const reportResults  = ref([])
const loadingReport  = ref(false)
const newP = ref({ name: '', lisPatientId: '', philhealthNo: '', birthdate: '', gender: '', contactNumber: '' })

// ── Selection (current page only) ────────────────────────────────────────
const selectedPatients    = ref([])
const allPatientsSelected = computed(() =>
  patients.value.length > 0 && patients.value.every(p => selectedPatients.value.includes(p.id))
)
function toggleSelectAllPatients(evt) {
  selectedPatients.value = evt.target.checked ? patients.value.map(p => p.id) : []
}

// ── Helpers ───────────────────────────────────────────────────────────────
function formatAge(days) {
  if (days === 0) return 'Today'
  if (days < 30)  return `${days}d ago`
  if (days < 365) return `${Math.floor(days / 30)}mo ago`
  return `${Math.floor(days / 365)}y ago`
}
function formatDate(d) {
  return new Date(d + 'T00:00:00').toLocaleDateString('en-PH', { year: 'numeric', month: 'short', day: 'numeric' })
}

// ── Load paged patients ───────────────────────────────────────────────────
async function load() {
  loading.value = true
  selectedPatients.value = []
  try {
    const { data } = await patientsApi.getAll({
      search:   search.value || undefined,
      status:   statusFilter.value || undefined,
      page:     page.value,
      pageSize: pageSize.value
    })
    patients.value = data.data
    total.value    = data.total
  } finally {
    loading.value = false
  }
}

// ── Load summary stat counts (independent of pagination/search) ──────────
async function loadSummary() {
  try {
    const { data } = await patientsApi.getSummary()
    summary.value = data
  } catch {}
}

// ── Debounce search — fire 300 ms after user stops typing ─────────────────
let searchTimer = null
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => { page.value = 1; load() }, 300)
})

// ── Immediate reload when filter/page/pageSize changes ────────────────────
watch(statusFilter, () => { page.value = 1; load() })
watch([page, pageSize], () => { load() })

onUnmounted(() => clearTimeout(searchTimer))

// ── Report modal ─────────────────────────────────────────────────────────
async function openReport(p) {
  reportPatient.value = p
  reportResults.value = []
  loadingReport.value = true
  showReport.value    = true
  try {
    const { data } = await api.get(`/results/by-date/${p.id}`)
    reportResults.value = (data || []).flatMap(g =>
      (g.results || []).map(r => ({ ...r, resultDate: r.resultDate || g.displayDate }))
    )
  } catch (e) {
    console.error('Failed to load report results', e)
  } finally {
    loadingReport.value = false
  }
}

async function create() {
  if (!newP.value.name) return
  await patientsApi.create(newP.value)
  showAdd.value = false
  newP.value = { name: '', lisPatientId: '', philhealthNo: '', birthdate: '', gender: '', contactNumber: '' }
  await Promise.all([load(), loadSummary()])
}

async function deactivate(p) {
  if (!await dialog.confirm(`Deactivate ${p.name}?`, 'Deactivate Patient')) return
  await patientsApi.deactivate(p.id)
  await Promise.all([load(), loadSummary()])
}

onMounted(() => Promise.all([load(), loadSummary()]))
</script>

<style scoped>
.pagination-wrap { display:flex; align-items:center; gap:6px; }
.page-btn        { width:28px; height:28px; border:1px solid var(--border); border-radius:6px; background:white; cursor:pointer; font-size:14px; display:flex; align-items:center; justify-content:center; }
.page-btn:disabled { opacity:0.4; cursor:not-allowed; }
.page-btn:not(:disabled):hover { background:var(--teal); color:white; border-color:var(--teal); }
.page-info       { font-size:13px; color:var(--slate); min-width:48px; text-align:center; }
.page-size-select { height:28px; padding:0 6px; border:1px solid var(--border); border-radius:6px; font-size:12px; color:var(--slate); }
.bulk-bar { display:flex; align-items:center; gap:10px; padding:8px 16px; background:#fef9c3; border-bottom:1px solid #fde68a; font-size:13px; }
.bulk-count { font-weight:600; color:#92400e; }
.row-check { width:15px; height:15px; cursor:pointer; }
/* Stat cards */
.stat-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; }
.stat-card { background: white; border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: flex; align-items: center; gap: 14px; box-shadow: var(--shadow); }
.stat-icon { font-size: 24px; }
.stat-value { font-family: 'Syne', sans-serif; font-size: 26px; font-weight: 800; color: var(--navy); line-height: 1; }
.stat-label { font-size: 12px; color: var(--slate); margin-top: 3px; }

/* Status pills (matching screenshot) */
.status-pill { display: inline-block; padding: 3px 10px; border-radius: 4px; font-size: 11.5px; font-weight: 600; }
.pill-ready  { background: #dcfce7; color: #166534; }
.pill-stale  { background: #fef9c3; color: #854d0e; }
.pill-nodata { background: #f1f5f9; color: var(--slate); }

/* Patient rows */
.patient-row { cursor: pointer; }
.row-selected td { background: #dbeafe !important; }

/* Mono tags */
.mono-tag { font-family: monospace; font-size: 12px; background: var(--off-white); padding: 2px 7px; border-radius: 4px; }

/* Results panel */


/* Date selector */
.date-tab { padding: 5px 14px; border: 1.5px solid var(--border); border-radius: 20px; font-size: 12px; font-weight: 500; background: white; cursor: pointer; color: var(--slate); transition: all 0.12s; }
.date-tab.active { background: var(--teal); color: white; border-color: var(--teal); }
.date-tab:hover:not(.active) { border-color: var(--teal); color: var(--teal); }

/* Panel tabs */

/* Result values */
.result-value { font-weight: 600; font-size: 14px; }
.val-h { color: var(--red); }
.val-l { color: #1d4ed8; }
.flag-pill { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: 700; }
.flag-h { background: #fee2e2; color: var(--red); }
.flag-l { background: #dbeafe; color: #1d4ed8; }

.text-green { color: var(--green); }
.text-gold  { color: var(--gold); }

/* Modal */
.modal-backdrop { position:fixed; inset:0; background:rgba(0,0,0,0.5); display:flex; align-items:center; justify-content:center; z-index:1000; }
.modal-box { background:white; border-radius:12px; width:520px; max-height:85vh; overflow-y:auto; box-shadow:0 24px 80px rgba(0,0,0,0.3); }
.modal-header { display:flex; align-items:center; justify-content:space-between; padding:18px 20px; border-bottom:1px solid var(--border); }
</style>