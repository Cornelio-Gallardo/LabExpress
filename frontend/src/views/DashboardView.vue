<template>
  <div>
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">Dashboard</div>
          <div class="page-sub">{{ dateRangeLabel }}</div>
        </div>
        <div v-if="showClinicPicker">
          <select v-model="activeClientId" class="form-input" style="max-width:240px; margin:0" @change="onClinicChange">
            <option v-for="c in clinics" :key="c.id" :value="c.id">{{ c.name }}</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Date range bar -->
    <div class="date-range-bar">
      <div class="date-range-group">
        <label class="date-label">From</label>
        <input type="date" v-model="dateFrom" class="date-input" @change="onDateChange" />
      </div>
      <div class="date-range-sep">—</div>
      <div class="date-range-group">
        <label class="date-label">To</label>
        <input type="date" v-model="dateTo" class="date-input" @change="onDateChange" />
      </div>
      <button class="date-quick-btn" :class="{ active: isToday }" @click="setToday">Today</button>
      <button class="date-quick-btn" @click="setLast7">Last 7 days</button>
      <button class="date-quick-btn" @click="setThisMonth">This month</button>
      <div v-if="loadingSessions" class="date-loading">Loading…</div>
    </div>

    <div class="dashboard-layout">

      <!-- LEFT: Patient list -->
      <div class="patient-panel">
        <div class="panel-head">
          <div>
            <div class="panel-title">Patients</div>
            <div class="panel-sub">{{ filteredPatients.length }} of {{ allPatients.length }}</div>
          </div>
          <div class="filter-pills">
            <button class="fpill" :class="{ active: filterStatus==='all' }" @click="filterStatus='all'">All</button>
            <button class="fpill" :class="{ active: filterStatus==='unassigned' }" @click="filterStatus='unassigned'">Unassigned</button>
            <button class="fpill" :class="{ active: filterStatus==='assigned' }" @click="filterStatus='assigned'">Assigned</button>
          </div>
        </div>

        <div style="padding:10px 14px; border-bottom:1px solid var(--border-light)">
          <input v-model="search" class="form-input" style="margin:0; width:100%" placeholder="🔍 Search name, LIS ID…" autocomplete="off" />
        </div>

        <div v-if="loadingPatients" class="panel-loading">Loading patients…</div>
        <div v-else class="patient-list">
          <div v-for="p in filteredPatients" :key="p.id" class="patient-row">

            <!-- Avatar + info -->
            <div class="pat-avatar" :style="{ background: avatarBg(p.name) }">{{ initials(p.name) }}</div>
            <div class="pat-info">
              <div class="pat-name">{{ p.name }}</div>
              <div class="pat-meta">
                <span v-if="p.lisPatientId" class="pat-id">{{ p.lisPatientId }}</span>
                <span v-if="p.birthdate" class="pat-age">{{ age(p.birthdate) }} yrs</span>
              </div>
            </div>

            <!-- Assignment controls -->
            <div class="pat-controls">
              <!-- Already assigned: show badge + remove -->
              <template v-if="isAssigned(p)">
<span class="assigned-badge">
                  {{ getAssignedShift(p) }}
                  <span v-if="getAssignedChair(p)"> · {{ getAssignedChair(p) }}</span>
                </span>
                <button v-if="auth.canManageSession" class="remove-btn" @click="removePatient(p)" title="Remove">✕</button>
                <button class="btn btn-primary btn-sm" @click="goToPatientSession(p)">Results</button>
              </template>

              <!-- Not assigned: shift picker + chair + assign -->
              <template v-else-if="auth.canManageSession">
<input v-model="rowShift[p.id]" class="shift-label-input" placeholder="Shift" maxlength="20" />
                <input v-model="rowChair[p.id]" class="chair-input" placeholder="Chair" maxlength="10" />
                <button
                  class="assign-btn"
                  :disabled="addingId === p.id"
                  @click="assignPatient(p)"
                >{{ addingId === p.id ? '…' : 'Assign' }}</button>
              </template>
              <template v-else>
                <span class="text-slate text-sm">Not assigned</span>
              </template>
            </div>
          </div>
          <div v-if="filteredPatients.length === 0" class="empty-list">No patients found</div>
        </div>
      </div>

      <!-- RIGHT: Today's assignment summary by shift -->
      <div class="summary-panel">
        <div class="panel-head" style="border-radius:var(--radius) var(--radius) 0 0">
          <div class="panel-title">Today's Assignments</div>
          <div class="panel-sub">{{ allSessions.length }} total</div>
        </div>

        <div v-if="loadingSessions" class="panel-loading">Loading…</div>
        <div v-else>
          <div v-for="key in shiftKeys" :key="key" class="shift-group">
<div class="shift-group-header">
<span class="shift-group-label">{{ key }}</span>
<span class="shift-group-count">{{ sessionsByShift[key]?.length || 0 }} patients</span>
            </div>

<div v-if="!sessionsByShift[key]?.length" class="shift-empty">No patients assigned</div>
<div v-else class="shift-roster">
<div v-for="(s, i) in sessionsByShift[key]" :key="s.id" class="shift-roster-row">
                <span class="roster-num">{{ i + 1 }}</span>
                <!-- Chair inline edit -->
                <template v-if="editingChairId !== s.id">
                  <span
                    class="chair-badge" :class="{ 'chair-dup': isDupChair(s, n), 'chair-empty': !s.chair }"
                    @click="auth.canManageSession && startEditChair(s)"
                    :style="auth.canManageSession ? 'cursor:pointer' : ''"
                    :title="auth.canManageSession ? 'Click to edit' : ''"
                  >{{ s.chair || '—' }}</span>
<span v-if="isDupChair(s, key)" class="dup-warn">⚠</span>
                </template>
                <template v-else>
                  <div class="chair-edit">
                    <input v-model="chairEditVal" class="form-input"
                      style="width:54px;padding:3px 6px;margin:0;font-size:12px"
                      @keyup.enter="saveChair(s)" @keyup.escape="editingChairId=null"
                      ref="chairEditRef" />
                    <button class="btn btn-primary btn-sm" @click="saveChair(s)">✓</button>
                    <button class="btn btn-outline btn-sm" @click="editingChairId=null">✕</button>
                  </div>
                </template>

                <span class="roster-name">{{ s.patientName }}</span>
                <div class="roster-row-actions">
                  <button class="btn btn-primary btn-sm" @click="goToSession(s)">Results</button>
                  <button v-if="auth.canManageSession" class="remove-btn" @click="removeSession(s)" title="Remove">✕</button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { useAuthStore } from '../store/auth'
import { sessionsApi, patientsApi } from '../services/api'
import api from '../services/api'
import { useRouter } from 'vue-router'
import { useDialog } from '../composables/useDialog'
const dialog = useDialog()

const auth   = useAuthStore()
const router = useRouter()

const todayIso = new Date().toISOString().split('T')[0]

// Date range
const dateFrom = ref(todayIso)
const dateTo   = ref(todayIso)

const dateRangeLabel = computed(() => {
  if (dateFrom.value === dateTo.value) {
    return new Date(dateFrom.value + 'T12:00:00').toLocaleDateString('en-PH', { weekday:'long', year:'numeric', month:'long', day:'numeric' })
  }
  const fmt = d => new Date(d + 'T12:00:00').toLocaleDateString('en-PH', { month:'short', day:'numeric', year:'numeric' })
  return fmt(dateFrom.value) + ' – ' + fmt(dateTo.value)
})

const isToday = computed(() => dateFrom.value === todayIso && dateTo.value === todayIso)

function setToday()     { dateFrom.value = todayIso; dateTo.value = todayIso; loadSessions() }
function setLast7()     { const d = new Date(); d.setDate(d.getDate()-6); dateFrom.value = d.toISOString().split('T')[0]; dateTo.value = todayIso; loadSessions() }
function setThisMonth() { const n = new Date(); dateFrom.value = new Date(n.getFullYear(), n.getMonth(), 1).toISOString().split('T')[0]; dateTo.value = todayIso; loadSessions() }
function onDateChange() { loadSessions() }

// Clinics
const clinics         = ref([])
const activeClientId  = ref(auth.client?.id || null)
const showClinicPicker = computed(() => auth.isPlAdmin && clinics.value.length > 1)

// Sessions (all shifts, today)
const allSessions     = ref([])
const loadingSessions  = ref(false)

const sessionsByShift = computed(() => {
  const m = {}
  for (const s of allSessions.value) {
    const key = (s.shiftLabel && s.shiftLabel.trim()) 
      ? s.shiftLabel.trim() 
      : (s.shiftNumber ? `Shift ${s.shiftNumber}` : 'Unassigned')
    if (!m[key]) m[key] = []
    m[key].push(s)
  }
  return m
})

const shiftKeys = computed(() => Object.keys(sessionsByShift.value))

// Patients
const allPatients     = ref([])
const loadingPatients  = ref(false)
const search          = ref('')
const filterStatus    = ref('all')

// Per-row shift/chair state (default shift 1)
const rowShift = ref({})
const rowChair = ref({})
const addingId = ref(null)

// Chair editing in roster
const editingChairId = ref(null)
const chairEditVal   = ref('')
const chairEditRef   = ref(null)

// ── Helpers ───────────────────────────────────────────────────────────────────
const avatarPalette = ['#1d4ed8','#0891b2','#059669','#7c3aed','#db2777','#d97706','#dc2626','#0284c7']
function avatarBg(name) {
  if (!name) return '#6b7280'
  let h = 0; for (const c of name) h = (h*31+c.charCodeAt(0))&0xffffffff
  return avatarPalette[Math.abs(h) % avatarPalette.length]
}
function initials(name) {
  if (!name) return '?'
  const p = name.trim().split(/\s+/)
  return p.length >= 2 ? (p[0][0]+p[p.length-1][0]).toUpperCase() : name.slice(0,2).toUpperCase()
}
function age(b) {
  const d=new Date(b),n=new Date()
  return n.getFullYear()-d.getFullYear()-(n<new Date(n.getFullYear(),d.getMonth(),d.getDate())?1:0)
}

function getSessionForPatient(p)  { return allSessions.value.find(s => s.patientId === p.id) }
function isAssigned(p)            { return !!getSessionForPatient(p) }
function getAssignedShift(p) {
  const s = getSessionForPatient(p)
  if (!s) return null
  return (s.shiftLabel && s.shiftLabel.trim()) ? s.shiftLabel : (s.shiftNumber ? `Shift ${s.shiftNumber}` : '—')
}
function getAssignedChair(p)      { return getSessionForPatient(p)?.chair }

function dupChairsForShift(key) {
  const chairs = (sessionsByShift.value[key]||[]).map(s=>s.chair).filter(Boolean)
  return new Set(chairs.filter((c,i)=>chairs.indexOf(c)!==i))
}
function isDupChair(s, key) { return !!s.chair && dupChairsForShift(key).has(s.chair) }

const filteredPatients = computed(() => {
  let list = allPatients.value
  if (filterStatus.value === 'unassigned') list = list.filter(p => !isAssigned(p))
  if (filterStatus.value === 'assigned')   list = list.filter(p => isAssigned(p))
  const q = search.value.toLowerCase()
  if (q) list = list.filter(p => p.name?.toLowerCase().includes(q) || p.lisPatientId?.toLowerCase().includes(q))
  return list
})

// ── Data loading ──────────────────────────────────────────────────────────────
async function loadClinics() {
  if (!auth.isPlAdmin) return
  try {
    const { data } = await api.get('/clinics')
    clinics.value = data
    if (!activeClientId.value && data.length) activeClientId.value = data[0].id
  } catch {}
}

async function loadSessions() {
  loadingSessions.value = true
  try {
    const params = { dateFrom: dateFrom.value, dateTo: dateTo.value }
    if (activeClientId.value) params.clientId = activeClientId.value
    const { data } = await sessionsApi.getAll(params)
    allSessions.value = data
  } catch { allSessions.value = [] }
  finally { loadingSessions.value = false }
}

async function loadLastDate() {
  try {
    const params = {}
    if (activeClientId.value) params.clientId = activeClientId.value
    const { data } = await api.get('/sessions/last-date', { params })
    dateFrom.value = data.date
    dateTo.value   = data.date
  } catch {}
}

async function loadPatients() {
  loadingPatients.value = true
  try {
    const params = {}
    if (activeClientId.value) params.clientId = activeClientId.value
    const { data } = await patientsApi.getAll(params)
    allPatients.value = data
    // default each patient's shift to 1
    for (const p of data) {
      if (!rowShift.value[p.id]) rowShift.value[p.id] = 'Shift 1'
      if (!rowChair.value[p.id]) rowChair.value[p.id] = ''
    }
  } catch { allPatients.value = [] }
  finally { loadingPatients.value = false }
}

async function onClinicChange() {
  await Promise.all([loadSessions(), loadPatients()])
}

// ── Assign ────────────────────────────────────────────────────────────────────
async function assignPatient(patient) {
  addingId.value = patient.id
  try {
    const payload = {
      patientId:   patient.id,
      sessionDate: dateFrom.value === dateTo.value ? dateFrom.value : todayIso,
      shiftLabel: rowShift.value[patient.id] || 'Shift 1',
      chair:       rowChair.value[patient.id]?.trim() || null,
      clientId:    activeClientId.value || null
    }
    const { data } = await sessionsApi.create(payload)
    if (data.duplicate) {
      await dialog.alert('Patient already assigned on this date.', 'Already Assigned')
      return
    }
    if (data.session) {
      allSessions.value.push(data.session)
      if (data.warning) await dialog.alert(data.warning, 'Warning')
    } else {
      // fallback: reload
      await loadSessions()
    }
  } catch (e) {
    await dialog.alert('Failed: ' + (e.response?.data?.message || e.message), 'Error')
  } finally {
    addingId.value = null
  }
}

// ── Remove ────────────────────────────────────────────────────────────────────
async function removePatient(patient) {
  const session = getSessionForPatient(patient)
  if (!session || !await dialog.confirm(`Remove ${patient.name} from ${session.shiftLabel || 'shift'}?`, 'Remove Patient')) return
  await sessionsApi.delete(session.id)
  allSessions.value = allSessions.value.filter(s => s.id !== session.id)
}

async function removeSession(session) {
  if (!await dialog.confirm(`Remove ${session.patientName} from ${session.shiftLabel || 'shift'}?`, 'Remove Patient')) return
  await sessionsApi.delete(session.id)
  allSessions.value = allSessions.value.filter(s => s.id !== session.id)
}

// ── Chair edit ────────────────────────────────────────────────────────────────
function startEditChair(session) {
  editingChairId.value = session.id
  chairEditVal.value   = session.chair || ''
  nextTick(() => chairEditRef.value?.focus())
}
async function saveChair(session) {
  await sessionsApi.updateChair(session.id, chairEditVal.value.trim() || null)
  const s = allSessions.value.find(x => x.id === session.id)
  if (s) s.chair = chairEditVal.value.trim() || null
  editingChairId.value = null
}

// ── Navigate ──────────────────────────────────────────────────────────────────
function buildRoster() {
  // Build ordered roster from all sessions in the current date range
  const ordered = allSessions.value
    .slice()
    .sort((a, b) => (a.shiftNumber - b.shiftNumber) || a.patientName?.localeCompare(b.patientName ?? '') || 0)
    .map(s => s.id)
  return ordered.join(',')
}

function goToSession(session) {
  const roster = buildRoster()
  router.push({ name: 'Session', params: { sessionId: session.id }, query: roster ? { roster } : {} })
}
function goToPatientSession(p) {
  const s = getSessionForPatient(p)
  if (s) goToSession(s)
}

onMounted(async () => {
  await loadClinics()
  await loadLastDate()
  await Promise.all([loadSessions(), loadPatients()])
})
</script>

<style scoped>
.date-range-bar { display:flex; align-items:center; gap:10px; background:white; border:1.5px solid var(--border); border-radius:var(--radius); padding:10px 16px; margin-bottom:16px; flex-wrap:wrap; }
.date-range-group { display:flex; align-items:center; gap:6px; }
.date-label  { font-size:11px; font-weight:600; color:var(--slate); white-space:nowrap; }
.date-input  { height:32px; padding:0 10px; border:1.5px solid var(--border); border-radius:6px; font-size:13px; color:var(--navy); cursor:pointer; }
.date-input:focus { border-color:var(--primary-mid); outline:none; }
.date-range-sep { color:var(--slate); font-size:14px; }
.date-quick-btn { padding:5px 12px; border:1.5px solid var(--border); border-radius:6px; background:white; font-size:12px; font-weight:600; color:var(--slate); cursor:pointer; transition:all .12s; }
.date-quick-btn:hover  { border-color:var(--primary-mid); color:var(--primary-mid); }
.date-quick-btn.active { border-color:var(--primary-mid); background:var(--primary-mid); color:white; }
.date-loading { font-size:12px; color:var(--slate); font-style:italic; }

.dashboard-layout { display:grid; grid-template-columns:1fr 380px; gap:18px; align-items:start; }

/* ── Patient panel ── */
.patient-panel { background:white; border:1.5px solid var(--border); border-radius:var(--radius); overflow:hidden; display:flex; flex-direction:column; }
.panel-head    { display:flex; align-items:center; justify-content:space-between; padding:14px 16px; border-bottom:1px solid var(--border-light); background:#f8faff; flex-wrap:wrap; gap:8px; }
.panel-title   { font-size:14px; font-weight:700; color:var(--navy); }
.panel-sub     { font-size:11px; color:var(--slate); margin-top:2px; }
.panel-loading { padding:24px; text-align:center; color:var(--slate); font-size:13px; }

.filter-pills  { display:flex; gap:5px; }
.fpill { padding:4px 12px; border-radius:20px; border:1.5px solid var(--border); background:white; font-size:11px; font-weight:600; color:var(--slate); cursor:pointer; transition:all .12s; }
.fpill:hover   { border-color:var(--primary-mid); color:var(--primary-mid); }
.fpill.active  { border-color:var(--primary-mid); background:var(--primary-mid); color:white; }

.patient-list  { overflow-y:auto; max-height:calc(100vh - 260px); }
.patient-row   { display:flex; align-items:center; gap:12px; padding:11px 16px; border-bottom:1px solid var(--border-light); transition:background .1s; }
.patient-row:last-child { border-bottom:none; }
.patient-row:hover { background:#fafbff; }
.empty-list    { padding:32px; text-align:center; color:var(--slate); font-size:13px; }

.pat-avatar { width:36px; height:36px; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:12px; font-weight:700; color:white; flex-shrink:0; }
.pat-info   { flex:1; min-width:0; }
.pat-name   { font-size:13px; font-weight:600; color:var(--navy); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.pat-meta   { display:flex; gap:8px; margin-top:2px; }
.pat-id     { font-size:11px; color:var(--slate); font-family:monospace; }
.pat-age    { font-size:11px; color:var(--slate); }

.pat-controls { display:flex; align-items:center; gap:7px; flex-shrink:0; }
.shift-label-input { width:80px; height:32px; padding:0 8px; border:1.5px solid var(--border); border-radius:6px; font-size:12px; font-weight:600; color:var(--navy); }
.shift-select:focus { border-color:var(--primary-mid); outline:none; }
.chair-input  { width:70px; height:32px; padding:0 8px; border:1.5px solid var(--border); border-radius:6px; font-size:12px; text-align:center; font-weight:600; }
.chair-input:focus { border-color:var(--primary-mid); outline:none; }
.assign-btn   { padding:5px 14px; height:32px; border:none; background:var(--primary-mid); color:white; border-radius:6px; font-size:12px; font-weight:600; cursor:pointer; transition:background .12s; white-space:nowrap; }
.assign-btn:hover:not(:disabled) { background:#1d4ed8; }
.assign-btn:disabled { opacity:.5; cursor:not-allowed; }
.remove-btn   { width:26px; height:26px; border:none; background:transparent; color:#d1d5db; cursor:pointer; border-radius:4px; font-size:14px; display:flex; align-items:center; justify-content:center; }
.remove-btn:hover { background:#fee2e2; color:#dc2626; }

.assigned-badge { display:inline-flex; align-items:center; padding:4px 10px; border-radius:20px; font-size:11px; font-weight:700; white-space:nowrap; background:#eff6ff; color:#1e40af; }
/* .sc-1 { background:#dbeafe; color:#1e40af; }
/* .sc-2 { background:#dcfce7; color:#166534; }
/* .sc-3 { background:#fef9c3; color:#854d0e; }
/* .sc-4 { background:#fce7f3; color:#9d174d; }

/* ── Summary panel ── */
.summary-panel { background:white; border:1.5px solid var(--border); border-radius:var(--radius); overflow:hidden; }

.shift-group        { border-bottom:1px solid var(--border-light); }
.shift-group:last-child { border-bottom:none; }
.shift-group-header { display:flex; align-items:center; justify-content:space-between; padding:9px 16px; }
.shift-group-label  { font-size:12px; font-weight:700; }
.shift-group-count  { font-size:11px; font-weight:600; }





.shift-empty { padding:10px 16px; font-size:12px; color:var(--slate); font-style:italic; }
.shift-roster { }
.shift-roster-row { display:flex; align-items:center; gap:8px; padding:8px 16px; border-top:1px solid var(--border-light); }
.roster-num   { width:18px; font-size:11px; color:var(--slate); font-weight:600; text-align:right; flex-shrink:0; }
.roster-name  { flex:1; font-size:12px; font-weight:600; color:var(--navy); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.roster-row-actions { display:flex; align-items:center; gap:4px; flex-shrink:0; }

.chair-badge { display:inline-block; padding:2px 8px; background:#eff6ff; color:var(--primary-mid); border:1.5px solid #bfdbfe; border-radius:6px; font-weight:700; font-size:11px; flex-shrink:0; }
.chair-badge.chair-dup   { background:#fee2e2; color:#dc2626; border-color:#fca5a5; }
.chair-badge.chair-empty { background:#f8fafc; color:#94a3b8; border-color:#e2e8f0; font-weight:400; }
.dup-warn    { font-size:10px; color:#dc2626; font-weight:700; }
.chair-edit  { display:flex; gap:4px; align-items:center; flex-shrink:0; }
</style>