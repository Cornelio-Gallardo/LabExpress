<template>
  <div>

    <!-- ══ SHIFT PICKER — shown when arriving directly (no shift in URL) ══ -->
    <div v-if="!shiftNum || !resolvedClientId">
      <div class="page-header">
        <div class="page-title">Patient Roster</div>
        <div class="page-sub">Select a shift to add patients</div>
      </div>

      <!-- Clinic picker for pl_admin -->
      <div v-if="auth.isPlAdmin && clinics.length > 1" class="card" style="padding:14px 20px; margin-bottom:16px; display:flex; align-items:center; gap:12px">
        <label class="form-label" style="margin:0; white-space:nowrap">Clinic:</label>
        <select v-model="pickerClientId" class="form-input" style="max-width:280px; margin:0" @change="loadPickerShifts">
          <option v-for="c in clinics" :key="c.id" :value="c.id">{{ c.name }}</option>
        </select>
      </div>

      <div v-if="loadingPicker" class="loading">Loading shifts…</div>

      <div v-else-if="pickerShifts.length === 0" class="empty-state">
        <div class="empty-icon">📅</div>
        <div class="empty-title">No shifts scheduled for today</div>
        <div class="empty-sub">Go to Shift Management to create shifts for today.</div>
        <router-link to="/shift-management" class="btn btn-primary" style="margin-top:16px">Go to Shift Management</router-link>
      </div>

      <div v-else>
        <div style="font-size:13px; font-weight:600; color:var(--slate); margin-bottom:12px; text-transform:uppercase; letter-spacing:0.5px">
          Today's Shifts — {{ todayLabel }}
        </div>
        <div class="shift-picker-grid">
          <div
            v-for="s in pickerShifts" :key="s.id"
            class="shift-pick-card"
            @click="selectPickerShift(s)"
          >
            <div class="spcard-num">{{ s.shiftNumber }}</div>
            <div class="spcard-label">{{ s.shiftLabel }}</div>
            <div class="spcard-time">{{ s.startTime }} – {{ s.endTime }}</div>
            <div class="spcard-count">{{ s.patientCount }} patients</div>
            <div class="spcard-cta">Click to add patients →</div>
          </div>
        </div>
      </div>
    </div>

    <!-- ══ ROSTER — shown once shift is known ══ -->
    <div v-else>
      <div class="page-header">
        <div class="flex-between">
          <div>
            <div class="page-title">Add Patients — Shift {{ shiftNum }}</div>
            <div class="page-sub">{{ dateLabel }}</div>
          </div>
          <div class="flex gap-2">
            <button v-if="selected.length" class="btn btn-primary" @click="addSelected" :disabled="saving">
              {{ saving ? 'Adding…' : `Confirm — Add ${selected.length} to Shift ${shiftNum}` }}
            </button>
            <button class="btn btn-outline" @click="resetShift">← Change Shift</button>
            <router-link to="/shifts" class="btn btn-outline">← Back to Shifts</router-link>
          </div>
        </div>
      </div>

      <!-- Search + filter bar -->
      <div class="card toolbar">
        <input
          v-model="search"
          class="form-input search-input"
          placeholder="🔍  Type name or LIS ID to search…"
          autofocus
        />
        <select v-model="statusFilter" class="form-input" style="width:160px; margin:0">
          <option value="">All statuses</option>
          <option value="ready">Ready</option>
          <option value="stale">Stale (&gt;30d)</option>
          <option value="nodata">No data</option>
        </select>
        <div class="flex gap-2" style="margin-left:auto; align-items:center">
          <span class="text-slate text-sm">{{ filtered.length }} shown</span>
          <span v-if="selected.length" class="sel-badge">{{ selected.length }} selected</span>
          <button v-if="selected.length" class="btn btn-outline btn-sm" @click="selected = []">Clear</button>
        </div>
      </div>

      <div v-if="loading" class="loading">Loading patients…</div>

      <div v-else-if="filtered.length === 0" class="empty-state">
        <div class="empty-icon">👤</div>
        <div class="empty-title">No patients found</div>
      </div>

      <div v-else class="card">
        <div class="table-card">
          <div class="table-wrap">
            <table>
            <thead>
              <tr>
                <th style="width:36px">
                  <input type="checkbox" :checked="allChecked" @change="toggleAll" />
                </th>
                <th>Patient</th>
                <th>LIS ID</th>
                <th>DOB</th>
                <th>Last result</th>
                <th>Status</th>
                <th style="width:110px">Chair</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="p in filtered" :key="p.id"
                :class="{
                  'row-selected': isSelected(p.id),
                  'row-inshift':  inShift.has(p.id)
                }"
                @click="!inShift.has(p.id) && toggle(p.id)"
                style="cursor:pointer; user-select:none"
              >
                <td @click.stop>
                  <input
                    type="checkbox"
                    :checked="isSelected(p.id)"
                    :disabled="inShift.has(p.id)"
                    @change="toggle(p.id)"
                  />
                </td>
                <td>
                  <div style="font-weight:600; color:var(--navy)">{{ p.name }}</div>
                  <div v-if="inShift.has(p.id)" class="inshift-tag">✓ In this shift</div>
                </td>
                <td class="text-sm text-slate">{{ p.lisPatientId || '—' }}</td>
                <td class="text-sm text-slate">{{ p.birthdate ? fmtDate(p.birthdate) : '—' }}</td>
                <td class="text-sm">
                  <span v-if="p.daysSinceLastResult !== null" :class="p.daysSinceLastResult > 30 ? 'text-warning' : 'text-ok'">
                    {{ fmtAge(p.daysSinceLastResult) }}
                  </span>
                  <span v-else class="text-slate">Never</span>
                </td>
                <td>
                  <span class="status-pill" :class="`pill-${p.resultStatus}`">
                    {{ p.resultStatus === 'ready' ? 'Ready' : p.resultStatus === 'stale' ? 'Stale' : 'No data' }}
                  </span>
                </td>
                <td @click.stop>
                  <input
                    v-if="isSelected(p.id)"
                    v-model="chairs[p.id]"
                    class="form-input"
                    style="width:88px; padding:4px 8px; margin:0; font-size:12px"
                    placeholder="e.g. A3"
                    @click.stop
                  />
                  <span v-else class="text-slate text-sm">—</span>
                </td>
                <td @click.stop>
                  <span v-if="inShift.has(p.id)" class="text-slate text-sm">Added</span>
                  <button
                    v-else-if="!isSelected(p.id)"
                    class="btn btn-outline btn-sm"
                    @click.stop="quickAdd(p)"
                    :disabled="saving"
                  >Add</button>
                  <span v-else class="text-slate text-sm">✓</span>
                </td>
              </tr>
            </tbody>
            </table>
          </div>
          <div class="table-footer">
            <span>Showing {{ filtered.length }} of {{ patients.length }} patient{{ patients.length !== 1 ? 's' : '' }}</span>
          </div>
        </div>
      </div>

        <!-- Sticky confirm bar -->
        <transition name="slide-up">
          <div v-if="selected.length" class="sticky-bar">
            <div class="sticky-inner">
              <span style="font-weight:700; color:white">
                {{ selected.length }} patient{{ selected.length !== 1 ? 's' : '' }} selected for Shift {{ shiftNum }}
              </span>
              <div class="flex gap-2">
                <button class="btn-ghost" @click="selected = []">Clear</button>
                <button class="btn-confirm" @click="addSelected" :disabled="saving">
                  {{ saving ? 'Adding…' : 'Confirm & Add to Shift' }}
                </button>
              </div>
            </div>
          </div>
        </transition>
      </div>

  </div>
</template>

<script setup>
import { ref, computed, reactive, onMounted } from 'vue'
import { useAuthStore } from '../store/auth'
import { patientsApi, sessionsApi, shiftsApi } from '../services/api'
import { useRouter, useRoute } from 'vue-router'

const auth   = useAuthStore()
const router = useRouter()
const route  = useRoute()

const todayIso  = new Date().toISOString().split('T')[0]
const todayLabel = new Date().toLocaleDateString('en-PH', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })

// ── Shift + client resolution ─────────────────────────────────────────────────
// These may come from URL (via ShiftSelectView) or be set by the in-page picker
const shiftNum        = ref(route.query.shift ? parseInt(route.query.shift, 10) : null)
const shiftDate       = ref(route.query.date || todayIso)
const resolvedClientId = ref(
  route.query.clientId ||
  auth.client?.id ||
  auth.client?.Id ||
  null
)

const dateLabel = computed(() =>
  new Date(shiftDate.value + 'T00:00:00').toLocaleDateString('en-PH', {
    weekday: 'long', year: 'numeric', month: 'long', day: 'numeric'
  })
)

// ── Shift picker (shown when no shift in URL) ─────────────────────────────────
const clinics       = ref([])
const pickerClientId = ref(resolvedClientId.value)
const pickerShifts  = ref([])
const loadingPicker = ref(false)

async function loadClinics() {
  if (!auth.isPlAdmin) return
  try {
    const { data } = await (await import('../services/api')).default.get('/clinics')
    clinics.value = data
    if (!pickerClientId.value && data.length > 0) {
      pickerClientId.value = data[0].id
      resolvedClientId.value = data[0].id
    }
  } catch { /* ignore */ }
}

async function loadPickerShifts() {
  loadingPicker.value = true
  try {
    const params = { date: todayIso }
    const cid = pickerClientId.value || resolvedClientId.value
    if (cid) params.clientId = cid
    const { data } = await shiftsApi.getAll(params)
    pickerShifts.value = data
  } catch { pickerShifts.value = [] }
  finally { loadingPicker.value = false }
}

function selectPickerShift(schedule) {
  shiftNum.value         = schedule.shiftNumber
  shiftDate.value        = todayIso
  resolvedClientId.value = pickerClientId.value || resolvedClientId.value
  loadRoster()
}

function resetShift() {
  shiftNum.value = null
  patients.value = []
  selected.value = []
  inShift.value  = new Set()
  loadPickerShifts()
}

// ── Patient roster ────────────────────────────────────────────────────────────
const patients     = ref([])
const search       = ref('')
const statusFilter = ref('')
const loading      = ref(false)
const saving       = ref(false)
const selected     = ref([])
const chairs       = reactive({})
const inShift      = ref(new Set())

const filtered = computed(() => {
  const s = search.value.toLowerCase()
  return patients.value.filter(p => {
    const matchSearch = !s
      || p.name.toLowerCase().includes(s)
      || (p.lisPatientId || '').toLowerCase().includes(s)
    const matchStatus = !statusFilter.value || p.resultStatus === statusFilter.value
    return matchSearch && matchStatus
  })
})

const eligibleFiltered = computed(() => filtered.value.filter(p => !inShift.value.has(p.id)))
const allChecked = computed(() =>
  eligibleFiltered.value.length > 0 && eligibleFiltered.value.every(p => selected.value.includes(p.id))
)

function isSelected(id) { return selected.value.includes(id) }

function toggle(id) {
  if (inShift.value.has(id)) return
  const i = selected.value.indexOf(id)
  if (i >= 0) selected.value.splice(i, 1)
  else selected.value.push(id)
}

function toggleAll(e) {
  const ids = eligibleFiltered.value.map(p => p.id)
  if (e.target.checked) selected.value = [...new Set([...selected.value, ...ids])]
  else selected.value = selected.value.filter(id => !ids.includes(id))
}

function fmtDate(d) {
  return new Date(d + 'T00:00:00').toLocaleDateString('en-PH', { year: 'numeric', month: 'short', day: 'numeric' })
}
function fmtAge(days) {
  if (days === 0) return 'Today'
  if (days < 30)  return `${days}d ago`
  if (days < 365) return `${Math.floor(days / 30)}mo ago`
  return `${Math.floor(days / 365)}y ago`
}

async function loadRoster() {
  loading.value = true
  try {
    const cid = resolvedClientId.value
    const params = {}
    if (cid) params.clientId = cid
    const [pRes, sRes] = await Promise.all([
      patientsApi.getAll(params),
      sessionsApi.getAll({ shift: shiftNum.value, date: shiftDate.value, ...(cid ? { clientId: cid } : {}) })
    ])
    patients.value = pRes.data
    inShift.value  = new Set(sRes.data.map(s => s.patientId))
  } finally { loading.value = false }
}

async function quickAdd(patient) {
  saving.value = true
  try {
    const res = await sessionsApi.create({
      patientId:   patient.id,
      sessionDate: shiftDate.value,
      shiftNumber: Number(shiftNum.value),
      chair:       null,
      clientId:    resolvedClientId.value || null
    })
    if (res.data?.duplicate) {
      inShift.value = new Set([...inShift.value, patient.id])
    } else {
      inShift.value = new Set([...inShift.value, patient.id])
    }
  } catch (e) {
    const status = e.response?.status
    const msg = e.response?.data?.message || e.response?.data || e.message || 'Unknown error'
    alert(`Error ${status}: ${JSON.stringify(msg)}`)
  } finally { saving.value = false }
}

async function addSelected() {
  if (!selected.value.length) return
  saving.value = true
  try {
    for (const pid of selected.value) {
      await sessionsApi.create({
        patientId:   pid,
        sessionDate: shiftDate.value,
        shiftNumber: Number(shiftNum.value),
        chair:       chairs[pid]?.trim() || null,
        clientId:    resolvedClientId.value || null
      })
    }
    router.push('/shifts')
  } catch (e) {
    const status = e.response?.status
    const msg = e.response?.data?.message || e.response?.data || e.message || 'Unknown error'
    alert(`Error ${status}: ${JSON.stringify(msg)}`)
    saving.value = false
  }
}

onMounted(async () => {
  await loadClinics()
  if (shiftNum.value && resolvedClientId.value) {
    // Came from ShiftSelectView — load roster directly
    await loadRoster()
  } else {
    // Direct navigation — show shift picker
    await loadPickerShifts()
  }
})
</script>

<style scoped>
/* Shift picker */
.shift-picker-grid {
  display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px;
}
.shift-pick-card {
  background: white; border: 2px solid var(--border);
  border-radius: var(--radius); padding: 22px 18px;
  cursor: pointer; transition: all .15s; text-align: center;
}
.shift-pick-card:hover {
  border-color: var(--primary-mid);
  box-shadow: 0 4px 20px rgba(59,130,246,.12);
  transform: translateY(-2px);
}
.spcard-num   { font-size: 42px; font-weight: 900; color: var(--navy); line-height: 1; font-family: 'DM Sans', sans-serif; }
.spcard-label { font-size: 13px; font-weight: 700; color: var(--navy-mid); margin-top: 4px; }
.spcard-time  { font-size: 12px; color: var(--slate); margin-top: 3px; }
.spcard-count { font-size: 13px; font-weight: 700; color: var(--primary-mid); margin-top: 10px; }
.spcard-cta   { font-size: 11px; color: var(--slate-light); margin-top: 6px; }

/* Toolbar */
.toolbar {
  display: flex; align-items: center; gap: 12px;
  padding: 14px 18px; margin-bottom: 14px; flex-wrap: wrap;
}
.search-input { flex: 1; min-width: 200px; max-width: 360px; margin: 0; }
.sel-badge {
  background: var(--primary-mid); color: white;
  font-size: 12px; font-weight: 700; padding: 3px 10px; border-radius: 20px;
}

/* Table rows */
.row-selected td         { background: #eff6ff; }
.row-inshift td          { opacity: .6; background: #f8fafc; }
.row-selected:hover td   { background: #dbeafe; }
.inshift-tag  { font-size: 10px; color: var(--primary-mid); font-weight: 600; margin-top: 2px; }
.text-warning { color: #d97706; font-weight: 600; }
.text-ok      { color: #059669; font-weight: 500; }

/* Sticky confirm bar */
.sticky-bar {
  position: fixed; bottom: 0; left: 240px; right: 0;
  background: var(--primary-mid); padding: 14px 28px;
  box-shadow: 0 -4px 24px rgba(0,0,0,.18); z-index: 200;
}
.sticky-inner { display: flex; align-items: center; justify-content: space-between; max-width: 1000px; margin: 0 auto; }
.btn-ghost    { background: transparent; border: 1.5px solid rgba(255,255,255,.4); color: white; padding: 8px 16px; border-radius: 7px; cursor: pointer; font-weight: 600; }
.btn-confirm  { background: white; color: var(--navy); padding: 8px 20px; border-radius: 7px; border: none; cursor: pointer; font-weight: 800; font-size: 14px; }
.btn-confirm:disabled { opacity: .6; cursor: not-allowed; }

.slide-up-enter-active, .slide-up-leave-active { transition: transform .2s ease, opacity .2s ease; }
.slide-up-enter-from, .slide-up-leave-to        { transform: translateY(100%); opacity: 0; }
</style>