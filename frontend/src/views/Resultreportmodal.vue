<template>
  <Teleport to="body">
    <div class="modal-backdrop" @click.self="$emit('close')">
      <div class="report-modal">

        <!-- Header -->
        <div class="report-modal-header">
          <div>
            <div class="report-modal-title">Lab Results Report</div>
            <div class="report-modal-sub">{{ patientName }}</div>
          </div>
          <div class="flex gap-2">
            <button v-if="!loading" class="btn-pdf" @click="downloadPdf" :disabled="downloading">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="12" y1="18" x2="12" y2="12"/><polyline points="9 15 12 18 15 15"/></svg>
              {{ downloading ? 'Generating…' : 'Download PDF' }}
            </button>
            <button v-if="!loading" class="btn-print" @click="printReport">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
              Print
            </button>
            <button class="modal-close" @click="$emit('close')">✕</button>
          </div>
        </div>

        <!-- Body -->
        <div class="report-body" id="result-report-print">

          <div v-if="loading" class="loading" style="padding:80px">
            <div style="font-size:32px;margin-bottom:12px">⏳</div>
            Loading results…
          </div>

          <div v-else>

            <div class="rpt-header">
              <div class="rpt-logo"><span class="dx7-d">D</span><span class="dx7-x">X</span><span class="dx7-seven">7</span></div>
              <div class="rpt-clinic">
                <div class="rpt-clinic-name">{{ clinicName }}</div>
                <div class="rpt-clinic-sub">{{ tenantName }} · Laboratory Results Report</div>
              </div>
              <div class="rpt-meta">
                <div><span>Printed:</span> {{ now }}</div>
                <div><span>By:</span> {{ printedBy }}</div>
              </div>
            </div>

            <hr class="rpt-divider" />

            <div class="rpt-patient-bar">
              <div class="rpt-patient-field">
                <div class="rpt-field-label">Patient</div>
                <div class="rpt-field-value">{{ patientName }}</div>
              </div>
              <div v-if="philhealthNo" class="rpt-patient-field">
                <div class="rpt-field-label">PhilHealth No.</div>
                <div class="rpt-field-value font-mono">{{ philhealthNo }}</div>
              </div>
              <div v-if="birthdate" class="rpt-patient-field">
                <div class="rpt-field-label">Date of Birth</div>
                <div class="rpt-field-value">{{ birthdate }}</div>
              </div>
              <div v-if="gender" class="rpt-patient-field">
                <div class="rpt-field-label">Gender</div>
                <div class="rpt-field-value">{{ gender === 'M' ? 'Male' : gender === 'F' ? 'Female' : gender }}</div>
              </div>
              <div class="rpt-patient-field">
                <div class="rpt-field-label">Report Date</div>
                <div class="rpt-field-value">{{ reportDate }}</div>
              </div>
            </div>

            <hr class="rpt-divider" />


            <div v-if="dateGroups.length === 0" class="empty-state" style="padding:40px">
              <div class="empty-icon">🧪</div>
              <div class="empty-title">No results found</div>
            </div>

            <div v-else>
              <!-- Date tabs -->
              <div class="date-tab-bar">
                <button
                  v-for="dg in dateGroups" :key="dg.date"
                  class="date-tab-btn" :class="{ active: selectedDate === dg.date }"
                  @click="selectedDate = dg.date"
                >
                  {{ dg.date }}
                  <span class="date-tab-count">{{ dg.panels.reduce((s,p) => s + p.results.length, 0) }}</span>
                </button>
              </div>

              <!-- Active date panels -->
              <div v-if="activeDateGroup">
                <div v-for="panel in activeDateGroup.panels" :key="panel.name" class="rpt-panel">
                  <div class="rpt-panel-title">{{ panel.name }}</div>
                  <table class="rpt-table">
                    <thead>
                      <tr>
                        <th>Test Name</th>
                        <th>Result</th>
                        <th>Unit</th>
                        <th>Flag</th>
                        <th>Reference Range</th>
                        <th>Source</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="r in panel.results" :key="r.id" :class="{ 'rpt-row-flag': r.abnormalFlag && r.abnormalFlag !== 'N' }">
                        <td>
                          <div class="rpt-test-name">{{ r.testName }}</div>
                          <div class="rpt-test-code">{{ r.testCode }}</div>
                        </td>
                        <td>
                          <span class="rpt-value" :class="r.abnormalFlag === 'H' ? 'rpt-h' : r.abnormalFlag === 'L' ? 'rpt-l' : r.abnormalFlag === 'N' ? 'rpt-n' : ''">
                            {{ r.resultValue || (r.resultStatus === 'pending' ? 'Pending' : '—') }}
                          </span>
                        </td>
                        <td class="rpt-unit">{{ r.resultUnit || '—' }}</td>
                        <td>
                          <span v-if="r.abnormalFlag" class="rpt-flag" :class="r.abnormalFlag === 'H' ? 'rpt-flag-h' : r.abnormalFlag === 'L' ? 'rpt-flag-l' : 'rpt-flag-n'">{{ r.abnormalFlag === 'N' ? 'Normal' : r.abnormalFlag }}</span>
                          <span v-else class="text-slate">—</span>
                        </td>
                        <td class="rpt-ref">{{ r.referenceRange || '—' }}</td>
                        <td class="rpt-source">{{ r.sourceLab || '—' }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>

            <div class="rpt-footer">
              <div>Results from Dx7 Clinical Information System — presented as-is from the laboratory source.</div>
              <div style="margin-top:4px">No interpretation or risk labeling applied. Clinical judgment of the attending physician applies.</div>
              <div style="margin-top:8px;font-weight:600">— End of Report —</div>
            </div>

          </div>

        </div>

      </div>
    </div>
  </Teleport>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useAuthStore } from '../store/auth'

const props = defineProps({
  patientName:  { type: String,  default: '' },
  philhealthNo: { type: String,  default: '' },
  birthdate:    { type: String,  default: '' },
  gender:       { type: String,  default: '' },
  results:      { type: Array,   default: () => [] },
  reportDate:   { type: String,  default: '' },
  loading:      { type: Boolean, default: false },
})
defineEmits(['close'])

const auth       = useAuthStore()
const selectedDate = ref('')
const downloading = ref(false)
const now        = new Date().toLocaleString('en-PH', { dateStyle: 'long', timeStyle: 'short' })
const printedBy  = auth.user?.name    || ''
const clinicName = auth.client?.name  || 'Dialysis Center'
const tenantName = auth.tenant?.name  || 'LABExpress'

function panelLabel(code) {
  if (!code) return 'Other'
  const c = code.toUpperCase()
  // SXA analyte codes (CDM chain)
  if (c === 'SXA_A_HGB' || c === 'SXA_A_HCT' || c === 'SXA_A_WBC' || c === 'SXA_A_PLT') return 'CBC'
  if (c.startsWith('SXA_A_BUN')) return 'Chemistry'
  if (c === 'SXA_A_CREAT' || c === 'SXA_A_NA' || c === 'SXA_A_K' || c === 'SXA_A_CA' ||
      c === 'SXA_A_PHOS' || c === 'SXA_A_ALB' || c === 'SXA_A_GLU') return 'Chemistry'
  // Legacy flat table codes (CBC-*, CHEM-*)
  const prefix = c.split('-')[0]
  return { CBC: 'CBC', CHEM: 'Chemistry', URINE: 'Urinalysis', UA: 'Urinalysis' }[prefix] || prefix || 'Other'
}

// Group by date (newest first), then by panel within each date
const dateGroups = computed(() => {
  const byDate = {}
  for (const r of props.results) {
    const date = r.resultDate || 'Unknown Date'
    if (!byDate[date]) byDate[date] = {}
    const panel = panelLabel(r.testCode)
    if (!byDate[date][panel]) byDate[date][panel] = []
    byDate[date][panel].push(r)
  }
  const panelOrder = ['CBC', 'Chemistry', 'Urinalysis']
  return Object.entries(byDate)
    .sort(([a], [b]) => b.localeCompare(a)) // newest date first
    .map(([date, panels]) => ({
      date,
      panels: Object.entries(panels)
        .sort(([a], [b]) => {
          const ai = panelOrder.includes(a) ? panelOrder.indexOf(a) : 99
          const bi = panelOrder.includes(b) ? panelOrder.indexOf(b) : 99
          return ai - bi
        })
        .map(([name, results]) => ({
          name,
          results: results.sort((a, b) => (a.testName || '').localeCompare(b.testName || ''))
        }))
    }))
})

// Auto-select newest date when results load
watch(() => props.results, () => {
  if (dateGroups.value.length > 0) selectedDate.value = dateGroups.value[0].date
}, { immediate: true })

// Current date group
const activeDateGroup = computed(() =>
  dateGroups.value.find(dg => dg.date === selectedDate.value) || dateGroups.value[0] || null
)

// Keep flat panels for backward compat (print uses all results)
const panels = computed(() => dateGroups.value.flatMap(dg => dg.panels))

const printStyles = `
  *{box-sizing:border-box;margin:0;padding:0}
  body{font-family:'Inter',sans-serif;font-size:11px;color:#111827;background:white;padding:24px;max-width:800px;margin:0 auto}
  .rpt-header{display:flex;align-items:flex-start;gap:20px;margin-bottom:12px}
  .rpt-logo{font-family:'DM Sans',sans-serif;font-size:26px;font-weight:800;letter-spacing:-1px}
  .dx7-d{color:#1e3a8a}.dx7-x{color:#1d4ed8}.dx7-seven{color:#1e3a8a}
  .rpt-clinic{flex:1;padding-top:4px}
  .rpt-clinic-name{font-size:15px;font-weight:700;color:#111827}
  .rpt-clinic-sub{font-size:11px;color:#6b7280;margin-top:2px}
  .rpt-meta{text-align:right;font-size:10px;color:#6b7280;line-height:1.7}
  .rpt-meta span{font-weight:600;color:#374151}
  .rpt-divider{border:none;border-top:1.5px solid #e5e7eb;margin:10px 0}
  .rpt-patient-bar{display:flex;gap:24px;flex-wrap:wrap;padding:8px 0}
  .rpt-patient-field{min-width:110px}
  .rpt-field-label{font-size:9px;text-transform:uppercase;letter-spacing:0.5px;color:#9ca3af;font-weight:600}
  .rpt-field-value{font-size:12px;font-weight:600;color:#111827;margin-top:2px}
  .font-mono{font-family:'Courier New',monospace}
  .rpt-panel{margin-bottom:18px}
  .rpt-panel-title{font-size:11px;font-weight:700;color:#1e3a8a;text-transform:uppercase;letter-spacing:0.5px;margin-bottom:6px;padding-bottom:4px;border-bottom:1px solid #dbeafe}
  table{width:100%;border-collapse:collapse;font-size:11px}
  thead tr{background:#f9fafb}
  th{padding:6px 10px;font-size:10px;font-weight:600;color:#6b7280;text-align:left;border-bottom:1px solid #e5e7eb;white-space:nowrap}
  td{padding:6px 10px;border-bottom:1px solid #f3f4f6;vertical-align:middle}
  tr:last-child td{border-bottom:none}
  .rpt-row-flag{background:#fff7f7}
  .rpt-test-name{font-weight:500;color:#1f2937}
  .rpt-test-code{font-size:9px;color:#9ca3af;margin-top:1px}
  .rpt-value{font-weight:600;font-size:12px}
  .rpt-h{color:#dc2626}.rpt-l{color:#2563eb}.rpt-n{color:#16a34a}
  .rpt-flag{display:inline-block;padding:1px 6px;border-radius:10px;font-size:10px;font-weight:700}
  .rpt-flag-h{background:#fee2e2;color:#dc2626}
  .rpt-flag-l{background:#dbeafe;color:#1d4ed8}
  .rpt-flag-n{background:#dcfce7;color:#16a34a}
  .rpt-unit,.rpt-ref,.rpt-date{color:#6b7280;font-size:10px}
  .rpt-source{color:#9ca3af;font-size:10px}
  .rpt-date-group{margin-bottom:24px}
  .rpt-date-header{display:flex;align-items:center;justify-content:space-between;background:#1e3a8a;color:white;padding:6px 12px;border-radius:4px;margin-bottom:8px}
  .rpt-date-badge{font-size:12px;font-weight:700;color:white}
  .rpt-date-count{font-size:10px;color:#93c5fd}
  .rpt-footer{margin-top:20px;padding-top:10px;border-top:1px solid #e5e7eb;font-size:9px;color:#9ca3af;text-align:center;line-height:1.6}
  @media print{@page{size:A4;margin:16mm 14mm}body{padding:0}.date-tab-bar{display:none!important}}
`

function openPrintWindow(title) {
  const body = document.getElementById('result-report-print')
  if (!body) return
  const win = window.open('', '_blank', 'width=900,height=700')
  win.document.write(`<!DOCTYPE html><html><head>
    <title>${title}</title>
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=DM+Sans:wght@700;800&display=swap" rel="stylesheet">
    <style>${printStyles}</style>
  </head><body>${body.innerHTML}</body></html>`)
  win.document.close()
  win.onload = () => { win.print(); win.onafterprint = () => win.close() }
}

function printReport() { openPrintWindow(`DX7 Lab Results – ${props.patientName}`) }

async function downloadPdf() {
  const body = document.getElementById('result-report-print')
  if (!body) return
  downloading.value = true

  const filename = `DX7_LabResults_${props.patientName.replace(/[^a-zA-Z0-9]/g, '_')}.pdf`
  const html = `<!DOCTYPE html><html><head>
    <meta charset="utf-8">
    <title>${filename}</title>
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=DM+Sans:wght@700;800&display=swap" rel="stylesheet">
    <style>${printStyles}</style>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/jspdf/2.5.1/jspdf.umd.min.js"><\/script>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js"><\/script>
  </head><body>${body.innerHTML}</body></html>`

  // Use a hidden iframe to render, then trigger jsPDF
  const iframe = document.createElement('iframe')
  iframe.style.cssText = 'position:fixed;top:-9999px;left:-9999px;width:794px;height:1123px;border:none'
  document.body.appendChild(iframe)

  iframe.contentDocument.open()
  iframe.contentDocument.write(html)
  iframe.contentDocument.close()

  // Wait for fonts/render then use html2canvas → jsPDF
  await new Promise(resolve => setTimeout(resolve, 1500))

  try {
    const iDoc = iframe.contentDocument
    // Hide UI-only elements before capture
    const tabBar = iDoc.querySelector('.date-tab-bar')
    if (tabBar) tabBar.style.display = 'none'

    const { jsPDF } = iframe.contentWindow.jspdf
    const canvas = await iframe.contentWindow.html2canvas(iDoc.body, {
      scale: 2,
      useCORS: true,
      windowWidth: 794,
      width: 794,
    })
    const imgData = canvas.toDataURL('image/jpeg', 0.95)
    const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' })
    const pageW = pdf.internal.pageSize.getWidth()
    const pageH = pdf.internal.pageSize.getHeight()
    const imgH = (canvas.height * pageW) / canvas.width

    let yPos = 0
    let remaining = imgH
    while (remaining > 0) {
      pdf.addImage(imgData, 'JPEG', 0, -yPos, pageW, imgH)
      remaining -= pageH
      yPos += pageH
      if (remaining > 0) pdf.addPage()
    }
    const pdfBlob = pdf.output('blob')

    if (window.showSaveFilePicker) {
      try {
        const fileHandle = await window.showSaveFilePicker({
          suggestedName: filename,
          types: [{ description: 'PDF Document', accept: { 'application/pdf': ['.pdf'] } }],
        })
        const writable = await fileHandle.createWritable()
        await writable.write(pdfBlob)
        await writable.close()
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
  } finally {
    document.body.removeChild(iframe)
    downloading.value = false
  }
}
</script>

<style scoped>
.report-modal {
  background: white; border-radius: 12px;
  box-shadow: 0 25px 50px -12px rgba(0,0,0,0.25);
  width: 920px; max-width: calc(100vw - 40px);
  max-height: 90vh; display: flex; flex-direction: column; overflow: hidden;
}
.report-modal-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 18px 24px; border-bottom: 1px solid var(--border); flex-shrink: 0;
}
.report-modal-title { font-family: 'DM Sans', sans-serif; font-size: 17px; font-weight: 700; color: var(--navy); }
.report-modal-sub   { font-size: 12px; color: var(--slate); margin-top: 2px; }
.report-body        { overflow-y: auto; padding: 28px; flex: 1; }

.rpt-header      { display: flex; align-items: flex-start; gap: 20px; margin-bottom: 14px; }
.rpt-logo        { font-family: 'DM Sans', sans-serif; font-size: 28px; font-weight: 800; letter-spacing: -1px; }
.dx7-d { color: #1e3a8a; } .dx7-x { color: #1d4ed8; } .dx7-seven { color: #1e3a8a; }
.rpt-clinic      { flex: 1; padding-top: 4px; }
.rpt-clinic-name { font-size: 15px; font-weight: 700; color: var(--navy); }
.rpt-clinic-sub  { font-size: 12px; color: var(--slate); margin-top: 2px; }
.rpt-meta        { text-align: right; font-size: 11px; color: var(--slate); line-height: 1.7; }
.rpt-meta span   { font-weight: 600; color: var(--navy-mid); }
.rpt-divider     { border: none; border-top: 1.5px solid var(--border); margin: 12px 0; }
.rpt-patient-bar { display: flex; gap: 28px; flex-wrap: wrap; padding: 8px 0; }
.rpt-patient-field { min-width: 120px; }
.rpt-field-label { font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px; color: var(--slate-light); font-weight: 600; }
.rpt-field-value { font-size: 13px; font-weight: 600; color: var(--navy); margin-top: 2px; }
.font-mono       { font-family: 'Courier New', monospace; font-size: 12px; }
.rpt-panel       { margin-bottom: 22px; }
.rpt-panel-title { font-size: 12px; font-weight: 700; color: var(--primary-mid); text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 8px; padding-bottom: 6px; border-bottom: 2px solid var(--primary-pale); }
.rpt-table       { width: 100%; border-collapse: collapse; font-size: 13px; }
.rpt-table thead tr { background: var(--table-head-bg); }
.rpt-table th    { padding: 8px 12px; font-size: 11px; font-weight: 600; color: var(--table-head-color); text-align: left; border-bottom: 1px solid var(--border); white-space: nowrap; }
.rpt-table td    { padding: 9px 12px; border-bottom: 1px solid var(--border-light); vertical-align: middle; color: var(--table-td-color); }
.rpt-table tbody tr:last-child td { border-bottom: none; }
.rpt-table tbody tr:hover { background: var(--off-white); }
.rpt-row-flag    { background: white !important; }
.rpt-test-name   { font-weight: 500; color: var(--navy-mid); }
.rpt-test-code   { font-size: 10px; color: var(--slate-light); margin-top: 1px; }
.rpt-value       { font-weight: 600; font-size: 13px; }
.rpt-h           { color: var(--red); }
.rpt-l           { color: #2563eb; }
.rpt-n           { color: #16a34a; }
.rpt-flag        { display: inline-flex; align-items: center; padding: 2px 8px; border-radius: 20px; font-size: 11px; font-weight: 700; }
.rpt-flag-h      { background: var(--red-pale); color: var(--red); }
.rpt-flag-l      { background: #dbeafe; color: #1d4ed8; }
.rpt-flag-n      { background: #dcfce7; color: #16a34a; }
.rpt-unit        { color: var(--slate); font-size: 12px; }
.rpt-ref         { color: var(--slate); font-size: 11px; }
.rpt-date        { color: var(--slate); font-size: 11px; white-space: nowrap; }
.rpt-source      { color: var(--slate-light); font-size: 11px; }
.rpt-summary-line     { font-size: 12px; color: var(--slate); margin-bottom: 16px; display: flex; align-items: center; gap: 8px; }
.rpt-summary-dot      { color: var(--border); font-weight: 700; }
.rpt-summary-abnormal { color: var(--red); font-weight: 600; }
.rpt-summary-normal   { color: var(--green); font-weight: 600; }
.date-tab-bar    { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 16px; padding-bottom: 12px; border-bottom: 2px solid var(--border); }
.date-tab-btn    { display: flex; align-items: center; gap: 6px; padding: 6px 14px; border-radius: 20px; border: 1.5px solid var(--border); background: white; font-size: 12px; font-weight: 600; color: var(--slate); cursor: pointer; transition: all 0.15s; }
.date-tab-btn:hover { border-color: var(--primary-mid); color: var(--primary-mid); }
.date-tab-btn.active { background: var(--primary-mid); border-color: var(--primary-mid); color: white; }
.date-tab-count  { display: inline-flex; align-items: center; justify-content: center; width: 18px; height: 18px; border-radius: 50%; background: rgba(255,255,255,0.25); font-size: 10px; font-weight: 700; }
.date-tab-btn:not(.active) .date-tab-count { background: var(--border); color: var(--slate); }
.rpt-date-group  { margin-bottom: 28px; }
.rpt-footer      { margin-top: 28px; padding-top: 12px; border-top: 1px solid var(--border); font-size: 11px; color: var(--slate-light); text-align: center; line-height: 1.7; }
</style>