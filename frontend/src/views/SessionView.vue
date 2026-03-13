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
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="8" y1="13" x2="16" y2="13"/><line x1="8" y1="17" x2="16" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>
            Export CSV
          </button>
          <button v-if="auth.canPrint" class="btn-pdf" @click="showPrintModal = true; printMode = 'download'">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="12" y1="18" x2="12" y2="12"/><polyline points="9 15 12 18 15 15"/></svg>
            Download PDF
          </button>
          <button v-if="auth.canPrint" class="btn-print" @click="showPrintModal = true; printMode = 'print'">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
            Print
          </button>
          <router-link to="/shifts" class="btn btn-outline btn-sm">← Back</router-link>
        </div>
      </div>
    </div>

    <div class="doctrine-bar">
      Results are displayed as-is from the laboratory source. No interpretation. No risk labels. Data only.
    </div>

    <div v-if="loadError" style="background:#fee2e2; border:1px solid #fca5a5; color:#dc2626; padding:12px 16px; border-radius:8px; margin-bottom:16px; font-size:13px; font-weight:600;">
      ⚠ {{ loadError }}
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
          <div class="table-footer">
            <span>Showing {{ results.length }} result{{ results.length !== 1 ? 's' : '' }}</span>
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
            <div class="modal-title">{{ printMode === 'download' ? '↓ Download PDF' : '🖨️ Print' }}</div>
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
          <button v-if="printMode === 'download'" class="btn btn-primary" @click="downloadPdf" :disabled="pdfLoading">
            <svg width="14" height="14" viewBox="0 0 20 20" fill="currentColor" style="margin-right:5px;vertical-align:-2px"><path fill-rule="evenodd" d="M6 2a2 2 0 00-2 2v12a2 2 0 002 2h8a2 2 0 002-2V7.414A2 2 0 0015.414 6L12 2.586A2 2 0 0010.586 2H6zm5 6a1 1 0 10-2 0v3.586l-1.293-1.293a1 1 0 10-1.414 1.414l3 3a1 1 0 001.414 0l3-3a1 1 0 00-1.414-1.414L11 11.586V8z" clip-rule="evenodd"/></svg>
            {{ pdfLoading ? 'Generating…' : 'Download PDF' }}
          </button>
          <button v-if="printMode === 'print'" class="btn btn-primary" @click="printReport">🖨️ Print</button>
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
const loadError    = ref('')
const newNote      = ref('')
const editingNote  = ref(null)
const editNoteText = ref('')
const showReport = ref(false)
const showPrintModal = ref(false)
const printMode = ref('print') // 'print' | 'download'

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

// ── Shared: build full HTML document for print/PDF ──
function buildPrintHtml(content, title) {
  return `<!DOCTYPE html>
<html>
<head>
  <title>${title}</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: Arial, sans-serif; font-size: 12px; color: #111827; padding: 24px; }
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
    .pr-flag-h, .flag-h { color: #dc2626; }
    .pr-flag-l, .flag-l { color: #2563eb; }
    .pr-ref { font-size: 10px; color: #9ca3af; margin-top: 2px; }
    .pr-table { width: 100%; border-collapse: collapse; font-size: 11px; }
    .pr-table th { background: #f9fafb; padding: 7px 10px; text-align: left; font-weight: 600; color: #6b7280; border-bottom: 1px solid #e5e7eb; font-size: 10px; text-transform: uppercase; letter-spacing: 0.3px; }
    .pr-table td { padding: 7px 10px; border-bottom: 1px solid #f3f4f6; }
    .pr-row-flag { background: white; }
    .pr-result { font-weight: 700; }
    .pr-code { color: #9ca3af; font-size: 10px; }
    .pr-note { border: 1px solid #e5e7eb; border-radius: 6px; padding: 10px 12px; margin-bottom: 8px; }
    .pr-note-meta { font-size: 10px; color: #6b7280; margin-bottom: 4px; font-weight: 600; }
    .pr-note-body { font-size: 12px; color: #374151; line-height: 1.5; white-space: pre-wrap; }
    .pr-footer { margin-top: 24px; padding-top: 10px; border-top: 1px solid #e5e7eb; font-size: 10px; color: #9ca3af; text-align: center; }
    @media print { body { padding: 0; } button { display: none !important; } }
  </style>
</head>
<body>${content}</body>
</html>`
}

// ── Print — uses hidden iframe to avoid popup blocker ──
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

// ── Download PDF — html2canvas + jsPDF (same approach as ResultReportModal) ──
const pdfLoading = ref(false)
async function downloadPdf() {
  if (pdfLoading.value) return
  pdfLoading.value = true
  showPrintModal.value = false

  const body = document.getElementById('print-preview')
  if (!body) { pdfLoading.value = false; return }

  const patientName = (session.value?.patientName || 'report').replace(/[^a-zA-Z0-9]/g, '_')
  const dateStr = session.value?.sessionDate || ''
  const filename = 'DX7_Results_' + patientName + '_' + dateStr + '.pdf'

  const html = buildPrintHtml(body.innerHTML, filename)
  const fullHtml = html.replace('</head>', `
    <script src="https://cdnjs.cloudflare.com/ajax/libs/jspdf/2.5.1/jspdf.umd.min.js"><\/script>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js"><\/script>
  </head>`)

  const iframe = document.createElement('iframe')
  iframe.style.cssText = 'position:fixed;top:-9999px;left:-9999px;width:794px;height:1123px;border:none'
  document.body.appendChild(iframe)

  iframe.contentDocument.open()
  iframe.contentDocument.write(fullHtml)
  iframe.contentDocument.close()

  await new Promise(resolve => setTimeout(resolve, 1500))

  try {
    const { jsPDF } = iframe.contentWindow.jspdf
    const canvas = await iframe.contentWindow.html2canvas(iframe.contentDocument.body, {
      scale: 2, useCORS: true, windowWidth: 794, width: 794
    })
    const imgData = canvas.toDataURL('image/jpeg', 0.95)
    const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' })
    const pageW = pdf.internal.pageSize.getWidth()
    const pageH = pdf.internal.pageSize.getHeight()
    const imgH  = (canvas.height * pageW) / canvas.width

    let yPos = 0, remaining = imgH
    while (remaining > 0) {
      pdf.addImage(imgData, 'JPEG', 0, -yPos, pageW, imgH)
      remaining -= pageH
      yPos += pageH
      if (remaining > 0) pdf.addPage()
    }
    const pdfBlob = pdf.output('blob')

    if (window.showSaveFilePicker) {
      try {
        const fh = await window.showSaveFilePicker({
          suggestedName: filename,
          types: [{ description: 'PDF Document', accept: { 'application/pdf': ['.pdf'] } }]
        })
        const w = await fh.createWritable()
        await w.write(pdfBlob)
        await w.close()
      } catch (e) {
        if (e.name !== 'AbortError') {
          const url = URL.createObjectURL(pdfBlob)
          const a = document.createElement('a')
          a.href = url; a.download = filename; a.click()
          URL.revokeObjectURL(url)
        }
      }
    } else {
      const url = URL.createObjectURL(pdfBlob)
      const a = document.createElement('a')
      a.href = url; a.download = filename; a.click()
      URL.revokeObjectURL(url)
    }
  } catch (e) {
    alert('Failed to generate PDF: ' + e.message)
  } finally {
    document.body.removeChild(iframe)
    pdfLoading.value = false
  }
}

async function loadResults(patientId) {
  const { data } = await resultsApi.getCurrent(patientId)
  results.value = data
}

async function loadSession() {
  loading.value = true
  loadError.value = ''
  try {
    const { data } = await sessionsApi.getById(sessionId)
    session.value = data
    if (session.value) {
      await Promise.all([
        loadResults(session.value.patientId),
        notesApi.getBySession(sessionId).then(r => { notes.value = r.data })
      ])
    }
  } catch (e) {
    loadError.value = `Error ${e.response?.status}: ${e.response?.data?.message || e.response?.data || e.message}`
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
  background: linear-gradient(160deg, #f0f7ff 0%, #ffffff 40%);
  border: 1px solid #bfdbfe;
  border-radius: 12px;
  padding: 28px;
  font-family: 'Inter', Arial, sans-serif;
  font-size: 12px;
  color: #111827;
  box-shadow: 0 4px 24px rgba(37,99,235,0.07), inset 0 1px 0 rgba(255,255,255,0.8);
}
.pr-header {
  display: flex; align-items: center; gap: 16px;
  background: linear-gradient(135deg, #1e3a8a 0%, #1d4ed8 60%, #2563eb 100%);
  border-radius: 10px; padding: 16px 20px; margin-bottom: 16px;
  box-shadow: 0 4px 16px rgba(30,58,138,0.25);
}
.pr-logo { font-family: 'DM Sans', sans-serif; font-size: 28px; font-weight: 900; color: white; letter-spacing: -1px; text-shadow: 0 2px 8px rgba(0,0,0,0.2); }
.pr-clinic { font-size: 13px; font-weight: 700; color: rgba(255,255,255,0.95); }
.pr-meta { font-size: 11px; color: rgba(255,255,255,0.65); margin-top: 2px; }
.pr-patient-bar {
  display: flex; gap: 0; margin-bottom: 16px; flex-wrap: wrap;
  border: 1.5px solid #bfdbfe; border-radius: 10px; overflow: hidden;
  background: white; box-shadow: 0 2px 8px rgba(37,99,235,0.06);
}
.pr-patient-detail {
  display: flex; flex-direction: column; padding: 10px 18px; flex: 1;
  border-right: 1px solid #e0eaff;
}
.pr-patient-detail:last-child { border-right: none; }
.pr-label { font-size: 9px; text-transform: uppercase; letter-spacing: 0.8px; color: #93c5fd; font-weight: 700; }
.pr-val { font-size: 12px; font-weight: 700; color: #1e3a8a; margin-top: 3px; }
.pr-section-title {
  font-size: 10px; font-weight: 800; color: #1d4ed8;
  text-transform: uppercase; letter-spacing: 1px;
  padding: 6px 12px; margin: 14px 0 10px;
  background: linear-gradient(90deg, #eff6ff, transparent);
  border-left: 3px solid #1d4ed8; border-radius: 0 4px 4px 0;
}
.pr-priority-grid { display: grid; grid-template-columns: repeat(3,1fr); gap: 10px; }
.pr-priority-cell {
  border: 1.5px solid #e0eaff; border-radius: 10px; padding: 14px 10px;
  text-align: center; background: white;
  box-shadow: 0 2px 8px rgba(37,99,235,0.06);
  transition: box-shadow .15s;
}
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
.pr-row-flag td { background: white; }
.pr-result { font-weight: 700; }
.pr-code { color: #9ca3af; font-size: 10px; font-family: monospace; }
.pr-note { border: 1px solid #e5e7eb; border-radius: 6px; padding: 10px 12px; margin-bottom: 8px; }
.pr-note-meta { font-size: 10px; color: #6b7280; margin-bottom: 4px; font-weight: 600; }
.pr-note-body { font-size: 12px; color: #374151; line-height: 1.5; white-space: pre-wrap; }
.pr-footer { margin-top: 20px; padding-top: 10px; border-top: 1px solid #e5e7eb; font-size: 10px; color: #9ca3af; text-align: center; }
</style>