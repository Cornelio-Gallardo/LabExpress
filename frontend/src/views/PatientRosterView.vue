<template>
  <div>
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
          <router-link to="/shifts" class="btn btn-outline">← Back</router-link>
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
              <td class="text-sm text-slate mono">{{ p.lisPatientId || '—' }}</td>
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
    </div>

    <!-- Sticky confirm bar -->
    <transition name="slide-up">
      <div v-if="selected.length" class="sticky-bar">
        <div class="sticky-inner">
          <span style="font-weight:700; color:white">
            {{ selected.length }} patient{{ selected.length !== 1 ? 's' : '' }} selected for Shift {{ shiftNum }}
          </span>
          <div class="flex gap-2">
            <button class="btn-ghost" @click="selected = []">Clear selection</button>
            <button class="btn-confirm" @click="addSelected" :disabled="saving">
              {{ saving ? 'Adding…' : 'Confirm & Add to Shift' }}
            </button>
          </div>
        </div>
      </div>
    </transition>
  </div>
</template>

<script setup>
import { ref, computed, reactive, onMounted } from 'vue'
import { useAuthStore } from '../store/auth'
import { patientsApi, sessionsApi } from '../services/api'
import { useRouter, useRoute } from 'vue-router'

const auth   = useAuthStore()
const router = useRouter()
const route  = useRoute()

const shiftNum  = Number(route.query.shift) || null
const shiftDate = route.query.date     || new Date().toISOString().split('T')[0]
const clientId  = route.query.clientId || null

const dateLabel = new Date(shiftDate + 'T00:00:00').toLocaleDateString('en-PH', {
  weekday: 'long', year: 'numeric', month: 'long', day: 'numeric'
})

const patients     = ref([])
const search       = ref('')
const statusFilter = ref('')
const loading      = ref(false)
const saving       = ref(false)
const selected     = ref([])      // array of patient IDs
const chairs       = reactive({}) // patientId → chair string
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
  if (e.target.checked) {
    selected.value = [...new Set([...selected.value, ...ids])]
  } else {
    selected.value = selected.value.filter(id => !ids.includes(id))
  }
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

async function quickAdd(patient) {
  saving.value = true
  try {
    const res = await sessionsApi.create({
      patientId:   patient.id,
      sessionDate: shiftDate,
      shiftNumber: shiftNum,
      chair:       null,
      clientId:    clientId || null
    })
    if (res.data?.duplicate) {
      alert(`${patient.name} is already in Shift ${shiftNum}.`)
      inShift.value.add(patient.id)
    } else {
      inShift.value = new Set([...inShift.value, patient.id])
    }
  } catch (e) {
    alert(e.response?.data?.message || 'Failed to add patient')
  } finally { saving.value = false }
}

async function addSelected() {
  if (!selected.value.length) return
  saving.value = true
  let added = 0, skipped = 0
  try {
    for (const pid of selected.value) {
      const res = await sessionsApi.create({
        patientId:   pid,
        sessionDate: shiftDate,
        shiftNumber: shiftNum,
        chair:       chairs[pid]?.trim() || null,
        clientId:    clientId || null
      })
      if (res.data?.duplicate) skipped++
      else added++
    }
    router.push('/shifts')
  } catch (e) {
    alert(e.response?.data?.message || 'Failed to add patients')
    saving.value = false
  }
}

async function load() {
  loading.value = true
  try {
    const params = {}
    if (clientId) params.clientId = clientId
    const [pRes, sRes] = await Promise.all([
      patientsApi.getAll(params),
      sessionsApi.getAll({ shift: shiftNum, date: shiftDate, ...(clientId ? { clientId } : {}) })
    ])
    patients.value = pRes.data
    inShift.value  = new Set(sRes.data.map(s => s.patientId))
  } finally { loading.value = false }
}

onMounted(load)
</script>

<style scoped>
.toolbar {
  display: flex; align-items: center; gap: 12px;
  padding: 14px 18px; margin-bottom: 14px; flex-wrap: wrap;
}
.search-input { flex: 1; min-width: 200px; max-width: 360px; margin: 0; }
.sel-badge {
  background: var(--primary-mid); color: white;
  font-size: 12px; font-weight: 700; padding: 3px 10px; border-radius: 20px;
}
.row-selected td  { background: #eff6ff; }
.row-inshift td   { opacity: .6; background: #f8fafc; }
.row-selected:hover td { background: #dbeafe; }
.inshift-tag  { font-size: 10px; color: var(--primary-mid); font-weight: 600; margin-top: 2px; }
.mono         { font-family: 'JetBrains Mono', monospace; }
.text-warning { color: #d97706; font-weight: 600; }
.text-ok      { color: #059669; font-weight: 500; }

/* sticky confirm bar */
.sticky-bar {
  position: fixed; bottom: 0; left: 240px; right: 0;
  background: var(--primary-mid); padding: 14px 28px;
  box-shadow: 0 -4px 24px rgba(0,0,0,.18); z-index: 200;
}
.sticky-inner  { display: flex; align-items: center; justify-content: space-between; max-width: 1000px; margin: 0 auto; }
.btn-ghost     { background: transparent; border: 1.5px solid rgba(255,255,255,.4); color: white; padding: 8px 16px; border-radius: 7px; cursor: pointer; font-weight: 600; }
.btn-confirm   { background: white; color: var(--navy); padding: 8px 20px; border-radius: 7px; border: none; cursor: pointer; font-weight: 800; font-size: 14px; }
.btn-confirm:disabled { opacity: .6; cursor: not-allowed; }

.slide-up-enter-active, .slide-up-leave-active { transition: transform .2s ease, opacity .2s ease; }
.slide-up-enter-from, .slide-up-leave-to        { transform: translateY(100%); opacity: 0; }
</style>