<template>
  <div>
    <div class="page-header">
      <div class="page-title">Settings</div>
      <div class="page-sub">Branding & appearance</div>
    </div>

    <div v-if="loading" class="loading">Loading…</div>

    <div v-else style="display:grid; grid-template-columns:1fr 1fr; gap:20px; align-items:start; max-width:1000px">

      <!-- Tenant branding — PL Admin only -->
      <div v-if="auth.isPlAdmin" class="card">
        <div class="card-header">
          <div class="card-title">Tenant Branding</div>
          <span class="text-slate text-sm">{{ tenant?.name }}</span>
        </div>
        <div class="card-body">
          <div class="form-group">
            <label class="form-label">Primary Color</label>
            <div style="display:flex; align-items:center; gap:8px">
              <input type="color" v-model="tenantForm.primaryColor" class="color-picker" />
              <input type="text" v-model="tenantForm.primaryColor" class="form-input" placeholder="#1e3a8a" style="max-width:120px" />
              <div class="color-preview" :style="{ background: tenantForm.primaryColor }"></div>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">Logo URL</label>
            <input type="url" v-model="tenantForm.logoUrl" class="form-input" placeholder="https://…" />
            <img v-if="tenantForm.logoUrl" :src="tenantForm.logoUrl" class="logo-preview" alt="Logo" @error="logoTenantError = true" />
          </div>
          <div class="form-group">
            <label class="form-label">Footer Text</label>
            <textarea v-model="tenantForm.footerText" class="form-input" rows="2" placeholder="Report footer text…" />
          </div>
          <div class="form-actions">
            <button class="btn btn-primary" @click="saveTenantBranding" :disabled="savingTenant">
              {{ savingTenant ? 'Saving…' : 'Save Tenant Branding' }}
            </button>
            <span v-if="tenantSaved" class="saved-badge">✓ Saved</span>
          </div>
        </div>
      </div>

      <!-- Clinic branding — Clinic Admin or PL Admin -->
      <div v-if="auth.isClinicAdmin || auth.isPlAdmin" class="card">
        <div class="card-header">
          <div class="card-title">Clinic Branding</div>
          <span class="text-slate text-sm">{{ selectedClinic?.name }}</span>
        </div>
        <div class="card-body">
          <!-- PL Admin can pick clinic; Clinic Admin is locked to own clinic -->
          <div v-if="auth.isPlAdmin && clinics.length > 1" class="form-group">
            <label class="form-label">Clinic</label>
            <select v-model="selectedClinicId" class="form-input" @change="onClinicChange">
              <option v-for="c in clinics" :key="c.id" :value="c.id">{{ c.name }}</option>
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">Logo URL</label>
            <input type="url" v-model="clinicForm.logoUrl" class="form-input" placeholder="https://…" />
            <img v-if="clinicForm.logoUrl" :src="clinicForm.logoUrl" class="logo-preview" alt="Clinic Logo" @error="logoClinicError = true" />
          </div>
          <div class="form-actions">
            <button class="btn btn-primary" @click="saveClinicBranding" :disabled="savingClinic">
              {{ savingClinic ? 'Saving…' : 'Save Clinic Branding' }}
            </button>
            <span v-if="clinicSaved" class="saved-badge">✓ Saved</span>
          </div>
        </div>
      </div>

      <!-- URR / Audit Defense export — charge nurse and above -->
      <div v-if="auth.isAdmin || auth.isChargeNurse" class="card" style="grid-column: span 2">
        <div class="card-header">
          <div class="card-title">Audit Defense — URR / Kt/V Report</div>
          <span class="text-slate text-sm">Dialysis adequacy metrics per session date</span>
        </div>
        <div class="card-body">
          <div style="display:flex; align-items:flex-end; gap:12px; flex-wrap:wrap">
            <div class="form-group" style="margin:0">
              <label class="form-label">Session Date</label>
              <input type="date" v-model="urrDate" class="form-input" style="width:160px" />
            </div>
            <button class="btn btn-primary" @click="loadUrr" :disabled="loadingUrr">
              {{ loadingUrr ? 'Loading…' : 'Generate Report' }}
            </button>
            <button v-if="urrRows.length" class="btn btn-outline" @click="exportUrrCsv">
              Export CSV
            </button>
          </div>

          <div v-if="urrRows.length" style="margin-top:16px">
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Patient</th>
                    <th>LIS ID</th>
                    <th>BUN Pre</th>
                    <th>BUN Post</th>
                    <th>URR (%)</th>
                    <th>Kt/V</th>
                    <th>Accession Pre</th>
                    <th>Accession Post</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in urrRows" :key="r.patientName">
                    <td style="font-weight:500">{{ r.patientName }}</td>
                    <td class="text-slate text-sm">{{ r.lisPatientId || '—' }}</td>
                    <td>{{ r.bunPre ?? '—' }}</td>
                    <td>{{ r.bunPost ?? '—' }}</td>
                    <td>
                      <span v-if="r.urr != null" :class="r.urr >= 65 ? 'adequate' : 'inadequate'">
                        {{ r.urr }}%
                      </span>
                      <span v-else class="text-slate">—</span>
                    </td>
                    <td>
                      <span v-if="r.ktV != null" :class="r.ktV >= 1.2 ? 'adequate' : 'inadequate'">
                        {{ r.ktV }}
                      </span>
                      <span v-else class="text-slate">—</span>
                    </td>
                    <td class="text-slate text-sm">{{ r.accessionPre || '—' }}</td>
                    <td class="text-slate text-sm">{{ r.accessionPost || '—' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div class="text-slate text-sm" style="margin-top:8px">
              URR ≥ 65% adequate · Kt/V ≥ 1.2 adequate (estimated, 3.5h session, avg UF/W)
            </div>
          </div>
          <div v-else-if="!loadingUrr && urrQueried" class="empty-state" style="padding:20px 0">
            <div class="text-slate text-sm">No sessions or BUN data found for this date.</div>
          </div>
        </div>
      </div>

      <!-- Code Mappings — OBR-4 and OBX-3 — PL Admin / Clinic Admin -->
      <div v-if="auth.isPlAdmin || auth.isClinicAdmin" class="card" style="grid-column: span 2">
        <div class="card-header">
          <div class="card-title">HL7 Code Mappings</div>
          <span class="text-slate text-sm">Map incoming OBR-4 test codes and OBX-3 analyte codes to the SXA catalog</span>
        </div>
        <div class="card-body">
          <!-- Tabs -->
          <div class="map-tabs">
            <button :class="['map-tab', mapTab === 'test' && 'active']" @click="mapTab = 'test'">
              Test Mappings (OBR-4)
              <span class="map-tab-count">{{ testMaps.length }}</span>
            </button>
            <button :class="['map-tab', mapTab === 'analyte' && 'active']" @click="mapTab = 'analyte'">
              Analyte Mappings (OBX-3)
              <span class="map-tab-count">{{ analyteMaps.length }}</span>
            </button>
          </div>

          <!-- Test mappings tab -->
          <div v-if="mapTab === 'test'">
            <div class="map-add-row">
              <input v-model="newTestCode" class="form-input map-code-input" placeholder="OBR-4 code (e.g. CBC)" @keyup.enter="addTestMap" />
              <select v-model="newTestSxaId" class="form-input map-select">
                <option value="">— Select SXA Test —</option>
                <option v-for="s in sxaTests" :key="s.sxaTestId" :value="s.sxaTestId">
                  {{ s.canonicalName }} ({{ s.sxaTestId }})
                </option>
              </select>
              <button class="btn btn-primary" @click="addTestMap" :disabled="addingTestMap || !newTestCode || !newTestSxaId">
                {{ addingTestMap ? 'Adding…' : '+ Add' }}
              </button>
              <span v-if="testMapError" class="map-error">{{ testMapError }}</span>
            </div>
            <div class="table-wrap" style="margin-top:10px">
              <table>
                <thead>
                  <tr>
                    <th>OBR-4 Code (from LIS)</th>
                    <th>SXA Test</th>
                    <th>SXA ID</th>
                    <th style="width:60px"></th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="m in testMaps" :key="m.id">
                    <td><code>{{ m.tenantTestCode }}</code></td>
                    <td>{{ m.canonicalName }}</td>
                    <td class="text-slate text-sm">{{ m.sxaTestId }}</td>
                    <td>
                      <button class="icon-btn delete" @click="removeTestMap(m)" title="Delete mapping">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/></svg>
                      </button>
                    </td>
                  </tr>
                  <tr v-if="testMaps.length === 0">
                    <td colspan="4" class="text-slate text-sm" style="text-align:center; padding:16px">No test mappings yet.</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- Analyte mappings tab -->
          <div v-if="mapTab === 'analyte'">
            <div class="map-add-row">
              <input v-model="newAnalyteCode" class="form-input map-code-input" placeholder="OBX-3 code (e.g. HGB)" @keyup.enter="addAnalyteMap" />
              <select v-model="newAnalyteSxaCode" class="form-input map-select">
                <option value="">— Select SXA Analyte —</option>
                <option v-for="a in sxaAnalytes" :key="a.analyteCode" :value="a.analyteCode">
                  {{ a.displayName }} ({{ a.analyteCode }})
                </option>
              </select>
              <button class="btn btn-primary" @click="addAnalyteMap" :disabled="addingAnalyteMap || !newAnalyteCode || !newAnalyteSxaCode">
                {{ addingAnalyteMap ? 'Adding…' : '+ Add' }}
              </button>
              <span v-if="analyteMapError" class="map-error">{{ analyteMapError }}</span>
            </div>
            <div class="table-wrap" style="margin-top:10px">
              <table>
                <thead>
                  <tr>
                    <th>OBX-3 Code (from LIS)</th>
                    <th>SXA Analyte</th>
                    <th>SXA Code</th>
                    <th style="width:60px"></th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="m in analyteMaps" :key="m.id">
                    <td><code>{{ m.tenantAnalyteCode }}</code></td>
                    <td>{{ m.displayName }}</td>
                    <td class="text-slate text-sm">{{ m.analyteCode }}</td>
                    <td>
                      <button class="icon-btn delete" @click="removeAnalyteMap(m)" title="Delete mapping">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/></svg>
                      </button>
                    </td>
                  </tr>
                  <tr v-if="analyteMaps.length === 0">
                    <td colspan="4" class="text-slate text-sm" style="text-align:center; padding:16px">No analyte mappings yet.</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useAuthStore } from '../store/auth'
import { tenantApi, clinicsApi, exportApi } from '../services/api'

const auth = useAuthStore()

const tenant         = ref(null)
const clinics        = ref([])
const loading        = ref(false)
const savingTenant   = ref(false)
const savingClinic   = ref(false)
const tenantSaved    = ref(false)
const clinicSaved    = ref(false)
const logoTenantError = ref(false)
const logoClinicError = ref(false)

const tenantForm = ref({ primaryColor: '#1e3a8a', logoUrl: '', footerText: '' })
const clinicForm = ref({ logoUrl: '' })

const selectedClinicId = ref(null)
const selectedClinic   = computed(() => clinics.value.find(c => c.id === selectedClinicId.value))

// URR
const urrDate    = ref(new Date().toISOString().split('T')[0])
const urrRows    = ref([])
const loadingUrr = ref(false)
const urrQueried = ref(false)

// Code mappings
const mapTab           = ref('test')
const sxaTests         = ref([])
const sxaAnalytes      = ref([])
const testMaps         = ref([])
const analyteMaps      = ref([])
const newTestCode      = ref('')
const newTestSxaId     = ref('')
const newAnalyteCode   = ref('')
const newAnalyteSxaCode = ref('')
const addingTestMap    = ref(false)
const addingAnalyteMap = ref(false)
const testMapError     = ref('')
const analyteMapError  = ref('')

async function loadData() {
  loading.value = true
  try {
    if (auth.isPlAdmin || auth.isClinicAdmin) {
      const [tenantRes, clinicsRes, sxaTestsRes, sxaAnalytesRes, testMapsRes, analyteMapsRes] = await Promise.all([
        tenantApi.get(),
        clinicsApi.getAll(),
        tenantApi.getSxaTests(),
        tenantApi.getSxaAnalytes(),
        tenantApi.getTestMaps(),
        tenantApi.getAnalyteMaps()
      ])
      tenant.value = tenantRes.data
      tenantForm.value = {
        primaryColor: tenant.value.primaryColor || '#1e3a8a',
        logoUrl:      tenant.value.logoUrl || '',
        footerText:   tenant.value.footerText || ''
      }
      clinics.value       = clinicsRes.data
      sxaTests.value      = sxaTestsRes.data
      sxaAnalytes.value   = sxaAnalytesRes.data
      testMaps.value      = testMapsRes.data
      analyteMaps.value   = analyteMapsRes.data
      // Set default clinic selection
      if (auth.isClinicAdmin && auth.client?.id) {
        selectedClinicId.value = auth.client.id
      } else if (clinics.value.length > 0) {
        selectedClinicId.value = clinics.value[0].id
      }
      onClinicChange()
    }
  } finally { loading.value = false }
}

async function addTestMap() {
  if (!newTestCode.value || !newTestSxaId.value) return
  testMapError.value = ''
  addingTestMap.value = true
  try {
    const { data } = await tenantApi.createTestMap({ tenantTestCode: newTestCode.value, sxaTestId: newTestSxaId.value })
    testMaps.value.push(data)
    testMaps.value.sort((a, b) => a.tenantTestCode.localeCompare(b.tenantTestCode))
    newTestCode.value  = ''
    newTestSxaId.value = ''
  } catch (e) {
    testMapError.value = e.response?.data?.title || e.response?.data || 'Error adding mapping.'
  } finally { addingTestMap.value = false }
}

async function removeTestMap(m) {
  if (!confirm(`Delete mapping for "${m.tenantTestCode}"?`)) return
  await tenantApi.deleteTestMap(m.id)
  testMaps.value = testMaps.value.filter(x => x.id !== m.id)
}

async function addAnalyteMap() {
  if (!newAnalyteCode.value || !newAnalyteSxaCode.value) return
  analyteMapError.value = ''
  addingAnalyteMap.value = true
  try {
    const { data } = await tenantApi.createAnalyteMap({ tenantAnalyteCode: newAnalyteCode.value, analyteCode: newAnalyteSxaCode.value })
    analyteMaps.value.push(data)
    analyteMaps.value.sort((a, b) => a.tenantAnalyteCode.localeCompare(b.tenantAnalyteCode))
    newAnalyteCode.value    = ''
    newAnalyteSxaCode.value = ''
  } catch (e) {
    analyteMapError.value = e.response?.data?.title || e.response?.data || 'Error adding mapping.'
  } finally { addingAnalyteMap.value = false }
}

async function removeAnalyteMap(m) {
  if (!confirm(`Delete mapping for "${m.tenantAnalyteCode}"?`)) return
  await tenantApi.deleteAnalyteMap(m.id)
  analyteMaps.value = analyteMaps.value.filter(x => x.id !== m.id)
}

function onClinicChange() {
  const c = selectedClinic.value
  clinicForm.value.logoUrl = c?.logoUrl || ''
  logoClinicError.value = false
}

async function saveTenantBranding() {
  savingTenant.value = true
  try {
    await tenantApi.updateBranding({
      primaryColor: tenantForm.value.primaryColor || null,
      logoUrl:      tenantForm.value.logoUrl || null,
      footerText:   tenantForm.value.footerText || null
    })
    tenantSaved.value = true
    setTimeout(() => { tenantSaved.value = false }, 3000)
  } finally { savingTenant.value = false }
}

async function saveClinicBranding() {
  if (!selectedClinicId.value) return
  savingClinic.value = true
  try {
    await clinicsApi.updateBranding(selectedClinicId.value, {
      logoUrl: clinicForm.value.logoUrl || null
    })
    clinicSaved.value = true
    setTimeout(() => { clinicSaved.value = false }, 3000)
  } finally { savingClinic.value = false }
}

async function loadUrr() {
  loadingUrr.value = true
  urrQueried.value = true
  try {
    const { data } = await exportApi.urr({ date: urrDate.value })
    urrRows.value = data
  } finally { loadingUrr.value = false }
}

function exportUrrCsv() {
  const header = 'Patient,LIS ID,BUN Pre,BUN Post,URR (%),Kt/V,Accession Pre,Accession Post\n'
  const rows = urrRows.value.map(r =>
    `${r.patientName},${r.lisPatientId || ''},${r.bunPre ?? ''},${r.bunPost ?? ''},${r.urr ?? ''},${r.ktV ?? ''},${r.accessionPre || ''},${r.accessionPost || ''}`
  ).join('\n')
  const blob = new Blob([header + rows], { type: 'text/csv' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `dx7_urr_${urrDate.value}.csv`
  a.click()
  URL.revokeObjectURL(a.href)
}

onMounted(loadData)
</script>

<style scoped>
.form-group    { margin-bottom:16px; }
.form-actions  { display:flex; align-items:center; gap:10px; margin-top:20px; }
.color-picker  { width:44px; height:36px; padding:2px; border:1.5px solid var(--border); border-radius:6px; cursor:pointer; }
.color-preview { width:36px; height:36px; border-radius:6px; border:1.5px solid var(--border); }
.logo-preview  { max-height:60px; max-width:100%; margin-top:8px; border-radius:6px; border:1px solid var(--border); }
.saved-badge   { font-size:13px; font-weight:600; color:#16a34a; }
.adequate      { font-weight:700; color:#16a34a; }
.inadequate    { font-weight:700; color:#dc2626; }

/* Code mappings */
.map-tabs        { display:flex; gap:4px; border-bottom:2px solid var(--border); margin-bottom:16px; }
.map-tab         { background:none; border:none; padding:8px 16px; font-size:13px; font-weight:500; color:var(--text-muted); cursor:pointer; border-bottom:2px solid transparent; margin-bottom:-2px; border-radius:4px 4px 0 0; display:flex; align-items:center; gap:6px; }
.map-tab:hover   { background:var(--bg-hover); color:var(--text); }
.map-tab.active  { color:var(--primary); border-bottom-color:var(--primary); }
.map-tab-count   { background:var(--bg-muted); color:var(--text-muted); font-size:11px; font-weight:600; padding:1px 6px; border-radius:10px; }
.map-add-row     { display:flex; align-items:center; gap:8px; flex-wrap:wrap; padding:10px; background:var(--bg-muted); border-radius:8px; }
.map-code-input  { width:180px !important; font-family:monospace; }
.map-select      { flex:1; min-width:200px; }
.map-error       { color:#dc2626; font-size:12px; }
</style>
