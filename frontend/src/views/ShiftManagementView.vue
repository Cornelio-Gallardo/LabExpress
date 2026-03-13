<template>
  <div>
    <!-- Header -->
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">Shift Management</div>
          <div class="page-sub">{{ clinics.find(c => c.id === selectedClientId)?.name || auth.client?.name }} · Schedule shifts and assign nursing staff</div>
        </div>
        <div class="flex gap-2">
          <button class="btn btn-outline" :class="{ 'btn-primary': activeTab === 'schedule' }" @click="activeTab = 'schedule'">📅 Schedule</button>
          <button class="btn btn-outline" :class="{ 'btn-primary': activeTab === 'week' }" @click="activeTab = 'week'">🗓 Week View</button>
          <button class="btn btn-outline" :class="{ 'btn-primary': activeTab === 'history' }" @click="activeTab = 'history'">📋 History</button>
        </div>
      </div>
    </div>


    <!-- Clinic picker — pl_admin only -->
    <div v-if="auth.isPlAdmin && clinics.length > 1" class="card" style="padding:12px 18px; margin-bottom:16px; display:flex; align-items:center; gap:12px">
      <label class="form-label" style="margin:0; white-space:nowrap; font-size:13px">Clinic:</label>
      <select v-model="selectedClientId" class="form-input" style="max-width:280px; margin:0" @change="loadSchedule">
        <option v-for="cl in clinics" :key="cl.id" :value="cl.id">{{ cl.name }}</option>
      </select>
    </div>

    <!-- ── SCHEDULE TAB ── -->
    <div v-if="activeTab === 'schedule'">
      <!-- Date picker + bulk create -->
      <div class="card" style="padding:16px 20px; margin-bottom:16px">
        <div class="flex gap-3" style="align-items:center; flex-wrap:wrap">
          <div>
            <label class="form-label">Date</label>
            <input v-model="selectedDate" type="date" class="form-input" style="width:180px; margin:0" @change="loadSchedule" />
          </div>
          <div style="margin-top:18px; margin-left:auto; display:flex; gap:8px">
            <button class="btn btn-outline" @click="changeDate(-1)">← Prev</button>
            <button class="btn btn-outline" @click="goToday">Today</button>
            <button class="btn btn-outline" @click="changeDate(1)">Next →</button>
            <button class="btn btn-primary" @click="showBulk = true">+ Bulk Create Shifts</button>
          </div>
        </div>
      </div>

      <div v-if="loading" class="loading">Loading shifts…</div>

      <div v-else>
        <!-- 4 shift cards -->
        <div style="display:grid; grid-template-columns:repeat(2,1fr); gap:16px">
          <div v-for="shiftNum in [1,2,3,4]" :key="shiftNum" class="shift-mgmt-card">
            <div v-if="scheduleByShift[shiftNum]" class="has-schedule">
              <!-- Shift header -->
              <div class="smcard-header">
                <div>
                  <div class="smcard-num">Shift {{ shiftNum }}</div>
                  <div class="smcard-label">{{ scheduleByShift[shiftNum].shiftLabel }}</div>
                  <div class="smcard-time">{{ scheduleByShift[shiftNum].startTime }} – {{ scheduleByShift[shiftNum].endTime }}</div>
                </div>
                <div style="text-align:right">
                  <div class="capacity-bar-wrap">
                    <div class="capacity-label">
                      {{ scheduleByShift[shiftNum].patientCount }} / {{ scheduleByShift[shiftNum].maxChairs }} patients
                    </div>
                    <div class="capacity-bar">
                      <div class="capacity-fill" :style="{ width: capacityPct(scheduleByShift[shiftNum]) + '%', background: capacityColor(scheduleByShift[shiftNum]) }"></div>
                    </div>
                  </div>
                  <div class="flex gap-2" style="margin-top:8px; justify-content:flex-end">
                    <button class="btn btn-outline btn-sm" @click="openEdit(scheduleByShift[shiftNum])">Edit</button>
                    <button class="btn btn-danger btn-sm" @click="deleteShift(scheduleByShift[shiftNum])">Delete</button>
                  </div>
                </div>
              </div>

              <!-- Nurses assigned -->
              <div class="nurses-section">
                <div class="nurses-title">Assigned Nurses</div>
                <div v-if="scheduleByShift[shiftNum].nurses.length === 0" class="text-slate text-sm" style="padding:8px 0">
                  No nurses assigned yet
                </div>
                <div v-for="nurse in scheduleByShift[shiftNum].nurses" :key="nurse.id" class="nurse-row">
                  <div>
                    <span class="nurse-name">{{ nurse.nurseName }}</span>
                    <span class="nurse-role-badge" :class="nurse.assignmentRole === 'charge_nurse' ? 'charge' : 'shift'">
                      {{ nurse.assignmentRole === 'charge_nurse' ? 'Charge' : 'Shift' }}
                    </span>
                  </div>
                  <button class="btn btn-outline btn-sm" style="padding:2px 8px; font-size:11px" @click="removeNurse(scheduleByShift[shiftNum], nurse)">✕</button>
                </div>

                <!-- Assign nurse -->
                <div class="assign-nurse-row">
                  <select v-model="nurseSelects[shiftNum]" class="form-input" style="margin:0; font-size:13px; flex:1">
                    <option value="">— Add nurse —</option>
                    <option v-for="n in availableNurses(scheduleByShift[shiftNum])" :key="n.id" :value="n.id">
                      {{ n.name }} ({{ n.role.replace('_', ' ') }})
                    </option>
                  </select>
                  <select v-model="nurseRoles[shiftNum]" class="form-input" style="margin:0; width:120px; font-size:13px">
                    <option value="shift_nurse">Shift Nurse</option>
                    <option value="charge_nurse">Charge Nurse</option>
                  </select>
                  <button class="btn btn-primary btn-sm" @click="assignNurse(scheduleByShift[shiftNum], shiftNum)" :disabled="!nurseSelects[shiftNum]">
                    Assign
                  </button>
                </div>
              </div>

              <!-- Notes -->
              <div v-if="scheduleByShift[shiftNum].notes" class="shift-notes">
                📝 {{ scheduleByShift[shiftNum].notes }}
              </div>
            </div>

            <!-- No schedule for this shift -->
            <div v-else class="no-schedule" @click="openCreate(shiftNum)">
              <div style="font-size:28px; margin-bottom:8px">+</div>
              <div style="font-weight:600; color:var(--navy)">Shift {{ shiftNum }}</div>
              <div class="text-slate text-sm">{{ defaultShifts[shiftNum-1].label }} · {{ defaultShifts[shiftNum-1].start }}–{{ defaultShifts[shiftNum-1].end }}</div>
              <div class="text-slate text-sm" style="margin-top:4px">Click to create</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ── WEEK VIEW TAB ── -->
    <div v-if="activeTab === 'week'">
      <div class="card" style="padding:16px 20px; margin-bottom:16px">
        <div class="flex gap-3" style="align-items:center">
          <div>
            <label class="form-label">Week Starting</label>
            <input v-model="weekStart" type="date" class="form-input" style="width:180px; margin:0" @change="loadWeek" />
          </div>
          <div style="margin-top:18px; margin-left:auto; display:flex; gap:8px">
            <button class="btn btn-outline" @click="changeWeek(-7)">← Prev Week</button>
            <button class="btn btn-outline" @click="thisWeek">This Week</button>
            <button class="btn btn-outline" @click="changeWeek(7)">Next Week →</button>
          </div>
        </div>
      </div>

      <div v-if="loadingWeek" class="loading">Loading week…</div>
      <div v-else class="card">
        <div class="table-card">
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th v-for="s in [1,2,3,4]" :key="s">Shift {{ s }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="day in weekDays" :key="day">
                  <td style="font-weight:600; white-space:nowrap">
                    <div>{{ formatWeekDay(day) }}</div>
                    <div class="text-slate text-sm">{{ day }}</div>
                  </td>
                  <td v-for="shiftNum in [1,2,3,4]" :key="shiftNum">
                    <div v-if="weekSchedule[day + '_' + shiftNum]" class="week-cell filled">
                      <div class="week-cell-label">{{ weekSchedule[day + '_' + shiftNum].shiftLabel }}</div>
                      <div class="week-cell-time">{{ weekSchedule[day + '_' + shiftNum].startTime }}–{{ weekSchedule[day + '_' + shiftNum].endTime }}</div>
                      <div class="week-cell-count">
                        {{ weekSchedule[day + '_' + shiftNum].patientCount }}/{{ weekSchedule[day + '_' + shiftNum].maxChairs }} pts
                      </div>
                      <div class="week-cell-nurses" v-if="weekSchedule[day + '_' + shiftNum].nurses.length">
                        👥 {{ weekSchedule[day + '_' + shiftNum].nurses.length }} nurse{{ weekSchedule[day + '_' + shiftNum].nurses.length > 1 ? 's' : '' }}
                      </div>
                    </div>
                    <div v-else class="week-cell empty">—</div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="table-footer">
            <span>{{ weekData.length }} day{{ weekData.length !== 1 ? 's' : '' }} shown</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ── HISTORY TAB ── -->
    <div v-if="activeTab === 'history'">
      <div class="card" style="padding:16px 20px; margin-bottom:16px">
        <div class="flex gap-3" style="align-items:center">
          <div>
            <label class="form-label">From</label>
            <input v-model="histFrom" type="date" class="form-input" style="width:160px; margin:0" />
          </div>
          <div>
            <label class="form-label">To</label>
            <input v-model="histTo" type="date" class="form-input" style="width:160px; margin:0" />
          </div>
          <button class="btn btn-primary" style="margin-top:18px" @click="loadHistory">Search</button>
        </div>
      </div>

      <div v-if="loadingHistory" class="loading">Loading history…</div>
      <div v-else class="card">
        <div class="table-card">
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Shift</th>
                  <th>Label</th>
                  <th>Time</th>
                  <th>Capacity</th>
                  <th>Patients</th>
                  <th>Nurses</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="s in history" :key="s.id">
                  <td style="font-weight:500">{{ s.scheduleDate }}</td>
                  <td>Shift {{ s.shiftNumber }}</td>
                  <td>{{ s.shiftLabel }}</td>
                  <td class="text-slate text-sm">{{ s.startTime }} – {{ s.endTime }}</td>
                  <td>
                    <div class="capacity-bar" style="width:80px">
                      <div class="capacity-fill" :style="{ width: capacityPct(s) + '%', background: capacityColor(s) }"></div>
                    </div>
                    <div class="text-sm text-slate">{{ s.patientCount }}/{{ s.maxChairs }}</div>
                  </td>
                  <td>{{ s.patientCount }}</td>
                  <td>
                    <div v-for="n in s.nurses" :key="n.id" class="text-sm">{{ n.nurseName }}</div>
                    <div v-if="!s.nurses.length" class="text-slate text-sm">—</div>
                  </td>
                  <td>
                    <span class="badge" :class="s.isActive ? 'badge-ready' : 'badge-nodata'">
                      {{ s.isActive ? 'Active' : 'Inactive' }}
                    </span>
                  </td>
                </tr>
                <tr v-if="history.length === 0">
                  <td colspan="8" style="text-align:center; padding:32px; color:var(--slate)">No shifts found for this period</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="table-footer">
            <span>{{ history.length }} record{{ history.length !== 1 ? 's' : '' }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ── CREATE/EDIT MODAL ── -->
    <div v-if="showModal" class="modal-backdrop" @click.self="showModal = false">
      <div class="modal-box">
        <div class="modal-header">
          <div class="card-title">{{ editingShift ? 'Edit Shift' : `Create Shift ${form.shiftNumber}` }}</div>
          <button class="btn btn-outline btn-sm" @click="showModal = false">✕</button>
        </div>
        <div style="padding:22px">
          <div style="display:grid; grid-template-columns:1fr 1fr; gap:12px">
            <div class="form-group">
              <label class="form-label">Shift Number</label>
              <select v-model="form.shiftNumber" class="form-input" :disabled="!!editingShift">
                <option v-for="n in [1,2,3,4]" :key="n" :value="n">Shift {{ n }}</option>
              </select>
            </div>
            <div class="form-group">
              <label class="form-label">Label</label>
              <input v-model="form.shiftLabel" class="form-input" placeholder="e.g. Morning" />
            </div>
            <div class="form-group">
              <label class="form-label">Start Time</label>
              <input v-model="form.startTime" type="time" class="form-input" />
            </div>
            <div class="form-group">
              <label class="form-label">End Time</label>
              <input v-model="form.endTime" type="time" class="form-input" />
            </div>
            <div class="form-group">
              <label class="form-label">Max Chairs</label>
              <input v-model.number="form.maxChairs" type="number" min="1" max="50" class="form-input" />
            </div>
            <div class="form-group">
              <label class="form-label">Date</label>
              <input v-model="form.scheduleDate" type="date" class="form-input" :disabled="!!editingShift" />
            </div>
            <div class="form-group" style="grid-column:1/-1">
              <label class="form-label">Notes (optional)</label>
              <textarea v-model="form.notes" class="form-input" rows="2" />
            </div>
          </div>

          <div v-if="formError" style="color:var(--red); font-size:13px; margin-bottom:12px; background:rgba(239,68,68,0.08); padding:10px 14px; border-radius:6px">
            {{ formError }}
          </div>

          <div class="flex gap-2" style="justify-content:flex-end; margin-top:8px">
            <button class="btn btn-outline" @click="showModal = false">Cancel</button>
            <button class="btn btn-primary" @click="saveShift" :disabled="saving">
              {{ saving ? 'Saving…' : editingShift ? 'Save Changes' : 'Create Shift' }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- ── BULK CREATE MODAL ── -->
    <div v-if="showBulk" class="modal-backdrop" @click.self="showBulk = false">
      <div class="modal-box" style="max-width:680px; width:96%">
        <div class="modal-header">
          <div class="card-title">Bulk Create Shifts</div>
          <button class="btn btn-outline btn-sm" @click="showBulk = false">✕</button>
        </div>
        <div style="padding:22px">
          <div class="doctrine-bar" style="margin-bottom:20px">
            Creates selected shifts for every day in the date range. Existing shifts are skipped.
          </div>

          <!-- Date range + chairs -->
          <div style="display:grid; grid-template-columns:1fr 1fr 120px; gap:12px; margin-bottom:20px">
            <div class="form-group" style="margin:0">
              <label class="form-label">From Date</label>
              <input v-model="bulk.fromDate" type="date" class="form-input" />
            </div>
            <div class="form-group" style="margin:0">
              <label class="form-label">To Date</label>
              <input v-model="bulk.toDate" type="date" class="form-input" />
            </div>
            <div class="form-group" style="margin:0">
              <label class="form-label">Max Chairs</label>
              <input v-model.number="bulk.maxChairs" type="number" min="1" max="50" class="form-input" />
            </div>
          </div>

          <!-- Shifts table -->
          <div style="font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:0.6px; color:var(--slate); margin-bottom:8px">
            Shifts to Create
          </div>
          <div class="bulk-shifts-table">
            <div class="bulk-shift-head">
              <span></span>
              <span>Shift</span>
              <span>Label</span>
              <span>Start</span>
              <span>End</span>
              <span>Assign Nurse</span>
            </div>
            <div
              v-for="s in bulk.shifts" :key="s.num"
              class="bulk-shift-row"
              :class="{ 'bulk-row-disabled': !s.enabled }"
            >
              <input type="checkbox" v-model="s.enabled" />
              <span class="bulk-shift-num">{{ s.num }}</span>
              <input v-model="s.label" class="form-input bulk-input" :disabled="!s.enabled" placeholder="Label" />
              <input v-model="s.start" type="time" class="form-input bulk-input" :disabled="!s.enabled" />
              <input v-model="s.end"   type="time" class="form-input bulk-input" :disabled="!s.enabled" />
              <div class="bulk-nurse-cell" :class="{ 'bulk-nurse-disabled': !s.enabled }">
                <!-- Selected nurse chips -->
                <div class="bulk-nurse-chips">
                  <span
                    v-for="uid in s.nurseIds" :key="uid"
                    class="nurse-chip"
                  >
                    {{ allUsers.find(u => u.id === uid)?.name || uid }}
                    <button class="nurse-chip-remove" @click.stop="s.nurseIds = s.nurseIds.filter(id => id !== uid)" :disabled="!s.enabled">×</button>
                  </span>
                  <span v-if="!s.nurseIds.length" class="text-slate text-sm" style="font-style:italic">No nurses</span>
                </div>
                <!-- Add nurse button -->
                <button
                  class="btn-add-nurse"
                  :disabled="!s.enabled"
                  @click.stop="openNursePicker(s)"
                  title="Add nurses"
                >
                  <svg width="14" height="14" viewBox="0 0 20 20" fill="currentColor"><path d="M10 5a1 1 0 011 1v3h3a1 1 0 110 2h-3v3a1 1 0 11-2 0v-3H6a1 1 0 110-2h3V6a1 1 0 011-1z"/><path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm0-2a6 6 0 100-12 6 6 0 000 12z" clip-rule="evenodd"/></svg>
                  Add
                </button>
              </div>
            </div>
          </div>

          <div class="flex gap-2" style="justify-content:flex-end; margin-top:16px">
            <button class="btn btn-outline" @click="showBulk = false">Cancel</button>
            <button class="btn btn-primary" @click="doBulkCreate" :disabled="bulkSaving || !bulk.shifts.some(s => s.enabled)">
              {{ bulkSaving ? 'Creating…' : `Create ${bulk.shifts.filter(s=>s.enabled).length} Shift(s)` }}
            </button>
          </div>
        </div>
      </div>
    </div>

  <!-- ══ Nurse Picker Modal — teleported to body, always above everything ══ -->
  <teleport to="body">
    <div v-if="nursePickerShift" class="nurse-picker-backdrop" @click.self="nursePickerShift = null">
      <div class="nurse-picker-box">
        <div class="modal-header">
          <div class="card-title">
            Assign Nurses — Shift {{ nursePickerShift.num }}: {{ nursePickerShift.label }}
          </div>
          <button class="btn btn-outline btn-sm" @click="nursePickerShift = null">✕</button>
        </div>
        <div style="padding:18px">
          <input
            v-model="nurseSearch"
            class="form-input"
            placeholder="🔍 Search nurse name…"
            style="margin-bottom:14px"
            autofocus
          />
          <div class="nurse-picker-list">
            <div
              v-for="u in filteredPickerNurses"
              :key="u.id"
              class="nurse-picker-row"
              :class="{ 'nurse-picker-selected': nursePickerShift.nurseIds.includes(u.id) }"
              @click="togglePickerNurse(u.id)"
            >
              <div class="nurse-picker-avatar">{{ u.name.charAt(0) }}</div>
              <div style="flex:1">
                <div style="font-weight:600; font-size:13px; color:var(--navy)">{{ u.name }}</div>
                <div style="font-size:11px; color:var(--slate)">{{ u.role === 'charge_nurse' ? 'Charge Nurse' : 'Shift Nurse' }}</div>
              </div>
              <div v-if="nursePickerShift.nurseIds.includes(u.id)" class="nurse-picker-check">✓</div>
            </div>
            <div v-if="filteredPickerNurses.length === 0" style="padding:24px; text-align:center" class="text-slate text-sm">
              No nurses found
            </div>
          </div>
          <div style="display:flex; align-items:center; justify-content:space-between; margin-top:14px; padding-top:14px; border-top:1px solid var(--border)">
            <span class="text-slate text-sm">
              {{ nursePickerShift.nurseIds.length }} nurse{{ nursePickerShift.nurseIds.length !== 1 ? 's' : '' }} selected
            </span>
            <div class="flex gap-2">
              <button class="btn btn-outline btn-sm" @click="nursePickerShift.nurseIds = []">Clear all</button>
              <button class="btn btn-primary btn-sm" @click="nursePickerShift = null">Done</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </teleport>

  <!-- Toast notification -->
  <teleport to="body">
    <transition name="toast-slide">
      <div v-if="toast" class="dx7-toast">{{ toast }}</div>
    </transition>
  </teleport>

</div>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { useAuthStore } from '../store/auth'
import { shiftsApi } from '../services/api'
import api from '../services/api'

const auth = useAuthStore()

// ── Clinic selector (pl_admin only) ──────────────────────────────────────────
const clinics = ref([])
const selectedClientId = ref(auth.client?.id || null)

async function loadClinics() {
  if (!auth.isPlAdmin) return
  try {
    const { data } = await api.get('/clinics')
    clinics.value = data
    if (!selectedClientId.value && data.length > 0) selectedClientId.value = data[0].id
  } catch { /* ignore */ }
}

// ── State ─────────────────────────────────────────────────────────────────────
const activeTab = ref('schedule')
const loading = ref(false)
const loadingWeek = ref(false)
const loadingHistory = ref(false)
const schedule = ref([])
const weekData = ref([])
const history = ref([])
const allUsers = ref([])
const showModal = ref(false)
const showBulk = ref(false)
const editingShift = ref(null)
const saving = ref(false)
const formError = ref('')
const bulkSaving = ref(false)
const bulkResult = ref(null)
const toast = ref('')
let toastTimer = null

function showToast(msg) {
  toast.value = msg
  clearTimeout(toastTimer)
  toastTimer = setTimeout(() => { toast.value = '' }, 4000)
}
const nursePickerShift = ref(null)   // the bulk.shifts[x] object being edited
const nurseSearch      = ref('')

const filteredPickerNurses = computed(() => {
  const nurses = allUsers.value.filter(u => ['charge_nurse', 'shift_nurse'].includes(u.role))
  const s = nurseSearch.value.toLowerCase()
  return s ? nurses.filter(u => u.name.toLowerCase().includes(s)) : nurses
})

function openNursePicker(shiftRow) {
  nurseSearch.value = ''
  nursePickerShift.value = shiftRow
}

function togglePickerNurse(userId) {
  if (!nursePickerShift.value) return
  const ids = nursePickerShift.value.nurseIds
  const i = ids.indexOf(userId)
  if (i >= 0) ids.splice(i, 1)
  else ids.push(userId)
}

const nurseSelects = reactive({ 1: '', 2: '', 3: '', 4: '' })
const nurseRoles = reactive({ 1: 'shift_nurse', 2: 'shift_nurse', 3: 'shift_nurse', 4: 'shift_nurse' })

const today = new Date().toISOString().split('T')[0]
const selectedDate = ref(today)
const weekStart = ref(getMonday())
const histFrom = ref(new Date(Date.now() - 30*24*60*60*1000).toISOString().split('T')[0])
const histTo = ref(today)

const form = reactive({
  shiftNumber: 1, shiftLabel: '', startTime: '',
  endTime: '', maxChairs: 20, scheduleDate: today, notes: ''
})

const bulk = reactive({
  fromDate: today,
  toDate: today,
  maxChairs: 20,
  shifts: [
    { num: 1, label: 'Morning',     start: '06:00', end: '10:00', enabled: true, nurseIds: [] },
    { num: 2, label: 'Mid-Morning', start: '10:00', end: '14:00', enabled: true, nurseIds: [] },
    { num: 3, label: 'Afternoon',   start: '14:00', end: '18:00', enabled: true, nurseIds: [] },
    { num: 4, label: 'Evening',     start: '18:00', end: '22:00', enabled: true, nurseIds: [] },
  ]
})

const defaultShifts = [
  { label: 'Morning',     start: '06:00', end: '10:00' },
  { label: 'Mid-Morning', start: '10:00', end: '14:00' },
  { label: 'Afternoon',   start: '14:00', end: '18:00' },
  { label: 'Evening',     start: '18:00', end: '22:00' },
]

// ── Computed ──────────────────────────────────────────────────────────────────
const scheduleByShift = computed(() => {
  const map = {}
  schedule.value.forEach(s => { map[s.shiftNumber] = s })
  return map
})

const weekDays = computed(() => {
  const days = []
  const start = new Date(weekStart.value)
  for (let i = 0; i < 7; i++) {
    const d = new Date(start)
    d.setDate(d.getDate() + i)
    days.push(d.toISOString().split('T')[0])
  }
  return days
})

const weekSchedule = computed(() => {
  const map = {}
  weekData.value.forEach(s => { map[`${s.scheduleDate}_${s.shiftNumber}`] = s })
  return map
})

// ── Helpers ───────────────────────────────────────────────────────────────────
function getMonday() {
  const d = new Date()
  const day = d.getDay()
  const diff = d.getDate() - day + (day === 0 ? -6 : 1)
  d.setDate(diff)
  return d.toISOString().split('T')[0]
}

function capacityPct(s) {
  return Math.min(100, Math.round((s.patientCount / s.maxChairs) * 100))
}

function capacityColor(s) {
  const pct = capacityPct(s)
  if (pct >= 90) return 'var(--red)'
  if (pct >= 70) return 'var(--gold)'
  return 'var(--green)'
}

function formatWeekDay(dateStr) {
  return new Date(dateStr).toLocaleDateString('en-PH', { weekday: 'short', month: 'short', day: 'numeric' })
}

function availableNurses(shift) {
  const assigned = shift.nurses.map(n => n.nurseUserId)
  return allUsers.value.filter(u =>
    (u.role === 'charge_nurse' || u.role === 'shift_nurse') &&
    u.isActive && !assigned.includes(u.id)
  )
}

// ── Data loading ──────────────────────────────────────────────────────────────
async function loadSchedule() {
  loading.value = true
  try {
    const params = { date: selectedDate.value }
    if (selectedClientId.value) params.clientId = selectedClientId.value
    const { data } = await shiftsApi.getAll(params)
    schedule.value = data
  } finally { loading.value = false }
}

async function loadWeek() {
  loadingWeek.value = true
  try {
    const { data } = await shiftsApi.getWeek({ from: weekStart.value })
    weekData.value = data
  } finally { loadingWeek.value = false }
}

async function loadHistory() {
  loadingHistory.value = true
  try {
    const { data } = await shiftsApi.getHistory({ from: histFrom.value, to: histTo.value })
    history.value = data
  } finally { loadingHistory.value = false }
}

async function loadUsers() {
  try {
    const { data } = await api.get('/users')
    allUsers.value = data
  } catch {}
}

function changeDate(days) {
  const d = new Date(selectedDate.value)
  d.setDate(d.getDate() + days)
  selectedDate.value = d.toISOString().split('T')[0]
  loadSchedule()
}

function goToday() {
  selectedDate.value = today
  loadSchedule()
}

function changeWeek(days) {
  const d = new Date(weekStart.value)
  d.setDate(d.getDate() + days)
  weekStart.value = d.toISOString().split('T')[0]
  loadWeek()
}

function thisWeek() {
  weekStart.value = getMonday()
  loadWeek()
}

// ── CRUD ──────────────────────────────────────────────────────────────────────
function openCreate(shiftNum) {
  editingShift.value = null
  const def = defaultShifts[shiftNum - 1]
  form.shiftNumber = shiftNum
  form.shiftLabel = def.label
  form.startTime = def.start
  form.endTime = def.end
  form.maxChairs = 20
  form.scheduleDate = selectedDate.value
  form.notes = ''
  formError.value = ''
  showModal.value = true
}

function openEdit(shift) {
  editingShift.value = shift
  form.shiftNumber = shift.shiftNumber
  form.shiftLabel = shift.shiftLabel
  form.startTime = shift.startTime
  form.endTime = shift.endTime
  form.maxChairs = shift.maxChairs
  form.scheduleDate = shift.scheduleDate
  form.notes = shift.notes || ''
  formError.value = ''
  showModal.value = true
}

async function saveShift() {
  formError.value = ''
  if (!form.shiftLabel || !form.startTime || !form.endTime) {
    formError.value = 'Label, start time and end time are required'
    return
  }
  saving.value = true
  try {
    if (editingShift.value) {
      await shiftsApi.update(editingShift.value.id, {
        shiftLabel: form.shiftLabel,
        startTime: form.startTime,
        endTime: form.endTime,
        maxChairs: form.maxChairs,
        notes: form.notes || null
      })
    } else {
      await shiftsApi.create({
        scheduleDate: form.scheduleDate,
        shiftNumber: form.shiftNumber,
        shiftLabel: form.shiftLabel,
        startTime: form.startTime,
        endTime: form.endTime,
        maxChairs: form.maxChairs,
        notes: form.notes || null,
        clientId: selectedClientId.value ?? null
      })
    }
    showModal.value = false
    await loadSchedule()
  } catch (err) {
    formError.value = err.response?.data?.message || 'An error occurred'
  } finally { saving.value = false }
}

async function deleteShift(shift) {
  if (!confirm(`Delete Shift ${shift.shiftNumber} on ${shift.scheduleDate}? This cannot be undone.`)) return
  await shiftsApi.delete(shift.id)
  await loadSchedule()
}

async function assignNurse(shift, shiftNum) {
  if (!nurseSelects[shiftNum]) return
  await shiftsApi.assignNurse(shift.id, {
    nurseUserId: nurseSelects[shiftNum],
    assignmentRole: nurseRoles[shiftNum]
  })
  nurseSelects[shiftNum] = ''
  await loadSchedule()
}

async function removeNurse(shift, nurse) {
  if (!confirm(`Remove ${nurse.nurseName} from this shift?`)) return
  await shiftsApi.removeNurse(shift.id, nurse.id)
  await loadSchedule()
}

async function doBulkCreate() {
  bulkSaving.value = true
  bulkResult.value = null
  try {
    const enabledShifts = bulk.shifts.filter(s => s.enabled)
    const { data } = await shiftsApi.bulkCreate({
      fromDate:  bulk.fromDate,
      toDate:    bulk.toDate,
      maxChairs: bulk.maxChairs,
      clientId:  selectedClientId.value ?? null,
      shifts:    enabledShifts.map(s => ({
        shiftNumber: s.num,
        shiftLabel:  s.label,
        startTime:   s.start,
        endTime:     s.end,
      }))
    })
    const created = data.created
    let nursesAssigned = 0

    // Assign nurses for every date in the range
    const shiftsWithNurses = enabledShifts.filter(s => s.nurseIds.length > 0)
    if (shiftsWithNurses.length > 0) {
      // Build array of all dates from fromDate to toDate (string comparison — no timezone issues)
      const dates = []
      const cur = new Date(bulk.fromDate + 'T12:00:00')
      const end = new Date(bulk.toDate + 'T12:00:00')
      while (cur.toISOString().split('T')[0] <= bulk.toDate) {
        dates.push(cur.toISOString().split('T')[0])
        cur.setDate(cur.getDate() + 1)
      }

      for (const dateStr of dates) {
        try {
          const params = { date: dateStr }
          if (selectedClientId.value) params.clientId = selectedClientId.value
          const { data: freshShifts } = await shiftsApi.getAll(params)

          for (const s of shiftsWithNurses) {
            const shift = freshShifts.find(sh => sh.shiftNumber === s.num)
            if (!shift) continue
            for (const nurseId of s.nurseIds) {
              try {
                const nurse = allUsers.value.find(u => u.id === nurseId)
                await shiftsApi.assignNurse(shift.id, {
                  nurseUserId: nurseId,
                  assignmentRole: nurse?.role || 'shift_nurse'
                })
                nursesAssigned++
              } catch {
                // already assigned or other error — skip silently
              }
            }
          }
        } catch (e) {
          console.warn('Failed to assign nurses for date', dateStr, e.message)
        }
      }
    }

    await loadSchedule()
    // Close modal and show toast
    showBulk.value = false
    // Reset nurse selections for next time
    bulk.shifts.forEach(s => { s.nurseIds = [] })
    const nurseMsg = nursesAssigned ? ' · ' + nursesAssigned + ' nurse assignment' + (nursesAssigned !== 1 ? 's' : '') + ' made' : ''
    showToast('✅ ' + created + ' shift' + (created !== 1 ? 's' : '') + ' created successfully' + nurseMsg + '.')
  } catch (e) {
    const msg = e.response?.data?.message || e.response?.data || e.message || 'Unknown error'
    showToast('❌ Failed to create shifts: ' + msg)
  } finally { bulkSaving.value = false }
}

onMounted(async () => {
  await loadClinics()
  await Promise.all([loadSchedule(), loadUsers()])
})
</script>

<style scoped>
.shift-mgmt-card {
  background: white;
  border: 2px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
  min-height: 200px;
}
.has-schedule { padding: 18px 20px; }
.no-schedule {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  height: 200px; cursor: pointer; opacity: 0.6; transition: all 0.15s;
  border: 2px dashed var(--border);
  border-radius: var(--radius);
  background: var(--off-white);
}
.no-schedule:hover { opacity: 1; border-color: var(--teal); background: var(--teal-pale); }

.smcard-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 14px; }
.smcard-num { font-family: 'Syne', sans-serif; font-size: 13px; font-weight: 700; color: var(--teal); text-transform: uppercase; letter-spacing: 1px; }
.smcard-label { font-family: 'Syne', sans-serif; font-size: 18px; font-weight: 700; color: var(--navy); }
.smcard-time { font-size: 12px; color: var(--slate); margin-top: 2px; }

.capacity-bar-wrap { text-align: right; }
.capacity-label { font-size: 12px; color: var(--slate); margin-bottom: 4px; }
.capacity-bar { width: 120px; height: 6px; background: var(--border); border-radius: 4px; overflow: hidden; }
.capacity-fill { height: 100%; border-radius: 4px; transition: width 0.3s; }

.bulk-shifts-table { border: 1px solid var(--border); border-radius: var(--radius); overflow: hidden; }
.bulk-shift-head {
  display: grid; grid-template-columns: 28px 40px 1fr 90px 90px 1.4fr;
  gap: 8px; padding: 8px 12px;
  background: var(--off-white); font-size: 11px; font-weight: 700;
  text-transform: uppercase; letter-spacing: 0.5px; color: var(--slate);
}
.bulk-shift-row {
  display: grid; grid-template-columns: 28px 40px 1fr 90px 90px 1.4fr;
  gap: 8px; padding: 8px 12px; align-items: center;
  border-top: 1px solid var(--border); transition: background .1s;
}
.bulk-shift-row:hover { background: #f8fafc; }
.bulk-row-disabled { opacity: .45; }
.bulk-shift-num { font-size: 18px; font-weight: 900; color: var(--navy); text-align: center; }
.bulk-input { margin: 0; padding: 5px 8px; font-size: 12px; }
/* Nurse cell in bulk row */
.bulk-nurse-cell { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; min-height: 32px; }
.bulk-nurse-disabled { opacity: .45; pointer-events: none; }
.bulk-nurse-chips { display: flex; flex-wrap: wrap; gap: 4px; flex: 1; }
.nurse-chip {
  display: inline-flex; align-items: center; gap: 4px;
  background: #eff6ff; border: 1px solid var(--primary-light);
  color: var(--navy); font-size: 11px; font-weight: 600;
  padding: 2px 6px 2px 8px; border-radius: 20px;
}
.nurse-chip-remove {
  background: none; border: none; cursor: pointer; color: var(--slate);
  font-size: 13px; line-height: 1; padding: 0; margin: 0;
  display: flex; align-items: center;
}
.nurse-chip-remove:hover { color: var(--red); }
.btn-add-nurse {
  display: inline-flex; align-items: center; gap: 4px;
  background: white; border: 1.5px dashed var(--border);
  color: var(--primary-mid); font-size: 11px; font-weight: 700;
  padding: 3px 8px; border-radius: 6px; cursor: pointer;
  white-space: nowrap; transition: all .12s; flex-shrink: 0;
}
.btn-add-nurse:hover { border-color: var(--primary-mid); background: var(--primary-pale); }
.btn-add-nurse:disabled { opacity: .4; cursor: not-allowed; }

/* Nurse picker sub-modal */
.nurse-picker-backdrop {
  position: fixed; inset: 0;
  background: rgba(0,0,0,0.55);
  display: flex; align-items: center; justify-content: center;
  z-index: 2000;
}
.nurse-picker-box {
  background: white; border-radius: 14px;
  box-shadow: 0 20px 60px rgba(0,0,0,0.25);
  max-width: 420px; width: 94%;
  max-height: 90vh; overflow-y: auto;
}
.nurse-picker-list { max-height: 320px; overflow-y: auto; display: flex; flex-direction: column; gap: 4px; }
.nurse-picker-row {
  display: flex; align-items: center; gap: 10px;
  padding: 10px 12px; border-radius: 8px; cursor: pointer;
  border: 1.5px solid transparent; transition: all .12s;
}
.nurse-picker-row:hover { background: var(--primary-pale); border-color: var(--primary-light); }
.nurse-picker-selected { background: #eff6ff; border-color: var(--primary-light); }
.nurse-picker-avatar {
  width: 32px; height: 32px; border-radius: 50%;
  background: var(--primary-mid); color: white;
  font-size: 13px; font-weight: 800;
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0;
}
.nurse-picker-check { color: var(--primary-mid); font-size: 16px; font-weight: 800; }

/* Toast */
.dx7-toast {
  position: fixed; bottom: 28px; left: 50%; transform: translateX(-50%);
  background: var(--navy); color: white;
  padding: 12px 24px; border-radius: 10px;
  font-size: 13px; font-weight: 600;
  box-shadow: 0 8px 32px rgba(0,0,0,.22);
  z-index: 3000; white-space: nowrap;
}
.toast-slide-enter-active, .toast-slide-leave-active { transition: all .25s ease; }
.toast-slide-enter-from, .toast-slide-leave-to { opacity: 0; transform: translateX(-50%) translateY(16px); }
.nurses-section { border-top: 1px solid var(--border); padding-top: 12px; }
.nurses-title { font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.8px; color: var(--slate); margin-bottom: 8px; }
.nurse-row { display: flex; justify-content: space-between; align-items: center; padding: 5px 0; border-bottom: 1px solid var(--off-white); }
.nurse-name { font-size: 13px; font-weight: 500; margin-right: 8px; }
.nurse-role-badge { font-size: 10px; padding: 2px 7px; border-radius: 10px; font-weight: 600; }
.nurse-role-badge.charge { background: #e0f2fe; color: #0369a1; }
.nurse-role-badge.shift { background: #f0fdf4; color: #166534; }
.assign-nurse-row { display: flex; gap: 6px; align-items: center; margin-top: 10px; }
.shift-notes { margin-top: 10px; font-size: 12px; color: var(--slate); font-style: italic; background: var(--off-white); padding: 8px 12px; border-radius: 6px; }

/* Week view */
.week-cell { padding: 8px 10px; border-radius: 6px; font-size: 12px; }
.week-cell.filled { background: var(--teal-pale); border: 1px solid rgba(13,115,119,0.2); }
.week-cell.empty { color: var(--slate); text-align: center; padding: 16px; }
.week-cell-label { font-weight: 600; color: var(--navy); }
.week-cell-time { color: var(--slate); font-size: 11px; }
.week-cell-count { color: var(--teal); font-size: 11px; font-weight: 600; margin-top: 3px; }
.week-cell-nurses { color: var(--slate); font-size: 11px; margin-top: 2px; }

.modal-backdrop { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; }
.modal-box { background: white; border-radius: 12px; width: 520px; max-height: 85vh; overflow-y: auto; box-shadow: 0 24px 80px rgba(0,0,0,0.3); }
.modal-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 22px; border-bottom: 1px solid var(--border); }
</style>