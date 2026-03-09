<template>
  <div>
    <!-- Header -->
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">{{ session?.patientName || 'Loading…' }}</div>
          <div class="page-sub">
            Session · {{ session?.sessionDate }} · Shift {{ session?.shiftNumber }}
            <span v-if="session?.chair"> · Chair {{ session.chair }}</span>
          </div>
        </div>
        <div class="flex gap-2">
          <button v-if="auth.canExport" class="btn btn-outline btn-sm" @click="showPrintModal = true">
            🖨️ Print / PDF
          </button>
          <button v-if="auth.canExport" class="btn btn-outline btn-sm" @click="exportCsv">
            ↓ Export CSV
          </button>
          <button v-if="auth.canPrint" class="btn btn-primary btn-sm" @click="showReport = true">
            🖨 Print / PDF
          </button>
          <router-link to="/shifts" class="btn btn-outline btn-sm">← Back</router-link>
        </div>
      </div>
    </div>

    <div class="doctrine-bar">
      Results are displayed as-is from the laboratory source. No interpretation. No risk labels. Data only.
    </div>

    <div v-if="loading" class="loading">Loading results…</div>

    <div v-else style="display:grid; grid-template-columns:1fr 340px; gap:20px; align-items:start">

      <!-- LEFT: Results -->
      <div>
        <!-- Priority Labs -->
        <div class="card" style="margin-bottom:16px">
          <div class="card-header">
            <div class="card-title">⚡ Priority Labs</div>
            <div class="text-slate text-sm">Potassium · Phosphorus · Hemoglobin</div>
          </div>
          <div class="card-body" style="display:grid; grid-template-columns:repeat(3,1fr); gap:16px">
            <div v-for="code in ['K', 'PHOS', 'HGB']" :key="code">
              <div v-if="resultsByCode[code]" class="stat-card">
                <div class="stat-label">{{ resultsByCode[code].testName }}</div>
                <div class="stat-value" :class="flagClass(resultsByCode[code].abnormalFlag)">
                  {{ resultsByCode[code].resultValue }}
                  <span class="stat-unit">{{ resultsByCode[code].resultUnit }}</span>
                </div>
                <div class="stat-meta">
                  <span v-if="resultsByCode[code].abnormalFlag" class="badge" :class="resultsByCode[code].abnormalFlag === 'H' ? 'badge-h' : 'badge-l'">
                    {{ resultsByCode[code].abnormalFlag }}
                  </span>
                  <span class="text-slate text-sm">{{ daysSince(resultsByCode[code].daysSince) }}</span>
                </div>
              </div>
              <div v-else class="stat-card stat-empty">
                <div class="stat-label">{{ code }}</div>
                <div style="color:var(--slate); font-size:13px">No result</div>
              </div>
            </div>
          </div>
        </div>

        <!-- Full results table -->
        <div class="table-card">
          <div class="card-header">
            <div class="card-title">All Lab Results</div>
            <div class="text-slate text-sm">{{ results.length }} tests · Pass-through from HL7</div>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Test</th>
                  <th>Value</th>
                  <th>Unit</th>
                  <th>Flag</th>
                  <th>Reference</th>
                  <th>Date</th>
                  <th>Source</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="r in results" :key="r.id">
                  <td>
                    <div style="font-weight:500">{{ r.testName }}</div>
                    <div class="text-slate" style="font-size:11px">{{ r.testCode }}</div>
                  </td>
                  <td>
                    <span class="result-value" :class="flagClass(r.abnormalFlag)">
                      {{ r.resultValue || '—' }}
                    </span>
                  </td>
                  <td class="text-slate text-sm">{{ r.resultUnit || '—' }}</td>
                  <td>
                    <span v-if="r.abnormalFlag" class="badge" :class="r.abnormalFlag === 'H' ? 'badge-h' : 'badge-l'">
                      {{ r.abnormalFlag }}
                    </span>
                    <span v-else class="text-slate">—</span>
                  </td>
                  <td class="text-slate text-sm">{{ r.referenceRange || '—' }}</td>
                  <td>
                    <div class="text-sm">{{ r.resultDate }}</div>
                    <div v-if="r.daysSince > 30" class="stale-indicator">⚠ {{ r.daysSince }}d ago</div>
                    <div v-else class="text-sm text-slate">{{ daysSince(r.daysSince) }}</div>
                  </td>
                  <td class="text-slate text-sm">{{ r.sourceLab || '—' }}</td>
                </tr>
                <tr v-if="results.length === 0">
                  <td colspan="7" style="text-align:center; padding:40px; color:var(--slate)">
                    No results found for this patient
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- RIGHT: MD Notes -->
      <div class="card">
        <div class="card-header">
          <div class="card-title">📋 MD Notes</div>
          <span class="text-slate text-sm">Session notes</span>
        </div>
        <div class="card-body">
          <div v-if="auth.canWriteNotes" style="margin-bottom:16px">
            <textarea v-model="newNote" class="form-input" placeholder="Enter session note…" rows="4"/>
            <button class="btn btn-primary" style="width:100%; margin-top:8px" @click="saveNote" :disabled="!newNote.trim()">
              Save Note
            </button>
          </div>
          <div v-if="notes.length === 0" class="empty-state" style="padding:24px 0">
            <div class="text-slate text-sm">No notes for this session.</div>
          </div>
          <div v-for="note in notes" :key="note.id" class="note-item">
            <div class="note-header">
              <span style="font-weight:600; font-size:13px">{{ note.mdName }}</span>
              <span class="text-slate text-sm">{{ formatDate(note.createdAt) }}</span>
            </div>
            <div class="note-body" v-if="editingNote !== note.id">{{ note.noteText }}</div>
            <textarea v-else v-model="editNoteText" class="form-input" rows="3" style="margin-top:8px"/>
            <div v-if="auth.canWriteNotes && note.canEdit" class="note-actions">
              <button v-if="editingNote !== note.id" class="btn btn-outline btn-sm" @click="startEditNote(note)">Edit</button>
              <button v-else class="btn btn-primary btn-sm" @click="updateNote(note)">Save</button>
              <button v-if="editingNote === note.id" class="btn btn-outline btn-sm" @click="editingNote = null">Cancel</button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ══════════════════════════════════════════
         PRINT / PDF MODAL
    ══════════════════════════════════════════ -->
    <div v-if="showPrintModal" class="modal-backdrop" @click.self="showPrintModal = false">
      <div class="modal" style="width:680px; max-height:90vh">
        <div class="modal-header">
          <div>
            <div class="modal-title">🖨️ Print / Download PDF</div>
            <div class="text-slate text-sm" style="margin-top:2px">{{ session?.patientName }}</div>
          </div>
          <button class="modal-close" @click="showPrintModal = false">✕</button>
        </div>

        <!-- Print options -->
        <div class="modal-body" style="padding-bottom:0">
          <div style="display:flex; gap:10px; margin-bottom:18px; flex-wrap:wrap">
            <label class="print-opt" :class="{ active: printOpts.priority }">
              <input type="checkbox" v-model="printOpts.priority" /> Priority Labs
            </label>
            <label class="print-opt" :class="{ active: printOpts.allResults }">
              <input type="checkbox" v-model="printOpts.allResults" /> All Results
            </label>
            <label class="print-opt" :class="{ active: printOpts.notes }">
              <input type="checkbox" v-model="printOpts.notes" /> MD Notes
            </label>
          </div>
        </div>

        <!-- Preview area -->
        <div class="modal-body" style="padding-top:0; overflow-y:auto; max-height:55vh">
          <div id="print-preview" class="print-preview">

            <!-- Report Header -->
            <div class="pr-header">
              <div class="pr-logo">DX7</div>
              <div class="pr-header-info">
                <div class="pr-clinic">{{ auth.tenant?.name }} — {{ auth.client?.name }}</div>
                <div class="pr-meta">Lab Results Report · Printed {{ printDate }}</div>
              </div>
            </div>

            <!-- Patient info bar -->
            <div class="pr-patient-bar">
              <div class="pr-patient-detail">
                <span class="pr-label">Patient</span>
                <span class="pr-val">{{ session?.patientName }}</span>
              </div>
              <div class="pr-patient-detail">
                <span class="pr-label">Session Date</span>
                <span class="pr-val">{{ session?.sessionDate }}</span>
              </div>
              <div class="pr-patient-detail">
                <span class="pr-label">Shift</span>
                <span class="pr-val">{{ session?.shiftNumber }}</span>
              </div>
              <div class="pr-patient-detail" v-if="session?.chair">
                <span class="pr-label">Chair</span>
                <span class="pr-val">{{ session.chair }}</span>
              </div>
              <div class="pr-patient-detail">
                <span class="pr-label">Printed by</span>
                <span class="pr-val">{{ auth.user?.name }}</span>
              </div>
            </div>

            <!-- Priority Labs section -->
            <div v-if="printOpts.priority">
              <div class="pr-section-title">Priority Labs</div>
              <div class="pr-priority-grid">
                <div v-for="code in ['K', 'PHOS', 'HGB']" :key="code" class="pr-priority-cell">
                  <div class="pr-test-name">{{ resultsByCode[code]?.testName || code }}</div>
                  <div class="pr-val-big" :class="flagClass(resultsByCode[code]?.abnormalFlag)">
                    {{ resultsByCode[code]?.resultValue || '—' }}
                    <span class="pr-unit">{{ resultsByCode[code]?.resultUnit }}</span>
                  </div>
                  <div v-if="resultsByCode[code]?.abnormalFlag" class="pr-flag" :class="resultsByCode[code].abnormalFlag === 'H' ? 'pr-flag-h' : 'pr-flag-l'">
                    {{ resultsByCode[code].abnormalFlag === 'H' ? '▲ HIGH' : '▼ LOW' }}
                  </div>
                  <div class="pr-ref">{{ resultsByCode[code]?.referenceRange || '' }}</div>
                </div>
              </div>
            </div>

            <!-- All Results table -->
            <div v-if="printOpts.allResults && results.length">
              <div class="pr-section-title">All Lab Results</div>
              <table class="pr-table">
                <thead>
                  <tr>
                    <th>Test</th>
                    <th>Code</th>
                    <th>Value</th>
                    <th>Unit</th>
                    <th>Flag</th>
                    <th>Reference Range</th>
                    <th>Date</th>
                    <th>Source</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in results" :key="r.id" :class="r.abnormalFlag ? 'pr-row-flag' : ''">
                    <td>{{ r.testName }}</td>
                    <td class="pr-code">{{ r.testCode }}</td>
                    <td class="pr-result" :class="flagClass(r.abnormalFlag)">{{ r.resultValue || '—' }}</td>
                    <td>{{ r.resultUnit || '—' }}</td>
                    <td>
                      <span v-if="r.abnormalFlag" :class="r.abnormalFlag === 'H' ? 'pr-flag-h' : 'pr-flag-l'">{{ r.abnormalFlag }}</span>
                      <span v-else>—</span>
                    </td>
                    <td>{{ r.referenceRange || '—' }}</td>
                    <td>{{ r.resultDate }}</td>
                    <td>{{ r.sourceLab || '—' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- MD Notes -->
            <div v-if="printOpts.notes && notes.length">
              <div class="pr-section-title">MD Notes</div>
              <div v-for="note in notes" :key="note.id" class="pr-note">
                <div class="pr-note-meta">{{ note.mdName }} · {{ formatDate(note.createdAt) }}</div>
                <div class="pr-note-body">{{ note.noteText }}</div>
              </div>
            </div>

            <!-- Footer -->
            <div class="pr-footer">
              This report was generated by Dx7 Clinical Information System · {{ auth.tenant?.name }}.
              For clinical use only. Not for distribution.
            </div>
          </div>
        </div>

        <div class="modal-footer">
          <button class="btn btn-outline" @click="showPrintModal = false">Cancel</button>
          <button class="btn btn-outline" @click="downloadPdf">↓ Download PDF</button>
          <button class="btn btn-primary" @click="printReport">🖨️ Print</button>
        </div>
      </div>
    </div>

  </div>
  <!-- Result Report Modal -->
  <ResultReportModal
    v-if="showReport"
    :patientName="session?.patientName || ''"
    :philhealthNo="session?.philhealthNo || ''"
    :birthdate="session?.birthdate || ''"
    :gender="session?.gender || ''"
    :results="results"
    :reportDate="session?.sessionDate || today"
    @close="showReport = false"
  />
</template>

<script setup>
import { ref, computed, onMounted, reactive } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '../store/auth'
import { sessionsApi, resultsApi, notesApi, exportApi } from '../services/api'

const auth      = useAuthStore()
const route     = useRoute()
const sessionId = route.params.sessionId
const today = new Date().toLocaleDateString('en-PH', { year: 'numeric', month: 'long', day: 'numeric' })

const session      = ref(null)
const results      = ref([])
const notes        = ref([])
const loading      = ref(false)
const newNote      = ref('')
const editingNote  = ref(null)
const editNoteText = ref('')
const showReport = ref(false)
const showPrintModal = ref(false)

const printOpts = reactive({ priority: true, allResults: true, notes: true })
const printDate = new Date().toLocaleString('en-PH', { dateStyle: 'long', timeStyle: 'short' })

const resultsByCode = computed(() =>
  Object.fromEntries(results.value.map(r => [r.testCode, r]))
)

function daysSince(d) {
  if (d === 0) return 'Today'
  if (d === 1) return '1 day ago'
  return `${d} days ago`
}

function flagClass(flag) {
  if (flag === 'H') return 'flag-h'
  if (flag === 'L') return 'flag-l'
  return ''
}

function formatDate(dt) {
  return new Date(dt).toLocaleString('en-PH', { dateStyle: 'medium', timeStyle: 'short' })
}

// ── Print using browser print dialog ──
function printReport() {
  const content = document.getElementById('print-preview').innerHTML
  const win = window.open('', '_blank', 'width=900,height=700')
  win.document.write(`
    <!DOCTYPE html>
    <html>
    <head>
      <title>Lab Results — ${session.value?.patientName}</title>
      <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: 'Inter', Arial, sans-serif; font-size: 12px; color: #111827; padding: 24px; }
        .pr-header { display: flex; align-items: center; gap: 16px; border-bottom: 2px solid #1d4ed8; padding-bottom: 12px; margin-bottom: 14px; }
        .pr-logo { font-size: 28px; font-weight: 900; color: #1e3a8a; letter-spacing: -1px; }
        .pr-clinic { font-size: 14px; font-weight: 700; color: #111827; }
        .pr-meta { font-size: 11px; color: #6b7280; margin-top: 2px; }
        .pr-patient-bar { display: flex; gap: 20px; background: #eff6ff; border-radius: 6px; padding: 10px 14px; margin-bottom: 16px; flex-wrap: wrap; }
        .pr-patient-detail { display: flex; flex-direction: column; }
        .pr-label { font-size: 9px; text-transform: uppercase; letter-spacing: 0.5px; color: #6b7280; font-weight: 600; }
        .pr-val { font-size: 12px; font-weight: 700; color: #111827; margin-top: 2px; }
        .pr-section-title { font-size: 13px; font-weight: 700; color: #1e3a8a; border-bottom: 1px solid #bfdbfe; padding-bottom: 4px; margin: 16px 0 10px; text-transform: uppercase; letter-spacing: 0.5px; }
        .pr-priority-grid { display: grid; grid-template-columns: repeat(3,1fr); gap: 12px; margin-bottom: 4px; }
        .pr-priority-cell { border: 1.5px solid #e5e7eb; border-radius: 6px; padding: 12px; text-align: center; }
        .pr-test-name { font-size: 10px; font-weight: 600; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }
        .pr-val-big { font-size: 26px; font-weight: 800; color: #111827; line-height: 1; }
        .pr-unit { font-size: 11px; font-weight: 400; color: #6b7280; }
        .pr-flag { font-size: 10px; font-weight: 700; margin-top: 4px; }
        .pr-flag-h { color: #dc2626; }
        .pr-flag-l { color: #2563eb; }
        .flag-h { color: #dc2626 !important; }
        .flag-l { color: #2563eb !important; }
        .pr-ref { font-size: 10px; color: #9ca3af; margin-top: 2px; }
        .pr-table { width: 100%; border-collapse: collapse; font-size: 11px; }
        .pr-table th { background: #f9fafb; padding: 7px 10px; text-align: left; font-weight: 600; color: #6b7280; border-bottom: 1px solid #e5e7eb; font-size: 10px; text-transform: uppercase; letter-spacing: 0.3px; }
        .pr-table td { padding: 7px 10px; border-bottom: 1px solid #f3f4f6; }
        .pr-row-flag { background: #fff7f7; }
        .pr-result { font-weight: 700; }
        .pr-code { color: #9ca3af; font-size: 10px; }
        .pr-note { border: 1px solid #e5e7eb; border-radius: 6px; padding: 10px 12px; margin-bottom: 8px; }
        .pr-note-meta { font-size: 10px; color: #6b7280; margin-bottom: 4px; font-weight: 600; }
        .pr-note-body { font-size: 12px; color: #374151; line-height: 1.5; white-space: pre-wrap; }
        .pr-footer { margin-top: 24px; padding-top: 10px; border-top: 1px solid #e5e7eb; font-size: 10px; color: #9ca3af; text-align: center; }
        @media print { body { padding: 0; } }
      </style>
    </head>
    <body>${content}</body>
    </html>
  `)
  win.document.close()
  win.focus()
  setTimeout(() => { win.print() }, 500)
}

// ── Download as PDF via browser print-to-PDF ──
function downloadPdf() {
  const content = document.getElementById('print-preview').innerHTML
  const patientName = session.value?.patientName?.replace(/ /g, '_') || 'results'
  const win = window.open('', '_blank', 'width=900,height=700')
  win.document.write(`
    <!DOCTYPE html>
    <html>
    <head>
      <title>Lab Results — ${session.value?.patientName}</title>
      <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: 'Inter', Arial, sans-serif; font-size: 12px; color: #111827; padding: 24px; }
        .pr-header { display: flex; align-items: center; gap: 16px; border-bottom: 2px solid #1d4ed8; padding-bottom: 12px; margin-bottom: 14px; }
        .pr-logo { font-size: 28px; font-weight: 900; color: #1e3a8a; letter-spacing: -1px; }
        .pr-clinic { font-size: 14px; font-weight: 700; color: #111827; }
        .pr-meta { font-size: 11px; color: #6b7280; margin-top: 2px; }
        .pr-patient-bar { display: flex; gap: 20px; background: #eff6ff; border-radius: 6px; padding: 10px 14px; margin-bottom: 16px; flex-wrap: wrap; }
        .pr-patient-detail { display: flex; flex-direction: column; }
        .pr-label { font-size: 9px; text-transform: uppercase; letter-spacing: 0.5px; color: #6b7280; font-weight: 600; }
        .pr-val { font-size: 12px; font-weight: 700; color: #111827; margin-top: 2px; }
        .pr-section-title { font-size: 13px; font-weight: 700; color: #1e3a8a; border-bottom: 1px solid #bfdbfe; padding-bottom: 4px; margin: 16px 0 10px; text-transform: uppercase; letter-spacing: 0.5px; }
        .pr-priority-grid { display: grid; grid-template-columns: repeat(3,1fr); gap: 12px; margin-bottom: 4px; }
        .pr-priority-cell { border: 1.5px solid #e5e7eb; border-radius: 6px; padding: 12px; text-align: center; }
        .pr-test-name { font-size: 10px; font-weight: 600; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }
        .pr-val-big { font-size: 26px; font-weight: 800; color: #111827; line-height: 1; }
        .pr-unit { font-size: 11px; font-weight: 400; color: #6b7280; }
        .pr-flag { font-size: 10px; font-weight: 700; margin-top: 4px; }
        .pr-flag-h { color: #dc2626; }
        .pr-flag-l { color: #2563eb; }
        .flag-h { color: #dc2626 !important; }
        .flag-l { color: #2563eb !important; }
        .pr-ref { font-size: 10px; color: #9ca3af; margin-top: 2px; }
        .pr-table { width: 100%; border-collapse: collapse; font-size: 11px; }
        .pr-table th { background: #f9fafb; padding: 7px 10px; text-align: left; font-weight: 600; color: #6b7280; border-bottom: 1px solid #e5e7eb; font-size: 10px; text-transform: uppercase; letter-spacing: 0.3px; }
        .pr-table td { padding: 7px 10px; border-bottom: 1px solid #f3f4f6; }
        .pr-row-flag { background: #fff7f7; }
        .pr-result { font-weight: 700; }
        .pr-code { color: #9ca3af; font-size: 10px; }
        .pr-note { border: 1px solid #e5e7eb; border-radius: 6px; padding: 10px 12px; margin-bottom: 8px; }
        .pr-note-meta { font-size: 10px; color: #6b7280; margin-bottom: 4px; font-weight: 600; }
        .pr-note-body { font-size: 12px; color: #374151; line-height: 1.5; white-space: pre-wrap; }
        .pr-footer { margin-top: 24px; padding-top: 10px; border-top: 1px solid #e5e7eb; font-size: 10px; color: #9ca3af; text-align: center; }
        @media print { body { padding: 0; } }
      </style>
    </head>
    <body>${content}</body>
    </html>
  `)
  win.document.close()
  win.focus()
  setTimeout(() => {
    win.document.title = `DX7_Results_${patientName}`
    win.print()
  }, 500)
}

async function loadResults(patientId) {
  const { data } = await resultsApi.getCurrent(patientId)
  results.value = data
}

async function loadSession() {
  loading.value = true
  try {
    const { data } = await sessionsApi.getById(sessionId)
    session.value = data
    if (session.value) {
      await Promise.all([
        loadResults(session.value.patientId),
        notesApi.getBySession(sessionId).then(r => { notes.value = r.data })
      ])
    }
  } finally { loading.value = false }
}

async function saveNote() {
  if (!newNote.value.trim()) return
  await notesApi.create({ sessionId, noteText: newNote.value })
  newNote.value = ''
  const { data } = await notesApi.getBySession(sessionId)
  notes.value = data
}

function startEditNote(note) {
  editingNote.value = note.id
  editNoteText.value = note.noteText
}

async function updateNote(note) {
  await notesApi.update(note.id, editNoteText.value)
  note.noteText = editNoteText.value
  editingNote.value = null
}

async function exportCsv() {
  if (!session.value) return
  const today = new Date().toISOString().split('T')[0]
  const { data } = await exportApi.export({
    patientIds: [session.value.patientId],
    fromDate: '2024-01-01', toDate: today,
    testCodes: null, format: 'csv'
  })
  const url = URL.createObjectURL(new Blob([data]))
  const a = document.createElement('a')
  a.href = url
  a.download = `dx7_${session.value.patientName.replace(/ /g, '_')}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

onMounted(loadSession)
</script>

<style scoped>
/* stat cards */
.stat-card { background: var(--off-white); border: 1.5px solid var(--border); border-radius: 8px; padding: 14px 16px; }
.stat-card.stat-empty { opacity: 0.5; }
.stat-label { font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; color: var(--slate); margin-bottom: 6px; }
.stat-value { font-size: 26px; font-family: 'DM Sans', sans-serif; font-weight: 800; color: var(--navy); line-height: 1; }
.stat-unit { font-size: 13px; font-weight: 400; color: var(--slate); margin-left: 2px; }
.stat-meta { display: flex; align-items: center; gap: 8px; margin-top: 6px; }

/* notes */
.note-item { border: 1.5px solid var(--border); border-radius: 8px; padding: 12px 14px; margin-bottom: 10px; }
.note-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px; }
.note-body { font-size: 13.5px; color: #334155; line-height: 1.5; white-space: pre-wrap; }
.note-actions { margin-top: 8px; display: flex; gap: 6px; }

/* print options checkboxes */
.print-opt {
  display: inline-flex; align-items: center; gap: 6px;
  padding: 6px 14px; border: 1.5px solid var(--border); border-radius: 20px;
  cursor: pointer; font-size: 13px; color: var(--slate);
  transition: all 0.1s; user-select: none;
}
.print-opt input { display: none; }
.print-opt.active { border-color: var(--primary-mid); background: var(--primary-pale); color: var(--primary-mid); font-weight: 600; }

/* print preview */
.print-preview {
  background: white; border: 1px solid var(--border); border-radius: 8px;
  padding: 24px; font-family: 'Inter', Arial, sans-serif; font-size: 12px; color: #111827;
}
.pr-header { display: flex; align-items: center; gap: 16px; border-bottom: 2px solid #1d4ed8; padding-bottom: 12px; margin-bottom: 14px; }
.pr-logo { font-family: 'DM Sans', sans-serif; font-size: 26px; font-weight: 900; color: #1e3a8a; letter-spacing: -1px; }
.pr-clinic { font-size: 13px; font-weight: 700; color: #111827; }
.pr-meta { font-size: 11px; color: #6b7280; margin-top: 2px; }
.pr-patient-bar { display: flex; gap: 20px; background: #eff6ff; border-radius: 6px; padding: 10px 14px; margin-bottom: 14px; flex-wrap: wrap; }
.pr-patient-detail { display: flex; flex-direction: column; }
.pr-label { font-size: 9px; text-transform: uppercase; letter-spacing: 0.5px; color: #6b7280; font-weight: 600; }
.pr-val { font-size: 12px; font-weight: 700; color: #111827; margin-top: 2px; }
.pr-section-title { font-size: 11px; font-weight: 700; color: #1e3a8a; border-bottom: 1px solid #bfdbfe; padding-bottom: 4px; margin: 14px 0 10px; text-transform: uppercase; letter-spacing: 0.5px; }
.pr-priority-grid { display: grid; grid-template-columns: repeat(3,1fr); gap: 10px; }
.pr-priority-cell { border: 1.5px solid #e5e7eb; border-radius: 6px; padding: 10px; text-align: center; }
.pr-test-name { font-size: 9px; font-weight: 600; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }
.pr-val-big { font-size: 24px; font-weight: 800; color: #111827; line-height: 1; }
.pr-unit { font-size: 11px; font-weight: 400; color: #6b7280; }
.pr-flag { font-size: 10px; font-weight: 700; margin-top: 4px; }
.pr-flag-h { color: #dc2626; }
.pr-flag-l { color: #2563eb; }
.pr-ref { font-size: 10px; color: #9ca3af; margin-top: 2px; }
.pr-table { width: 100%; border-collapse: collapse; font-size: 11px; margin-top: 4px; }
.pr-table th { background: #f9fafb; padding: 7px 8px; text-align: left; font-weight: 600; color: #6b7280; border-bottom: 1px solid #e5e7eb; font-size: 10px; text-transform: uppercase; }
.pr-table td { padding: 7px 8px; border-bottom: 1px solid #f3f4f6; color: #1f2937; }
.pr-row-flag td { background: #fff7f7; }
.pr-result { font-weight: 700; }
.pr-code { color: #9ca3af; font-size: 10px; font-family: monospace; }
.pr-note { border: 1px solid #e5e7eb; border-radius: 6px; padding: 10px 12px; margin-bottom: 8px; }
.pr-note-meta { font-size: 10px; color: #6b7280; margin-bottom: 4px; font-weight: 600; }
.pr-note-body { font-size: 12px; color: #374151; line-height: 1.5; white-space: pre-wrap; }
.pr-footer { margin-top: 20px; padding-top: 10px; border-top: 1px solid #e5e7eb; font-size: 10px; color: #9ca3af; text-align: center; }
</style>