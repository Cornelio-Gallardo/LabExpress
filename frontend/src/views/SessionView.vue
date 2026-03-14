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
          <button v-if="auth.canExport" class="btn-excel" @click="exportCsv">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="8" y1="13" x2="16" y2="13"/><line x1="8" y1="17" x2="16" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>
            Export CSV
          </button>
          <button v-if="auth.canPrint" class="btn-pdf" @click="showPrintModal = true; printMode = 'download'">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="12" y1="18" x2="12" y2="12"/><polyline points="9 15 12 18 15 15"/></svg>
            Download PDF
          </button>
          <button v-if="auth.canPrint" class="btn-print" @click="showPrintModal = true; printMode = 'print'">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
            Print
          </button>
          <router-link to="/dashboard" class="btn btn-outline btn-sm">← Back</router-link>
        </div>
      </div>
    </div>

    <div class="doctrine-bar">
      Results are displayed as-is from the laboratory source. No interpretation. No risk labels. Data only.
    </div>

    <div v-if="loadError" class="error-bar">⚠ {{ loadError }}</div>
    <div v-if="loading" class="loading">Loading results…</div>

    <div v-else style="display:grid; grid-template-columns:1fr 340px; gap:20px; align-items:start">

      <!-- LEFT: Unified Lab Results table (from CDM chain) -->
      <div class="table-card">
        <div class="card-header">
          <div>
            <div class="card-title">Lab Results</div>
            <div class="text-slate text-sm">
              {{ analyteRows.length }} analytes · {{ resultDates.length }} result date{{ resultDates.length !== 1 ? 's' : '' }}
            </div>
          </div>
          <div style="display:flex; align-items:center; gap:10px">
            <label class="compare-label">Show last</label>
            <select v-model="showCount" class="compare-select">
              <option :value="1">1 date</option>
              <option :value="2">2 dates</option>
              <option :value="3">3 dates</option>
              <option :value="5">5 dates</option>
            </select>
          </div>
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th style="min-width:170px">Analyte</th>
                <th style="min-width:55px">Unit</th>
                <th style="min-width:90px">Ref Range</th>
                <th v-for="(col, i) in visibleDates" :key="col.date" class="date-col-header" :class="{ 'col-latest': i === 0 }">
                  <div>{{ col.date }}</div>
                  <div class="accession-sub" :title="`Accession: ${col.accession}`">{{ col.accession }}</div>
                  <div v-if="i === 0" class="latest-badge">latest</div>
                </th>
              </tr>
            </thead>
            <tbody>
              <!-- Group by test panel -->
              <template v-for="group in groupedRows" :key="group.label">
                <tr class="group-row">
                  <td :colspan="3 + visibleDates.length">{{ group.label }}</td>
                </tr>
                <tr v-for="row in group.analytes" :key="row.analyteCode">
                  <td>
                    <div style="font-weight:500">{{ row.displayName }}</div>
                    <div class="text-slate" style="font-size:10px; font-family:monospace">{{ row.analyteCode }}</div>
                  </td>
                  <td class="text-slate text-sm">{{ row.unit || '—' }}</td>
                  <td class="text-slate text-sm">{{ row.refRange || '—' }}</td>
                  <td v-for="(col, i) in visibleDates" :key="col.date" :class="{ 'col-latest': i === 0 }">
                    <template v-if="row.byDate[col.date]">
                      <span class="result-val" :class="flagClass(row.byDate[col.date].flag)">
                        {{ row.byDate[col.date].value }}
                      </span>
                      <span v-if="row.byDate[col.date].flag" class="flag-badge" :class="row.byDate[col.date].flag === 'H' ? 'badge-h' : row.byDate[col.date].flag === 'L' ? 'badge-l' : 'badge-n'">
                        {{ row.byDate[col.date].flag === 'N' ? 'Normal' : row.byDate[col.date].flag }}
                      </span>
                    </template>
                    <span v-else class="text-slate">—</span>
                  </td>
                </tr>
              </template>
              <tr v-if="analyteRows.length === 0">
                <td colspan="20" style="text-align:center; padding:40px; color:var(--slate)">
                  No lab results found for this patient.
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="table-footer">
          <span>{{ analyteRows.length }} analyte{{ analyteRows.length !== 1 ? 's' : '' }}</span>
          <span v-if="resultDates.length" style="color:var(--slate)">
            · Orders: {{ orders.length }}
          </span>
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

    <!-- PRINT / PDF MODAL -->
    <div v-if="showPrintModal" class="modal-backdrop" @click.self="showPrintModal = false">
      <div class="modal" style="width:680px; max-height:90vh">
        <div class="modal-header">
          <div>
            <div class="modal-title">{{ printMode === 'download' ? '↓ Download PDF' : '🖨️ Print' }}</div>
            <div class="text-slate text-sm" style="margin-top:2px">{{ session?.patientName }}</div>
          </div>
          <button class="modal-close" @click="showPrintModal = false">✕</button>
        </div>
        <div class="modal-body" style="padding-bottom:0">
          <div style="display:flex; gap:10px; margin-bottom:18px; flex-wrap:wrap">
            <label class="print-opt" :class="{ active: printOpts.allResults }">
              <input type="checkbox" v-model="printOpts.allResults" /> All Results
            </label>
            <label class="print-opt" :class="{ active: printOpts.notes }">
              <input type="checkbox" v-model="printOpts.notes" /> MD Notes
            </label>
          </div>
        </div>
        <div class="modal-body" style="padding-top:0; overflow-y:auto; max-height:55vh">
          <div id="print-preview" class="print-preview">
            <div class="pr-header">
              <div class="pr-logo">DX7</div>
              <div>
                <div class="pr-clinic">{{ auth.tenant?.name }} — {{ auth.client?.name }}</div>
                <div class="pr-meta">Lab Results Report · Printed {{ printDate }}</div>
              </div>
            </div>
            <div class="pr-patient-bar">
              <div class="pr-patient-detail"><span class="pr-label">Patient</span><span class="pr-val">{{ session?.patientName }}</span></div>
              <div class="pr-patient-detail"><span class="pr-label">Session Date</span><span class="pr-val">{{ session?.sessionDate }}</span></div>
              <div class="pr-patient-detail"><span class="pr-label">Shift</span><span class="pr-val">{{ session?.shiftNumber }}</span></div>
              <div class="pr-patient-detail" v-if="session?.chair"><span class="pr-label">Chair</span><span class="pr-val">{{ session.chair }}</span></div>
              <div class="pr-patient-detail"><span class="pr-label">Printed by</span><span class="pr-val">{{ auth.user?.name }}</span></div>
            </div>
            <div v-if="printOpts.allResults && analyteRows.length">
              <div class="pr-section-title">Lab Results</div>
              <table class="pr-table">
                <thead>
                  <tr>
                    <th>Analyte</th>
                    <th>Unit</th>
                    <th>Ref Range</th>
                    <th v-for="col in visibleDates" :key="col.date">{{ col.date }}<br><small>{{ col.accession }}</small></th>
                  </tr>
                </thead>
                <tbody>
                  <template v-for="group in groupedRows" :key="group.label">
                    <tr class="pr-group-row"><td :colspan="3 + visibleDates.length">{{ group.label }}</td></tr>
                    <tr v-for="row in group.analytes" :key="row.analyteCode">
                      <td>{{ row.displayName }}</td>
                      <td>{{ row.unit || '—' }}</td>
                      <td>{{ row.refRange || '—' }}</td>
                      <td v-for="col in visibleDates" :key="col.date" class="pr-result" :class="row.byDate[col.date]?.flag === 'H' ? 'pr-flag-h' : row.byDate[col.date]?.flag === 'L' ? 'pr-flag-l' : row.byDate[col.date]?.flag === 'N' ? 'pr-flag-n' : ''">
                        {{ row.byDate[col.date]?.value || '—' }}
                        <span v-if="row.byDate[col.date]?.flag"> {{ row.byDate[col.date].flag }}</span>
                      </td>
                    </tr>
                  </template>
                </tbody>
              </table>
            </div>
            <div v-if="printOpts.notes && notes.length">
              <div class="pr-section-title">MD Notes</div>
              <div v-for="note in notes" :key="note.id" class="pr-note">
                <div class="pr-note-meta">{{ note.mdName }} · {{ formatDate(note.createdAt) }}</div>
                <div class="pr-note-body">{{ note.noteText }}</div>
              </div>
            </div>
            <div class="pr-footer">
              Generated by Dx7 Clinical Information System · {{ auth.tenant?.name }}. For clinical use only.
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-outline" @click="showPrintModal = false">Cancel</button>
          <button v-if="printMode === 'download'" class="btn btn-primary" @click="downloadPdf" :disabled="pdfLoading">
            {{ pdfLoading ? 'Generating…' : 'Download PDF' }}
          </button>
          <button v-if="printMode === 'print'" class="btn btn-primary" @click="printReport">🖨️ Print</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, reactive } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '../store/auth'
import { sessionsApi, resultsApi, notesApi, exportApi } from '../services/api'

const auth      = useAuthStore()
const route     = useRoute()
const sessionId = route.params.sessionId

const session   = ref(null)
const orders    = ref([])   // raw CDM orders
const notes     = ref([])
const loading   = ref(false)
const loadError = ref('')
const showCount = ref(3)

const newNote      = ref('')
const editingNote  = ref(null)
const editNoteText = ref('')
const showPrintModal = ref(false)
const printMode    = ref('print')
const pdfLoading   = ref(false)
const printOpts    = reactive({ allResults: true, notes: true })
const printDate    = new Date().toLocaleString('en-PH', { dateStyle: 'long', timeStyle: 'short' })

// ── Derived: all distinct result dates across all orders, newest first ────────
const resultDates = computed(() => {
  const seen = new Set()
  const dates = []
  for (const order of orders.value) {
    for (const header of order.headers ?? []) {
      if (!header.resultDatetime) continue
      const d = header.resultDatetime.slice(0, 10)
      if (!seen.has(d)) { seen.add(d); dates.push({ date: d, accession: order.accessionNumber, releasedAt: order.releasedAt }) }
    }
  }
  return dates.sort((a, b) => b.date.localeCompare(a.date))
})

const visibleDates = computed(() => resultDates.value.slice(0, showCount.value))

// ── Derived: analyte rows keyed by SXA analyte code ─────────────────────────
const analyteRows = computed(() => {
  const map = {}
  for (const order of orders.value) {
    for (const header of order.headers ?? []) {
      if (!header.resultDatetime) continue
      const dateKey = header.resultDatetime.slice(0, 10)
      const panel   = header.sxaTestId ?? 'OTHER'
      for (const val of header.values ?? []) {
        const key = val.analyteCode ?? val.id
        if (!map[key]) {
          map[key] = {
            analyteCode: val.analyteCode,
            displayName: val.analyteDisplayName ?? val.analyteCode ?? '—',
            unit:        val.unit,
            refRange:    val.referenceRangeLow != null && val.referenceRangeHigh != null
                           ? `${val.referenceRangeLow}–${val.referenceRangeHigh}`
                           : val.referenceRangeRaw,
            panel,
            byDate: {}
          }
        }
        map[key].byDate[dateKey] = { value: val.displayValue, flag: val.abnormalFlag }
      }
    }
  }
  return Object.values(map)
})

// Group by panel (CBC, CHEM, etc.) based on SXA test id
const groupedRows = computed(() => {
  const groups = {}
  for (const row of analyteRows.value) {
    const label = panelLabel(row.panel)
    if (!groups[label]) groups[label] = { label, analytes: [] }
    groups[label].analytes.push(row)
  }
  // Sort analytes within each group
  for (const g of Object.values(groups))
    g.analytes.sort((a, b) => (a.displayName ?? '').localeCompare(b.displayName ?? ''))
  return Object.values(groups).sort((a, b) => a.label.localeCompare(b.label))
})

function panelLabel(sxaTestId) {
  if (!sxaTestId) return 'Other'
  if (sxaTestId.includes('CBC')) return 'CBC — Complete Blood Count'
  if (sxaTestId.includes('BUN')) return 'BUN — Blood Urea Nitrogen'
  if (sxaTestId.includes('FBS') || sxaTestId.includes('GLU')) return 'Glucose'
  if (sxaTestId.includes('K'))  return 'Electrolytes'
  return sxaTestId
}

function flagClass(flag) {
  if (flag === 'H') return 'flag-h'
  if (flag === 'L') return 'flag-l'
  if (flag === 'N') return 'flag-n'
  return ''
}

function formatDate(dt) {
  if (!dt) return '—'
  return new Date(dt).toLocaleString('en-PH', { dateStyle: 'medium', timeStyle: 'short' })
}

// ── Load ──────────────────────────────────────────────────────────────────────
async function loadSession() {
  loading.value = true
  loadError.value = ''
  try {
    const { data } = await sessionsApi.getById(sessionId)
    session.value = data
    if (session.value) {
      await Promise.all([
        resultsApi.getOrders(session.value.patientId).then(r => { orders.value = r.data }),
        notesApi.getBySession(sessionId).then(r => { notes.value = r.data })
      ])
    }
  } catch (e) {
    loadError.value = `Error ${e.response?.status}: ${e.response?.data?.message || e.message}`
  } finally { loading.value = false }
}

// ── Notes ─────────────────────────────────────────────────────────────────────
async function saveNote() {
  if (!newNote.value.trim()) return
  await notesApi.create({ sessionId, noteText: newNote.value })
  newNote.value = ''
  const { data } = await notesApi.getBySession(sessionId)
  notes.value = data
}
function startEditNote(note) { editingNote.value = note.id; editNoteText.value = note.noteText }
async function updateNote(note) {
  await notesApi.update(note.id, editNoteText.value)
  note.noteText = editNoteText.value
  editingNote.value = null
}

// ── Export CSV ────────────────────────────────────────────────────────────────
async function exportCsv() {
  if (!session.value) return
  const todayStr = new Date().toISOString().split('T')[0]
  const { data } = await exportApi.export({
    patientIds: [session.value.patientId],
    fromDate: '2024-01-01', toDate: todayStr,
    testCodes: null, format: 'csv'
  })
  const url = URL.createObjectURL(new Blob([data]))
  const a = document.createElement('a')
  a.href = url
  a.download = `dx7_${session.value.patientName.replace(/ /g, '_')}.csv`
  a.click(); URL.revokeObjectURL(url)
}

// ── Print / PDF ───────────────────────────────────────────────────────────────
function buildPrintHtml(content, title) {
  return `<!DOCTYPE html><html><head><title>${title}</title><style>
    *{box-sizing:border-box;margin:0;padding:0}body{font-family:Arial,sans-serif;font-size:11px;color:#111827;padding:20px}
    .pr-header{display:flex;align-items:center;gap:16px;background:linear-gradient(135deg,#1e3a8a,#2563eb);border-radius:8px;padding:14px 18px;margin-bottom:14px}
    .pr-logo{font-size:26px;font-weight:900;color:white;letter-spacing:-1px}
    .pr-clinic{font-size:13px;font-weight:700;color:rgba(255,255,255,0.95)}.pr-meta{font-size:10px;color:rgba(255,255,255,0.6);margin-top:2px}
    .pr-patient-bar{display:flex;gap:0;margin-bottom:14px;border:1.5px solid #bfdbfe;border-radius:8px;overflow:hidden;background:white}
    .pr-patient-detail{display:flex;flex-direction:column;padding:8px 14px;flex:1;border-right:1px solid #e0eaff}
    .pr-patient-detail:last-child{border-right:none}
    .pr-label{font-size:8px;text-transform:uppercase;letter-spacing:0.8px;color:#93c5fd;font-weight:700}
    .pr-val{font-size:11px;font-weight:700;color:#1e3a8a;margin-top:2px}
    .pr-section-title{font-size:9px;font-weight:800;color:#1d4ed8;text-transform:uppercase;letter-spacing:1px;padding:5px 10px;margin:12px 0 8px;background:linear-gradient(90deg,#eff6ff,transparent);border-left:3px solid #1d4ed8}
    .pr-table{width:100%;border-collapse:collapse;font-size:10px}
    .pr-table th{background:#f9fafb;padding:5px 7px;text-align:left;font-weight:600;color:#6b7280;border-bottom:1px solid #e5e7eb;font-size:9px;text-transform:uppercase}
    .pr-table td{padding:5px 7px;border-bottom:1px solid #f3f4f6}
    .pr-group-row td{background:#eff6ff;font-weight:700;font-size:9px;color:#1d4ed8;text-transform:uppercase;letter-spacing:0.5px;padding:4px 7px}
    .pr-result{font-weight:700}.pr-flag-h{color:#dc2626}.pr-flag-l{color:#2563eb}.pr-flag-n{color:#16a34a}
    .pr-note{border:1px solid #e5e7eb;border-radius:5px;padding:8px 10px;margin-bottom:6px}
    .pr-note-meta{font-size:9px;color:#6b7280;margin-bottom:3px;font-weight:600}
    .pr-note-body{font-size:10px;color:#374151;line-height:1.5;white-space:pre-wrap}
    .pr-footer{margin-top:16px;padding-top:8px;border-top:1px solid #e5e7eb;font-size:9px;color:#9ca3af;text-align:center}
  </style></head><body>${content}</body></html>`
}

function printReport() {
  const content = document.getElementById('print-preview').innerHTML
  const html = buildPrintHtml(content, 'Lab Results — ' + (session.value?.patientName || ''))
  let iframe = document.getElementById('dx7-print-frame')
  if (!iframe) {
    iframe = document.createElement('iframe')
    iframe.id = 'dx7-print-frame'
    iframe.style.cssText = 'position:fixed;top:-9999px;left:-9999px;width:900px;height:600px;border:none;'
    document.body.appendChild(iframe)
  }
  iframe.srcdoc = html
  iframe.onload = () => { iframe.contentWindow.focus(); iframe.contentWindow.print() }
}

async function downloadPdf() {
  if (pdfLoading.value) return
  pdfLoading.value = true; showPrintModal.value = false
  const body = document.getElementById('print-preview')
  if (!body) { pdfLoading.value = false; return }
  const name    = (session.value?.patientName || 'report').replace(/[^a-zA-Z0-9]/g, '_')
  const filename = `DX7_Results_${name}_${session.value?.sessionDate || ''}.pdf`
  const fullHtml = buildPrintHtml(body.innerHTML, filename).replace('</head>',
    `<script src="https://cdnjs.cloudflare.com/ajax/libs/jspdf/2.5.1/jspdf.umd.min.js"><\/script>
     <script src="https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js"><\/script></head>`)
  const iframe = document.createElement('iframe')
  iframe.style.cssText = 'position:fixed;top:-9999px;left:-9999px;width:794px;height:1123px;border:none'
  document.body.appendChild(iframe)
  iframe.contentDocument.open(); iframe.contentDocument.write(fullHtml); iframe.contentDocument.close()
  await new Promise(r => setTimeout(r, 1500))
  try {
    const { jsPDF } = iframe.contentWindow.jspdf
    const canvas = await iframe.contentWindow.html2canvas(iframe.contentDocument.body, { scale: 2, useCORS: true, windowWidth: 794, width: 794 })
    const imgData = canvas.toDataURL('image/jpeg', 0.95)
    const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' })
    const pageW = pdf.internal.pageSize.getWidth(), pageH = pdf.internal.pageSize.getHeight()
    const imgH = (canvas.height * pageW) / canvas.width
    let yPos = 0, remaining = imgH
    while (remaining > 0) {
      pdf.addImage(imgData, 'JPEG', 0, -yPos, pageW, imgH)
      remaining -= pageH; yPos += pageH
      if (remaining > 0) pdf.addPage()
    }
    const blob = pdf.output('blob'), url = URL.createObjectURL(blob)
    const a = document.createElement('a'); a.href = url; a.download = filename; a.click()
    URL.revokeObjectURL(url)
  } catch (e) { alert('Failed to generate PDF: ' + e.message) }
  finally { document.body.removeChild(iframe); pdfLoading.value = false }
}

onMounted(loadSession)
</script>

<style scoped>
/* result table */
.compare-label  { font-size:12px; font-weight:600; color:var(--slate); white-space:nowrap; }
.compare-select { height:32px; padding:0 10px; border:1.5px solid var(--border); border-radius:6px; font-size:13px; color:var(--navy); cursor:pointer; }
.date-col-header { text-align:center; font-size:12px; font-weight:600; color:var(--slate); min-width:110px; }
.date-col-header.col-latest { background:#f0f9ff; color:var(--primary-mid); }
td.col-latest   { background:#f8fbff; }
.accession-sub  { font-size:10px; font-family:monospace; font-weight:400; color:var(--slate); opacity:0.75; margin-top:1px; }
.latest-badge   { display:inline-block; font-size:10px; font-weight:700; background:var(--primary-mid); color:white; border-radius:4px; padding:1px 5px; margin-top:2px; text-transform:uppercase; }
.group-row td   { background:#eff6ff; font-size:10px; font-weight:700; color:#1d4ed8; text-transform:uppercase; letter-spacing:0.5px; padding:5px 8px; }
.result-val     { font-weight:700; font-size:14px; color:var(--navy); }
.result-val.flag-h { color:#dc2626; }
.result-val.flag-l { color:#2563eb; }
.result-val.flag-n { color:#16a34a; }
.flag-badge     { font-size:10px; font-weight:700; margin-left:3px; }
.badge-h        { color:#dc2626; }
.badge-l        { color:#2563eb; }
.badge-n        { color:#16a34a; }
.error-bar      { background:#fee2e2; border:1px solid #fca5a5; color:#dc2626; padding:12px 16px; border-radius:8px; margin-bottom:16px; font-size:13px; font-weight:600; }

/* notes */
.note-item    { border:1.5px solid var(--border); border-radius:8px; padding:12px 14px; margin-bottom:10px; }
.note-header  { display:flex; justify-content:space-between; align-items:center; margin-bottom:6px; }
.note-body    { font-size:13.5px; color:#334155; line-height:1.5; white-space:pre-wrap; }
.note-actions { margin-top:8px; display:flex; gap:6px; }

/* print opts */
.print-opt { display:inline-flex; align-items:center; gap:6px; padding:6px 14px; border:1.5px solid var(--border); border-radius:20px; cursor:pointer; font-size:13px; color:var(--slate); user-select:none; }
.print-opt input { display:none; }
.print-opt.active { border-color:var(--primary-mid); background:var(--primary-pale); color:var(--primary-mid); font-weight:600; }

/* print preview */
.print-preview { background:linear-gradient(160deg,#f0f7ff 0%,#fff 40%); border:1px solid #bfdbfe; border-radius:12px; padding:24px; font-family:'Inter',Arial,sans-serif; font-size:11px; color:#111827; }
.pr-header { display:flex; align-items:center; gap:16px; background:linear-gradient(135deg,#1e3a8a,#2563eb); border-radius:10px; padding:14px 18px; margin-bottom:14px; }
.pr-logo { font-size:26px; font-weight:900; color:white; letter-spacing:-1px; }
.pr-clinic { font-size:13px; font-weight:700; color:rgba(255,255,255,.95); }
.pr-meta { font-size:10px; color:rgba(255,255,255,.6); margin-top:2px; }
.pr-patient-bar { display:flex; margin-bottom:14px; border:1.5px solid #bfdbfe; border-radius:8px; overflow:hidden; background:white; }
.pr-patient-detail { display:flex; flex-direction:column; padding:8px 14px; flex:1; border-right:1px solid #e0eaff; }
.pr-patient-detail:last-child { border-right:none; }
.pr-label { font-size:8px; text-transform:uppercase; letter-spacing:.8px; color:#93c5fd; font-weight:700; }
.pr-val { font-size:11px; font-weight:700; color:#1e3a8a; margin-top:2px; }
.pr-section-title { font-size:9px; font-weight:800; color:#1d4ed8; text-transform:uppercase; letter-spacing:1px; padding:5px 10px; margin:12px 0 8px; background:linear-gradient(90deg,#eff6ff,transparent); border-left:3px solid #1d4ed8; }
.pr-table { width:100%; border-collapse:collapse; font-size:10px; }
.pr-table th { background:#f9fafb; padding:5px 7px; text-align:left; font-weight:600; color:#6b7280; border-bottom:1px solid #e5e7eb; font-size:9px; text-transform:uppercase; }
.pr-table td { padding:5px 7px; border-bottom:1px solid #f3f4f6; }
.pr-group-row td { background:#eff6ff; font-weight:700; font-size:9px; color:#1d4ed8; text-transform:uppercase; padding:4px 7px; }
.pr-result { font-weight:700; }
.pr-flag-h { color:#dc2626; }
.pr-flag-l { color:#2563eb; }
.pr-flag-n { color:#16a34a; }
.pr-note { border:1px solid #e5e7eb; border-radius:5px; padding:8px 10px; margin-bottom:6px; }
.pr-note-meta { font-size:9px; color:#6b7280; margin-bottom:3px; font-weight:600; }
.pr-note-body { font-size:10px; color:#374151; line-height:1.5; white-space:pre-wrap; }
.pr-footer { margin-top:16px; padding-top:8px; border-top:1px solid #e5e7eb; font-size:9px; color:#9ca3af; text-align:center; }
</style>