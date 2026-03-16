<template>
  <div>
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">{{ patientName || 'Loading…' }}</div>
          <div class="page-sub">6-Month Lab Trend — Longitudinal Review</div>
        </div>
        <div class="flex gap-2">
          <select v-model="months" class="form-input" style="width:140px; margin:0" @change="load">
            <option :value="3">Last 3 months</option>
            <option :value="6">Last 6 months</option>
            <option :value="12">Last 12 months</option>
          </select>
          <button class="btn btn-outline btn-sm" @click="$router.back()">← Back</button>
        </div>
      </div>
    </div>

    <div class="doctrine-bar">
      Results are displayed as-is from the laboratory source. No interpretation. No risk labels. Data only.
    </div>

    <div v-if="error" class="error-bar">⚠ {{ error }}</div>
    <div v-if="loading" class="loading">Loading longitudinal data…</div>

    <div v-else-if="rows.length === 0 && !loading" class="empty-state">
      <div class="empty-icon">📊</div>
      <div class="empty-title">No results in the selected period</div>
      <div class="empty-sub">Try extending the date range.</div>
    </div>

    <div v-else class="table-card">
      <div class="card-header">
        <div>
          <div class="card-title">Analyte Trends</div>
          <div class="text-slate text-sm">{{ rows.length }} analytes · {{ allDates.length }} result dates</div>
        </div>
      </div>

      <div class="table-wrap" style="overflow-x:auto">
        <table>
          <thead>
            <tr>
              <th style="min-width:180px; position:sticky; left:0; z-index:2; background:var(--bg-light)">Analyte</th>
              <th style="min-width:60px; position:sticky; left:180px; z-index:2; background:var(--bg-light)">Unit</th>
              <th style="min-width:100px; position:sticky; left:240px; z-index:2; background:var(--bg-light)">Ref Range</th>
              <th v-for="d in allDates" :key="d" class="date-col" :title="d">
                {{ formatDateShort(d) }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in rows" :key="row.analyteCode">
              <td style="position:sticky; left:0; background:white; z-index:1">
                <div style="font-weight:500">{{ row.analyteName }}</div>
                <div class="text-slate" style="font-size:10px; font-family:monospace">{{ row.analyteCode }}</div>
              </td>
              <td style="position:sticky; left:180px; background:white; z-index:1" class="text-slate text-sm">{{ row.unit || '—' }}</td>
              <td style="position:sticky; left:240px; background:white; z-index:1" class="text-slate text-sm">{{ row.referenceRange || '—' }}</td>
              <td v-for="d in allDates" :key="d" class="value-cell">
                <template v-if="byDate(row, d)">
                  <span class="result-val" :class="flagClass(byDate(row, d).abnormalFlag)">
                    {{ byDate(row, d).displayValue }}
                  </span>
                  <span v-if="byDate(row, d).abnormalFlag && byDate(row, d).abnormalFlag !== 'N'" class="flag-tag" :class="byDate(row, d).abnormalFlag === 'H' ? 'tag-h' : 'tag-l'">
                    {{ byDate(row, d).abnormalFlag }}
                  </span>
                </template>
                <span v-else class="text-slate">—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="table-footer">
        <span>{{ rows.length }} analyte{{ rows.length !== 1 ? 's' : '' }} · {{ allDates.length }} dates</span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { resultsApi, patientsApi } from '../services/api'

const route     = useRoute()
const patientId = route.params.patientId

const rows        = ref([])
const patientName = ref('')
const loading     = ref(false)
const error       = ref('')
const months      = ref(6)

const allDates = computed(() => {
  const seen = new Set()
  for (const row of rows.value)
    for (const v of row.values)
      seen.add(v.date)
  return [...seen].sort((a, b) => a.localeCompare(b))
})

function byDate(row, date) {
  return row.values.find(v => v.date === date) || null
}

function flagClass(flag) {
  if (flag === 'H') return 'flag-h'
  if (flag === 'L') return 'flag-l'
  return ''
}

function formatDateShort(d) {
  if (!d) return '—'
  const [y, m, day] = d.split('-')
  return `${m}/${day}/${y.slice(2)}`
}

async function load() {
  loading.value = true
  error.value   = ''
  try {
    const { data } = await resultsApi.getLongitudinal(patientId, months.value)
    rows.value = data
  } catch (e) {
    error.value = `Error ${e.response?.status}: ${e.response?.data?.message || e.message}`
  } finally { loading.value = false }
}

async function loadPatient() {
  try {
    const { data } = await patientsApi.getById(patientId)
    patientName.value = data.name
  } catch { /* silently fail — name shown from results if possible */ }
}

onMounted(() => {
  loadPatient()
  load()
})
</script>

<style scoped>
.error-bar    { background:#fee2e2; border:1px solid #fca5a5; color:#dc2626; padding:12px 16px; border-radius:8px; margin-bottom:16px; font-size:13px; font-weight:600; }
.date-col     { text-align:center; font-size:11px; font-weight:600; color:var(--slate); min-width:80px; white-space:nowrap; }
.value-cell   { text-align:center; }
.result-val   { font-weight:700; font-size:13px; color:var(--navy); }
.result-val.flag-h { color:#dc2626; }
.result-val.flag-l { color:#2563eb; }
.flag-tag     { font-size:10px; font-weight:700; margin-left:2px; }
.tag-h        { color:#dc2626; }
.tag-l        { color:#2563eb; }
</style>
