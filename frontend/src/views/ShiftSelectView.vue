<template>
  <div>
    <div class="page-header">
      <div class="page-title">Select Shift</div>
      <div class="page-sub">{{ todayLabel }}</div>
    </div>

    <!-- Clinic picker — pl_admin / clinic_admin only -->
    <div v-if="showClinicPicker" class="clinic-bar">
      <span class="clinic-label">Clinic:</span>
      <select v-model="activeClientId" class="form-input" style="max-width:260px; margin:0" @change="onClinicChange">
        <option v-for="c in clinics" :key="c.id" :value="c.id">{{ c.name }}</option>
      </select>
      <span class="clinic-name">{{ activeClinic?.name }}</span>
    </div>
    <div v-else class="clinic-bar" style="justify-content:flex-start">
      <span class="clinic-name">{{ auth.client?.name }}</span>
    </div>

    <!-- Shift cards -->
    <div v-if="loadingShifts" class="loading">Loading shifts…</div>

    <div v-else-if="schedules.length === 0" class="empty-state">
      <div class="empty-icon">📅</div>
      <div class="empty-title">No shifts scheduled today</div>
      <div class="empty-sub">A clinic admin needs to create shift schedules for today.</div>
    </div>

    <div v-else class="shift-grid">
      <div
        v-for="s in schedules" :key="s.id"
        class="shift-card" :class="{ selected: selectedSchedule?.id === s.id }"
        @click="selectShift(s)"
      >
        <div class="shift-num">{{ s.shiftNumber }}</div>
        <div class="shift-label-text">{{ s.shiftLabel }}</div>
        <div class="shift-time">{{ s.startTime }} – {{ s.endTime }}</div>

        <!-- Progress bar -->
        <div class="shift-progress-wrap">
          <div class="shift-progress-bar">
            <div
              class="shift-progress-fill"
              :style="{ width: s.maxChairs ? Math.min(100, (s.patientCount / s.maxChairs) * 100) + '%' : '0%' }"
              :class="{
                'prog-full':    s.patientCount >= s.maxChairs,
                'prog-warn':    s.patientCount >= s.maxChairs * 0.8 && s.patientCount < s.maxChairs,
                'prog-normal':  s.patientCount < s.maxChairs * 0.8
              }"
            ></div>
          </div>
          <div class="shift-progress-label">{{ s.patientCount }} / {{ s.maxChairs }} patients</div>
        </div>

        <!-- Assigned nurses -->
        <div class="shift-nurses" v-if="s.nurses && s.nurses.length">
          <span
            v-for="n in s.nurses" :key="n.id"
            class="shift-nurse-chip"
            :title="n.nurseName"
          >
            <span class="nurse-avatar-xs">{{ n.nurseName.charAt(0) }}</span>
            {{ n.nurseName.split(' ')[0] }}
          </span>
        </div>
        <div v-else class="shift-no-nurse">No nurse assigned</div>
      </div>
    </div>

    <!-- Roster for selected shift -->
    <div v-if="selectedSchedule" class="roster-section">
      <div class="roster-header">
        <div>
          <div class="roster-title">Shift {{ selectedSchedule.shiftNumber }} — {{ selectedSchedule.shiftLabel }}</div>
          <div class="text-slate text-sm">{{ sessions.length }} patients · {{ selectedSchedule.startTime }} – {{ selectedSchedule.endTime }}</div>
        </div>
        <button v-if="auth.canManageSession" class="btn btn-primary" @click="goToRoster">
          + Add Patients
        </button>
      </div>

      <div v-if="loadingSessions" class="loading">Loading roster…</div>

      <div v-else-if="sessions.length === 0" class="empty-state" style="padding:32px 0">
        <div class="empty-icon">🏥</div>
        <div class="empty-title">No patients in this shift</div>
        <div v-if="auth.canManageSession" class="empty-sub">Click "Add Patients" to assign patients to this shift.</div>
      </div>

      <div v-else class="card">
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th style="width:90px">Chair</th>
                <th>Patient</th>
                <th>Assigned by</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="s in sessions" :key="s.id" :class="{ 'row-dup': isDupChair(s) }">

                <!-- Chair cell -->
                <td>
                  <template v-if="editingChair !== s.id">
                    <span
                      v-if="s.chair"
                      class="chair-badge" :class="{ 'chair-dup': isDupChair(s) }"
                      :title="isDupChair(s) ? 'Duplicate chair assignment' : ''"
                      @click="auth.canManageSession && startEditChair(s)"
                      style="cursor:pointer"
                    >{{ s.chair }}</span>
                    <span
                      v-else
                      class="chair-unset"
                      @click="auth.canManageSession && startEditChair(s)"
                      :style="auth.canManageSession ? 'cursor:pointer' : ''"
                    >{{ auth.canManageSession ? '+ Set chair' : '—' }}</span>
                    <div v-if="isDupChair(s)" class="dup-warn">⚠ Duplicate</div>
                  </template>
                  <template v-else>
                    <div class="chair-edit">
                      <input
                        v-model="chairInput"
                        class="form-input"
                        style="width:68px; padding:4px 8px; margin:0; font-size:12px"
                        placeholder="A1"
                        @keyup.enter="saveChair(s)"
                        @keyup.escape="editingChair = null"
                        ref="chairInputRef"
                      />
                      <button class="btn btn-primary btn-sm" @click="saveChair(s)">✓</button>
                      <button class="btn btn-outline btn-sm" @click="editingChair = null">✕</button>
                    </div>
                  </template>
                </td>

                <td style="font-weight:600; color:var(--navy)">{{ s.patientName }}</td>
                <td class="text-slate text-sm">{{ s.assignedByName }}</td>
                <td>
                  <div class="flex gap-2">
                    <button class="btn btn-primary btn-sm" @click="goToSession(s)">View Results</button>
                    <button v-if="auth.canManageSession" class="btn btn-outline btn-sm" style="color:#ef4444; border-color:#fca5a5" @click="removeSession(s)">Remove</button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, nextTick } from 'vue'
import { useAuthStore } from '../store/auth'
import { sessionsApi, shiftsApi } from '../services/api'
import api from '../services/api'
import { useRouter } from 'vue-router'

const auth   = useAuthStore()
const router = useRouter()

const todayLabel  = new Date().toLocaleDateString('en-PH', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })
const todayIso    = new Date().toISOString().split('T')[0]

// Clinics — only needed for multi-clinic admins
const clinics       = ref([])
const activeClientId = ref(auth.client?.id || auth.client?.Id || null)
const activeClinic  = computed(() => clinics.value.find(c => c.id === activeClientId.value) || auth.client || null)
const showClinicPicker = computed(() => auth.isPlAdmin && clinics.value.length > 1)

// Shifts
const schedules        = ref([])
const selectedSchedule = ref(null)
const loadingShifts    = ref(false)

// Sessions (roster for selected shift)
const sessions      = ref([])
const loadingSessions = ref(false)

// Chair editing
const editingChair  = ref(null) // session.id being edited
const chairInput    = ref('')
const chairInputRef = ref(null)

const dupChairs = computed(() => {
  const chairs = sessions.value.map(s => s.chair).filter(Boolean)
  return new Set(chairs.filter((c, i) => chairs.indexOf(c) !== i))
})
function isDupChair(s) { return !!s.chair && dupChairs.value.has(s.chair) }

// ── Load clinics (pl_admin only) ─────────────────────────────────────────────
async function loadClinics() {
  if (!auth.isPlAdmin) return
  try {
    const { data } = await api.get('/clinics')
    clinics.value = data
    if (!activeClientId.value && data.length > 0) activeClientId.value = data[0].id
  } catch { /* ignore */ }
}

// ── Load shift schedules for today ───────────────────────────────────────────
async function loadShifts() {
  loadingShifts.value = true
  selectedSchedule.value = null
  sessions.value = []
  try {
    const params = { date: todayIso }
    if (activeClientId.value) params.clientId = activeClientId.value
    const { data } = await shiftsApi.getAll(params)
    schedules.value = data
  } catch { schedules.value = [] }
  finally { loadingShifts.value = false }
}

async function onClinicChange() {
  await loadShifts()
}

// ── Select a shift, load its patient roster ──────────────────────────────────
async function selectShift(schedule) {
  selectedSchedule.value = schedule
  editingChair.value = null
  await loadSessions()
}

async function loadSessions() {
  if (!selectedSchedule.value) return
  loadingSessions.value = true
  try {
    const params = { shift: selectedSchedule.value.shiftNumber, date: todayIso }
    if (activeClientId.value) params.clientId = activeClientId.value
    const { data } = await sessionsApi.getAll(params)
    sessions.value = data
  } finally { loadingSessions.value = false }
}

// ── Chair management ─────────────────────────────────────────────────────────
function startEditChair(session) {
  editingChair.value = session.id
  chairInput.value   = session.chair || ''
  nextTick(() => chairInputRef.value?.focus())
}

async function saveChair(session) {
  await sessionsApi.updateChair(session.id, chairInput.value.trim() || null)
  session.chair      = chairInput.value.trim() || null
  editingChair.value = null
}

// ── Remove patient from shift ────────────────────────────────────────────────
async function removeSession(session) {
  if (!confirm(`Remove ${session.patientName} from Shift ${selectedSchedule.value?.shiftNumber}?`)) return
  await sessionsApi.delete(session.id)
  sessions.value = sessions.value.filter(s => s.id !== session.id)
  // Update patient count on card
  if (selectedSchedule.value) selectedSchedule.value.patientCount = sessions.value.length
}

// ── Navigate ─────────────────────────────────────────────────────────────────
function goToSession(session) {
  router.push(`/session/${session.id}`)
}

function goToRoster() {
  const q = {
    shift: selectedSchedule.value.shiftNumber,
    date:  todayIso,
  }
  if (activeClientId.value) q.clientId = activeClientId.value
  router.push({ path: '/roster', query: q })
}

onMounted(async () => {
  await loadClinics()
  await loadShifts()
})
</script>

<style scoped>
.clinic-bar {
  display: flex; align-items: center; gap: 12px;
  background: white; border: 1.5px solid var(--border); border-radius: var(--radius);
  padding: 10px 18px; margin-bottom: 18px;
}
.clinic-label { font-size: 13px; font-weight: 600; color: var(--slate); white-space: nowrap; }
.clinic-name  { font-size: 14px; font-weight: 700; color: var(--navy); }

.shift-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; }

.shift-card {
  background: white; border: 2px solid var(--border);
  border-radius: var(--radius); padding: 22px 18px;
  cursor: pointer; transition: all .15s; text-align: center;
}
.shift-card:hover    { border-color: var(--primary-mid); box-shadow: 0 4px 20px rgba(59,130,246,.12); transform: translateY(-1px); }
.shift-card.selected { border-color: var(--primary-mid); background: #eff6ff; }

.shift-num        { font-size: 42px; font-weight: 900; color: var(--navy); line-height: 1; font-family: 'DM Sans', sans-serif; }
.shift-label-text { font-size: 13px; font-weight: 700; color: var(--navy-mid); margin-top: 4px; }
.shift-time       { font-size: 12px; color: var(--slate); margin-top: 3px; }

/* Progress bar */
.shift-progress-wrap  { margin-top: 14px; }
.shift-progress-bar   { height: 6px; background: #e2e8f0; border-radius: 99px; overflow: hidden; }
.shift-progress-fill  { height: 100%; border-radius: 99px; transition: width .3s ease; }
.prog-normal          { background: var(--primary-mid); }
.prog-warn            { background: #f59e0b; }
.prog-full            { background: #ef4444; }
.shift-progress-label { font-size: 11px; color: var(--slate); margin-top: 4px; font-weight: 600; }

/* Nurse chips */
.shift-nurses    { display: flex; flex-wrap: wrap; gap: 4px; justify-content: center; margin-top: 10px; }
.shift-nurse-chip {
  display: inline-flex; align-items: center; gap: 4px;
  background: #f0fdf4; border: 1px solid #86efac;
  color: #166534; font-size: 11px; font-weight: 600;
  padding: 2px 8px 2px 4px; border-radius: 20px;
}
.nurse-avatar-xs {
  width: 16px; height: 16px; border-radius: 50%;
  background: #16a34a; color: white;
  font-size: 9px; font-weight: 800;
  display: flex; align-items: center; justify-content: center;
}
.shift-no-nurse  { font-size: 11px; color: #cbd5e1; margin-top: 10px; font-style: italic; }

.roster-section { margin-top: 28px; }
.roster-header  { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
.roster-title   { font-size: 16px; font-weight: 700; color: var(--navy); }

.chair-badge {
  display: inline-block; padding: 3px 11px;
  background: #eff6ff; color: var(--primary-mid);
  border: 1.5px solid #bfdbfe; border-radius: 6px;
  font-weight: 700; font-size: 13px;
}
.chair-badge.chair-dup { background: #fee2e2; color: #dc2626; border-color: #fca5a5; }
.chair-unset  { font-size: 12px; color: var(--slate-light); text-decoration: underline dotted; }
.dup-warn     { font-size: 10px; color: #dc2626; font-weight: 600; margin-top: 2px; }
.chair-edit   { display: flex; gap: 4px; align-items: center; }
.row-dup td   { background: #fff7f7; }
</style>